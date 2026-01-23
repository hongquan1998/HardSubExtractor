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

                // Reconstruct text
                var text = result.Text;
                
                // Estimate confidence (Windows OCR doesn't give per-text confidence directly like Tesseract)
                // But usually if it detects something, it's confident.
                // We'll return 100% or heuristic based on text length/structure if needed.
                // Actually, Word gives confidence? No, it gives Rect.
                
                // Let's assume 90% default if text found, 0 if not
                return (text, 90f);
            }
            catch
            {
                return (string.Empty, 0);
            }
        }

        public void Dispose()
        {
            // Windows OCR Engine doesn't implement IDisposable
        }
    }
}
