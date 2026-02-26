using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace HardSubExtractor.Services
{
    public class WindowsOcrEngine : IOcrEngine
    {
        private OcrEngine? _engine;
        private readonly string _language;

        public string Name => "Windows Media OCR";

        public WindowsOcrEngine(string languageCode = "en-US")
        {
            _language = languageCode; // e.g., en-US, zh-Hans, ja-JP
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            if (_engine != null) return;

            // Map standard codes to BCP-47 if needed or let user provide valid BCP-47
            var lang = new Windows.Globalization.Language(_language);
            
            if (OcrEngine.IsLanguageSupported(lang))
            {
                _engine = OcrEngine.TryCreateFromLanguage(lang);
            }
            else
            {
                // Fallback to English or default
                _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            }
        }

        public bool IsAvailable()
        {
            return _engine != null;
        }

        public async Task<(string text, float confidence)> RecognizeAsync(Bitmap bitmap)
        {
            if (_engine == null) return (string.Empty, 0);

            try
            {
                // Convert Bitmap to SoftwareBitmap
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Bmp);
                stream.Position = 0;

                var randomAccessStream = stream.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // Ensure format matches what OCR expects (usually Bgra8)
                if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                    softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                {
                    softwareBitmap = SoftwareBitmap.Convert(
                        softwareBitmap, 
                        BitmapPixelFormat.Bgra8, 
                        BitmapAlphaMode.Premultiplied);
                }

                var result = await _engine.RecognizeAsync(softwareBitmap);
                
                if (result.Lines.Count == 0)
                    return (string.Empty, 0);

                // Build text line-by-line for better accuracy
                var lines = new List<string>();
                var allWordConfidences = new List<float>();
                int totalCharCount = 0;

                foreach (var line in result.Lines)
                {
                    var lineWords = new List<string>();
                    foreach (var word in line.Words)
                    {
                        lineWords.Add(word.Text);
                        
                        // Estimate per-word confidence based on multiple heuristics:
                        float wordConf = EstimateWordConfidence(word, line);
                        // Weight by character count so longer words matter more
                        for (int i = 0; i < word.Text.Length; i++)
                            allWordConfidences.Add(wordConf);
                        totalCharCount += word.Text.Length;
                    }
                    lines.Add(string.Join(" ", lineWords));
                }

                var text = string.Join("\n", lines);
                
                // Calculate weighted average confidence
                float avgConfidence = 0;
                if (allWordConfidences.Count > 0)
                {
                    avgConfidence = allWordConfidences.Average();
                }

                // Apply text quality penalties
                avgConfidence = ApplyTextQualityScore(text, avgConfidence);

                return (text, avgConfidence);
            }
            catch
            {
                return (string.Empty, 0);
            }
        }

        /// <summary>
        /// Estimate confidence for a single word based on heuristics
        /// Windows OCR doesn't expose per-word confidence directly,
        /// so we estimate based on word properties
        /// </summary>
        private float EstimateWordConfidence(OcrWord word, OcrLine line)
        {
            float confidence = 85f; // Base confidence when OCR returns something
            string text = word.Text;

            // Bonus for longer recognizable words (less likely to be noise)
            if (text.Length >= 3) confidence += 5f;
            if (text.Length >= 5) confidence += 3f;

            // Penalty for very short "words" (more likely noise)
            if (text.Length == 1)
            {
                // Single character - could be noise
                if (char.IsLetterOrDigit(text[0]))
                    confidence -= 5f; // Still plausible
                else
                    confidence -= 25f; // Likely noise
            }

            // Penalty for words with mostly special characters
            int alphaNum = text.Count(c => char.IsLetterOrDigit(c));
            float alphaRatio = (float)alphaNum / text.Length;
            if (alphaRatio < 0.5f && text.Length > 1)
                confidence -= 15f;

            // Bonus for CJK characters (more reliably detected)
            int cjkCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF || // CJK Unified
                                           c >= 0x3040 && c <= 0x30FF || // Hiragana + Katakana
                                           c >= 0xAC00 && c <= 0xD7AF);  // Hangul
            if (cjkCount > 0)
                confidence += 5f;

            // Penalty for very small bounding box (likely noise or artifact)
            if (word.BoundingRect.Width < 8 || word.BoundingRect.Height < 8)
                confidence -= 20f;

            // Bonus for reasonable aspect ratio of bounding box
            double aspect = word.BoundingRect.Width / Math.Max(1, word.BoundingRect.Height);
            if (aspect >= 0.2 && aspect <= 15)
                confidence += 3f;

            return Math.Clamp(confidence, 0f, 100f);
        }

        /// <summary>
        /// Apply text-level quality score adjustments
        /// </summary>
        private float ApplyTextQualityScore(string text, float baseConfidence)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            float confidence = baseConfidence;

            // Very short text is less reliable
            if (text.Length < 2) confidence *= 0.5f;
            else if (text.Length < 4) confidence *= 0.7f;

            // All-punctuation text is noise
            int letters = text.Count(c => char.IsLetterOrDigit(c));
            if (letters == 0) return 5f;

            float letterRatio = (float)letters / text.Replace(" ", "").Replace("\n", "").Length;
            if (letterRatio < 0.3f) confidence *= 0.4f;
            else if (letterRatio < 0.5f) confidence *= 0.7f;

            // Repetitive single characters (OCR artifact)
            if (text.Distinct().Count() <= 2 && text.Length > 3)
                confidence *= 0.3f;

            return Math.Clamp(confidence, 0f, 100f);
        }

        public void Dispose()
        {
            // Windows OCR Engine doesn't implement IDisposable
        }
    }
}
