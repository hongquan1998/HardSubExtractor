using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Concurrent;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Preprocessing mode for OCR optimization
    /// </summary>
    public enum PreprocessMode
    {
        Auto,           // Try multiple methods, pick best result
        Otsu,           // Binary threshold (high contrast subtitles)
        Adaptive,       // Adaptive threshold (varied lighting)
        ColorBased,     // Detect white/yellow text specifically
        Invert          // For dark text on light background
    }

    /// <summary>
    /// Service OCR Strategy Context - Handles preprocessing and delegates to Engine
    /// REWRITTEN v2.0 - Improved preprocessing for accurate subtitle OCR
    /// </summary>
    public class OcrService : IDisposable
    {
        private IOcrEngine? _engine;
        private static int _debugCounter = 0;
        private static bool _debugMode = false;
        private static readonly object _debugLock = new object();
        
        // Configurable settings
        public PreprocessMode CurrentMode { get; set; } = PreprocessMode.Auto;
        public float MinConfidence { get; set; } = 20f;
        public bool UseMorphology { get; set; } = false; // Disabled by default - often destroys text

        public OcrService(IOcrEngine engine)
        {
            _engine = engine;
        }

        /// <summary>
        /// Enable debug mode to save preprocessed images
        /// </summary>
        public static void EnableDebugMode(bool enable = true)
        {
            _debugMode = enable;
            if (enable)
            {
                _debugCounter = 0;
            }
        }

        /// <summary>
        /// OCR một ảnh từ file path
        /// </summary>
        public string RecognizeText(string imagePath)
        {
            if (_engine == null)
                return string.Empty;

            try
            {
                using var bitmap = new Bitmap(imagePath);
                return RecognizeText(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR lỗi tại {imagePath}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// OCR một ảnh từ bitmap (sau khi crop ROI) - VỚI MULTIPLE PREPROCESSING
        /// </summary>
        public string RecognizeText(Bitmap bitmap)
        {
            if (_engine == null)
                return string.Empty;

            try
            {
                int currentCounter = 0;
                if (_debugMode)
                {
                    lock (_debugLock)
                    {
                        currentCounter = ++_debugCounter;
                        var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
                        Directory.CreateDirectory(debugFolder);
                        bitmap.Save(Path.Combine(debugFolder, $"0_original_{currentCounter:D4}.png"));
                    }
                }

                // Use Auto mode by default - tries multiple methods
                if (CurrentMode == PreprocessMode.Auto)
                {
                    var (text, confidence) = RecognizeTextMultiPass(bitmap, currentCounter);
                    return text;
                }
                else
                {
                    using var preprocessed = PreprocessImage(bitmap, CurrentMode);
                    SaveDebugImage(preprocessed, currentCounter, CurrentMode.ToString());
                    return RecognizeFromBitmap(preprocessed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR lỗi: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Multi-pass OCR - tries multiple preprocessing methods and returns best result
        /// v2.0: Uses REAL confidence scores and text quality metrics
        /// </summary>
        private (string text, float confidence) RecognizeTextMultiPass(Bitmap bitmap, int debugCounter)
        {
            var results = new List<(string text, float confidence, PreprocessMode mode)>();
            
            // Strategy: Try ALL methods and pick the best result
            // v5: Don't exit early to ensure best possible result
            var modesToTry = new[] { PreprocessMode.ColorBased, PreprocessMode.Otsu, PreprocessMode.Adaptive };
            
            foreach (var mode in modesToTry)
            {
                try
                {
                    using var preprocessed = PreprocessImage(bitmap, mode);
                    SaveDebugImage(preprocessed, debugCounter, mode.ToString());
                    
                    var (text, conf) = RecognizeWithConfidence(preprocessed);
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Apply text quality bonus/penalty
                        float qualityScore = CalculateTextQuality(text);
                        float adjustedConf = conf * 0.7f + qualityScore * 30f;
                        
                        results.Add((text, adjustedConf, mode));
                        
                        // v5: Only early exit on near-perfect results (>= 92)
                        if (adjustedConf >= 92f && text.Length >= 3)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    if (_debugMode)
                        Console.WriteLine($"[OCR] {mode} failed: {ex.Message}");
                }
            }
            
            if (results.Count == 0)
            {
                // Fallback: try with no preprocessing (raw upscaled image)
                try
                {
                    using var upscaled = UpscaleImage(bitmap, minWidth: 1200);
                    SaveDebugImage(upscaled, debugCounter, "Raw");
                    var (text, conf) = RecognizeWithConfidence(upscaled);
                    if (!string.IsNullOrWhiteSpace(text))
                        return (text, conf);
                    
                    // Also try upscaled original (no changes)
                    if (upscaled != bitmap)
                        upscaled.Dispose();
                }
                catch { }
                
                // Last resort: try inverted
                try
                {
                    using var inverted = PreprocessImage(bitmap, PreprocessMode.Invert);
                    SaveDebugImage(inverted, debugCounter, "Invert");
                    var (text, conf) = RecognizeWithConfidence(inverted);
                    if (!string.IsNullOrWhiteSpace(text))
                        return (text, conf);
                }
                catch { }
                
                return (string.Empty, 0);
            }
            
            // Return result with highest adjusted confidence
            var best = results.OrderByDescending(r => r.confidence).First();
            
            if (_debugMode)
            {
                var preview = best.text.Trim().Replace("\n", " ");
                if (preview.Length > 60) preview = preview.Substring(0, 60) + "...";
                Console.WriteLine($"[OCR] Best: {best.mode} @ {best.confidence:F1}% - '{preview}'");
                
                // Log all results for comparison
                foreach (var r in results.OrderByDescending(x => x.confidence))
                {
                    var p = r.text.Trim().Replace("\n", " ");
                    if (p.Length > 40) p = p.Substring(0, 40) + "...";
                    Console.WriteLine($"  [{r.mode}] {r.confidence:F1}% - '{p}'");
                }
            }
            
            return (best.text, best.confidence);
        }

        /// <summary>
        /// Calculate text quality score (0-1) based on heuristics
        /// </summary>
        private float CalculateTextQuality(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0f;

            float score = 0.5f;
            var cleanText = text.Replace(" ", "").Replace("\n", "");
            
            // CJK text detection first (affects other scoring)
            int cjkCount = cleanText.Count(c => c >= 0x4E00 && c <= 0x9FFF || 
                                                 c >= 0x3040 && c <= 0x30FF || 
                                                 c >= 0xAC00 && c <= 0xD7AF);
            bool hasCjk = cjkCount > 0;
            
            // Ratio of letters/digits (good text has high ratio)
            int alphaNum = cleanText.Count(c => char.IsLetterOrDigit(c));
            float alphaRatio = cleanText.Length > 0 ? (float)alphaNum / cleanText.Length : 0;
            
            if (hasCjk)
            {
                // CJK: characters are all "letters" so ratio is naturally high
                score += 0.2f;
                // CJK text is generally more reliable when detected
                score += Math.Min(0.15f, cjkCount * 0.03f);
            }
            else
            {
                score += (alphaRatio - 0.5f) * 0.3f;
            }
            
            // Bonus for reasonable text length (at least 2 chars)
            if (cleanText.Length >= 2 && cleanText.Length <= 200)
                score += 0.1f;
            else if (cleanText.Length == 1 && hasCjk)
                score += 0.05f; // Single CJK char can still be valid
            
            // Penalty for too many unique special chars (noise)
            var specialChars = cleanText.Where(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)).Distinct().Count();
            if (specialChars > 6)
                score -= 0.12f;
            else if (specialChars > 3)
                score -= 0.05f;
            
            // Penalty for repetitive patterns (OCR artifact)
            if (cleanText.Length > 3)
            {
                int distinctChars = cleanText.Distinct().Count();
                if (distinctChars <= 1) score -= 0.35f; // Truly degenerate
                else if (distinctChars <= 2 && !hasCjk) score -= 0.20f;
                else if ((float)distinctChars / cleanText.Length < 0.15f && !hasCjk) score -= 0.10f;
            }

            return Math.Clamp(score, 0f, 1f);
        }

        /// <summary>
        /// OCR with confidence score
        /// </summary>
        private (string text, float confidence) RecognizeWithConfidence(Bitmap bitmap)
        {
            if (_engine == null) return (string.Empty, 0f);
            var result = _engine.RecognizeAsync(bitmap).GetAwaiter().GetResult();
            return (NormalizeText(result.text), result.confidence);
        }

        /// <summary>
        /// Simple OCR from bitmap
        /// </summary>
        private string RecognizeFromBitmap(Bitmap bitmap)
        {
            var (text, _) = RecognizeWithConfidence(bitmap);
            return text;
        }

        /// <summary>
        /// Save debug image if debug mode is on
        /// </summary>
        private void SaveDebugImage(Bitmap bitmap, int counter, string modeName)
        {
            if (!_debugMode || counter == 0) return;
            
            lock (_debugLock)
            {
                var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
                Directory.CreateDirectory(debugFolder);
                bitmap.Save(Path.Combine(debugFolder, $"1_{modeName}_{counter:D4}.png"));
            }
        }

        /// <summary>
        /// Main preprocessing dispatcher - v2.0
        /// </summary>
        private Bitmap PreprocessImage(Bitmap original, PreprocessMode mode)
        {
            // Step 1: Upscale if too small (minimum 1200px for good OCR)
            var scaled = UpscaleImage(original, minWidth: 1200);
            
            Bitmap processed;
            
            switch (mode)
            {
                case PreprocessMode.Otsu:
                    processed = PreprocessOtsu(scaled);
                    break;
                    
                case PreprocessMode.Adaptive:
                    processed = PreprocessAdaptive(scaled);
                    break;
                    
                case PreprocessMode.ColorBased:
                    processed = PreprocessColorBased(scaled);
                    break;
                    
                case PreprocessMode.Invert:
                    processed = PreprocessInvert(scaled);
                    break;
                    
                default:
                    processed = PreprocessOtsu(scaled);
                    break;
            }
            
            // Apply morphology only if explicitly enabled
            if (UseMorphology)
            {
                var morphed = ApplyMorphology(processed);
                processed.Dispose();
                processed = morphed;
            }
            
            // Cleanup
            if (scaled != original) scaled.Dispose();
            
            return processed;
        }

        /// <summary>
        /// Otsu preprocessing - v2.0 with gentler contrast
        /// </summary>
        private Bitmap PreprocessOtsu(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            // Use gentler contrast (1.4 instead of 1.8) to avoid destroying thin text
            var contrasted = AdjustContrastOptimized(grayscale, contrast: 1.4f);
            var binarized = ApplyOtsuThresholdOptimized(contrasted);
            
            grayscale.Dispose();
            contrasted.Dispose();
            
            return binarized;
        }

        /// <summary>
        /// Adaptive threshold preprocessing - v2.0 using integral image for speed
        /// </summary>
        private unsafe Bitmap PreprocessAdaptive(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            
            int width = grayscale.Width;
            int height = grayscale.Height;
            
            // Build integral image for fast local mean computation
            var rect = new Rectangle(0, 0, width, height);
            var srcData = grayscale.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            
            // Extract grayscale values and build integral image
            long[,] integral = new long[height + 1, width + 1];
            
            byte* ptrSrc = (byte*)srcData.Scan0;
            int stride = srcData.Stride;
            
            for (int y = 0; y < height; y++)
            {
                byte* row = ptrSrc + y * stride;
                long rowSum = 0;
                for (int x = 0; x < width; x++)
                {
                    rowSum += row[x * 3];
                    integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
                }
            }
            
            // Apply adaptive threshold using integral image
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            byte* ptrDst = (byte*)dstData.Scan0;
            int dstStride = dstData.Stride;
            
            // Adaptive block size based on image dimensions
            int blockSize = Math.Max(15, Math.Min(51, width / 20));
            if (blockSize % 2 == 0) blockSize++; // Must be odd
            int halfBlock = blockSize / 2;
            int C = 10; // Threshold constant - slightly higher to be more selective
            
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = ptrSrc + y * stride;
                byte* dstRow = ptrDst + y * dstStride;
                
                for (int x = 0; x < width; x++)
                {
                    // Calculate local mean using integral image - O(1) per pixel
                    int y1 = Math.Max(0, y - halfBlock);
                    int y2 = Math.Min(height, y + halfBlock + 1);
                    int x1 = Math.Max(0, x - halfBlock);
                    int x2 = Math.Min(width, x + halfBlock + 1);
                    
                    long sum = integral[y2, x2] - integral[y1, x2] - integral[y2, x1] + integral[y1, x1];
                    int count = (y2 - y1) * (x2 - x1);
                    int localMean = (int)(sum / count);
                    
                    byte pixelValue = srcRow[x * 3];
                    // Pixel is text (white) if it's brighter than local mean
                    byte outputValue = pixelValue > (localMean - C) ? (byte)255 : (byte)0;
                    
                    dstRow[x * 3] = outputValue;
                    dstRow[x * 3 + 1] = outputValue;
                    dstRow[x * 3 + 2] = outputValue;
                }
            }
            
            grayscale.UnlockBits(srcData);
            result.UnlockBits(dstData);
            grayscale.Dispose();
            
            return result;
        }

        /// <summary>
        /// Color-based preprocessing - v2.0 with wider thresholds and multi-color support
        /// Detects white, yellow, cyan, and light-colored text (covers most subtitle styles)
        /// </summary>
        private unsafe Bitmap PreprocessColorBased(Bitmap original)
        {
            var result = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var srcData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrSrc = (byte*)srcData.Scan0;
                byte* ptrDst = (byte*)dstData.Scan0;
                
                int srcStride = srcData.Stride;
                int dstStride = dstData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = ptrSrc + (y * srcStride);
                    byte* dstRow = ptrDst + (y * dstStride);
                    
                    for (int x = 0; x < width; x++)
                    {
                        int idx = x * 3;
                        int b = srcRow[idx];
                        int g = srcRow[idx + 1];
                        int r = srcRow[idx + 2];
                        
                        // Calculate brightness (value in HSV)
                        int maxVal = Math.Max(Math.Max(r, g), b);
                        int minVal = Math.Min(Math.Min(r, g), b);
                        
                        // Calculate saturation (0-255 scale)
                        int saturation = maxVal == 0 ? 0 : (maxVal - minVal) * 255 / maxVal;
                        
                        // Calculate average brightness
                        int brightness = (r + g + b) / 3;
                        
                        bool isTextPixel = false;
                        
                        // 1. White/near-white text (most common subtitle color)
                        //    Lower threshold from 180 to 150 to catch slightly dimmer text
                        if (brightness >= 150 && saturation <= 80)
                        {
                            isTextPixel = true;
                        }
                        
                        // 2. Very bright text (any color above 200 brightness)
                        else if (maxVal >= 200 && saturation <= 50)
                        {
                            isTextPixel = true;
                        }
                        
                        // 3. Yellow text (common in Chinese/Japanese subtitles)
                        //    R >= 180, G >= 160, B < 130
                        else if (r >= 180 && g >= 160 && b < 130 && (r + g) > 360)
                        {
                            isTextPixel = true;
                        }
                        
                        // 4. Cyan text (some subtitle styles)
                        //    R < 130, G >= 180, B >= 180
                        else if (r < 130 && g >= 180 && b >= 180)
                        {
                            isTextPixel = true;
                        }
                        
                        // 5. Light green text (uncommon but exists)
                        else if (g >= 200 && r >= 150 && b < 130)
                        {
                            isTextPixel = true;
                        }
                        
                        // 6. High contrast text detection via local gradient
                        //    Check if pixel is significantly brighter than it would be if part of background
                        else if (brightness >= 130 && saturation <= 100)
                        {
                            // Check contrast with immediate neighbors
                            if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
                            {
                                int leftIdx = y * srcStride + (x - 1) * 3;
                                int rightIdx = y * srcStride + (x + 1) * 3;
                                int topIdx = (y - 1) * srcStride + x * 3;
                                int bottomIdx = (y + 1) * srcStride + x * 3;
                                
                                int leftBright = (ptrSrc[leftIdx] + ptrSrc[leftIdx + 1] + ptrSrc[leftIdx + 2]) / 3;
                                int rightBright = (ptrSrc[rightIdx] + ptrSrc[rightIdx + 1] + ptrSrc[rightIdx + 2]) / 3;
                                int topBright = (ptrSrc[topIdx] + ptrSrc[topIdx + 1] + ptrSrc[topIdx + 2]) / 3;
                                int bottomBright = (ptrSrc[bottomIdx] + ptrSrc[bottomIdx + 1] + ptrSrc[bottomIdx + 2]) / 3;
                                
                                int avgNeighbor = (leftBright + rightBright + topBright + bottomBright) / 4;
                                
                                // If this pixel is significantly brighter than neighbors, it could be text
                                if (brightness - avgNeighbor > 40)
                                    isTextPixel = true;
                            }
                        }
                        
                        byte outputValue = isTextPixel ? (byte)255 : (byte)0;
                        
                        dstRow[idx] = outputValue;
                        dstRow[idx + 1] = outputValue;
                        dstRow[idx + 2] = outputValue;
                    }
                }
            }
            finally
            {
                original.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }
            
            // Apply small closing operation (dilate then erode) to fill gaps in characters
            var closed = CloseSmallGaps(result, 1);
            result.Dispose();
            
            return closed;
        }

        /// <summary>
        /// Close small gaps in text - dilate then erode with small kernel
        /// This fills small holes in characters without merging separate characters
        /// </summary>
        private Bitmap CloseSmallGaps(Bitmap input, int radius)
        {
            var dilated = Dilate(input, radius);
            var eroded = Erode(dilated, radius);
            dilated.Dispose();
            return eroded;
        }

        /// <summary>
        /// Invert preprocessing (for dark text on light background)
        /// </summary>
        private Bitmap PreprocessInvert(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            var contrasted = AdjustContrastOptimized(grayscale, contrast: 1.4f);
            var binarized = ApplyOtsuThresholdOptimized(contrasted);
            
            // Invert the result
            var rect = new Rectangle(0, 0, binarized.Width, binarized.Height);
            var data = binarized.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                int bytes = data.Stride * binarized.Height;
                
                for (int i = 0; i < bytes; i++)
                {
                    ptr[i] = (byte)(255 - ptr[i]);
                }
            }
            
            binarized.UnlockBits(data);
            
            grayscale.Dispose();
            contrasted.Dispose();
            
            return binarized;
        }

        /// <summary>
        /// Morphological operations to improve text clarity
        /// </summary>
        private unsafe Bitmap ApplyMorphology(Bitmap original)
        {
            // Apply dilation to thicken thin text, then slight erosion to clean up
            var dilated = Dilate(original, 1);
            var eroded = Erode(dilated, 1);
            dilated.Dispose();
            return eroded;
        }

        private unsafe Bitmap Dilate(Bitmap original, int radius)
        {
            var result = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var srcData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrSrc = (byte*)srcData.Scan0;
                byte* ptrDst = (byte*)dstData.Scan0;
                
                int stride = srcData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte maxVal = 0;
                        
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int ny = Math.Clamp(y + dy, 0, height - 1);
                                int nx = Math.Clamp(x + dx, 0, width - 1);
                                
                                byte* srcRow = ptrSrc + (ny * stride);
                                maxVal = Math.Max(maxVal, srcRow[nx * 3]);
                            }
                        }
                        
                        byte* dstRow = ptrDst + (y * stride);
                        dstRow[x * 3] = maxVal;
                        dstRow[x * 3 + 1] = maxVal;
                        dstRow[x * 3 + 2] = maxVal;
                    }
                }
            }
            finally
            {
                original.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }
            
            return result;
        }

        private unsafe Bitmap Erode(Bitmap original, int radius)
        {
            var result = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var srcData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrSrc = (byte*)srcData.Scan0;
                byte* ptrDst = (byte*)dstData.Scan0;
                
                int stride = srcData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte minVal = 255;
                        
                        for (int dy = -radius; dy <= radius; dy++)
                        {
                            for (int dx = -radius; dx <= radius; dx++)
                            {
                                int ny = Math.Clamp(y + dy, 0, height - 1);
                                int nx = Math.Clamp(x + dx, 0, width - 1);
                                
                                byte* srcRow = ptrSrc + (ny * stride);
                                minVal = Math.Min(minVal, srcRow[nx * 3]);
                            }
                        }
                        
                        byte* dstRow = ptrDst + (y * stride);
                        dstRow[x * 3] = minVal;
                        dstRow[x * 3 + 1] = minVal;
                        dstRow[x * 3 + 2] = minVal;
                    }
                }
            }
            finally
            {
                original.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }
            
            return result;
        }

        /// <summary>
        /// Upscale ảnh nếu quá nhỏ - v2.0 with higher minimum
        /// </summary>
        private Bitmap UpscaleImage(Bitmap original, int minWidth = 1200)
        {
            if (original.Width >= minWidth)
                return original;
            
            float scale = (float)minWidth / original.Width;
            
            // Cap scale to avoid extreme upscaling (which adds noise)
            scale = Math.Min(scale, 4.0f);
            
            int newWidth = (int)(original.Width * scale);
            int newHeight = (int)(original.Height * scale);
            
            var upscaled = new Bitmap(newWidth, newHeight);
            using (var g = Graphics.FromImage(upscaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(original, 0, 0, newWidth, newHeight);
            }
            
            return upscaled;
        }

        /// <summary>
        /// Convert to grayscale - OPTIMIZED with unsafe code
        /// </summary>
        private unsafe Bitmap ConvertToGrayscaleOptimized(Bitmap original)
        {
            var grayscale = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var originalData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var grayscaleData = grayscale.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrOriginal = (byte*)originalData.Scan0;
                byte* ptrGrayscale = (byte*)grayscaleData.Scan0;
                
                int stride = originalData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                for (int y = 0; y < height; y++)
                {
                    byte* rowOriginal = ptrOriginal + (y * stride);
                    byte* rowGrayscale = ptrGrayscale + (y * stride);
                    
                    for (int x = 0; x < width; x++)
                    {
                        int idx = x * 3;
                        // Grayscale = 0.299R + 0.587G + 0.114B (integer math for speed)
                        byte gray = (byte)((rowOriginal[idx + 2] * 77 + rowOriginal[idx + 1] * 150 + rowOriginal[idx] * 29) >> 8);
                        rowGrayscale[idx] = gray;
                        rowGrayscale[idx + 1] = gray;
                        rowGrayscale[idx + 2] = gray;
                    }
                }
            }
            finally
            {
                original.UnlockBits(originalData);
                grayscale.UnlockBits(grayscaleData);
            }
            
            return grayscale;
        }

        /// <summary>
        /// Tăng contrast - OPTIMIZED with unsafe code - v2.0 with gentler default
        /// </summary>
        private unsafe Bitmap AdjustContrastOptimized(Bitmap original, float contrast = 1.4f)
        {
            var adjusted = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var originalData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var adjustedData = adjusted.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrOriginal = (byte*)originalData.Scan0;
                byte* ptrAdjusted = (byte*)adjustedData.Scan0;
                
                // Pre-calculate lookup table
                byte[] lut = new byte[256];
                float factor = contrast;
                float offset = 128 * (1 - contrast);
                
                for (int i = 0; i < 256; i++)
                {
                    int value = (int)(i * factor + offset);
                    lut[i] = (byte)Math.Clamp(value, 0, 255);
                }
                
                int stride = originalData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                for (int y = 0; y < height; y++)
                {
                    byte* rowOriginal = ptrOriginal + (y * stride);
                    byte* rowAdjusted = ptrAdjusted + (y * stride);
                    
                    for (int x = 0; x < width * 3; x++)
                    {
                        rowAdjusted[x] = lut[rowOriginal[x]];
                    }
                }
            }
            finally
            {
                original.UnlockBits(originalData);
                adjusted.UnlockBits(adjustedData);
            }
            
            return adjusted;
        }

        /// <summary>
        /// Otsu's binarization - OPTIMIZED with unsafe code
        /// </summary>
        private unsafe Bitmap ApplyOtsuThresholdOptimized(Bitmap original)
        {
            var binarized = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, original.Width, original.Height);
            var originalData = original.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var binarizedData = binarized.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrOriginal = (byte*)originalData.Scan0;
                byte* ptrBinarized = (byte*)binarizedData.Scan0;
                
                int stride = originalData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                // Calculate histogram
                int[] histogram = new int[256];
                for (int y = 0; y < height; y++)
                {
                    byte* row = ptrOriginal + (y * stride);
                    for (int x = 0; x < width; x++)
                    {
                        histogram[row[x * 3]]++;
                    }
                }
                
                // Calculate Otsu threshold
                int total = width * height;
                float sum = 0;
                for (int i = 0; i < 256; i++)
                    sum += i * histogram[i];
                
                float sumB = 0;
                int wB = 0;
                float varMax = 0;
                int threshold = 128; // Default
                
                for (int t = 0; t < 256; t++)
                {
                    wB += histogram[t];
                    if (wB == 0) continue;
                    
                    int wF = total - wB;
                    if (wF == 0) break;
                    
                    sumB += t * histogram[t];
                    
                    float mB = sumB / wB;
                    float mF = (sum - sumB) / wF;
                    
                    float varBetween = (float)wB * wF * (mB - mF) * (mB - mF);
                    
                    if (varBetween > varMax)
                    {
                        varMax = varBetween;
                        threshold = t;
                    }
                }
                
                // Apply threshold
                for (int y = 0; y < height; y++)
                {
                    byte* rowOriginal = ptrOriginal + (y * stride);
                    byte* rowBinarized = ptrBinarized + (y * stride);
                    
                    for (int x = 0; x < width; x++)
                    {
                        int idx = x * 3;
                        byte value = rowOriginal[idx] > threshold ? (byte)255 : (byte)0;
                        rowBinarized[idx] = value;
                        rowBinarized[idx + 1] = value;
                        rowBinarized[idx + 2] = value;
                    }
                }
            }
            finally
            {
                original.UnlockBits(originalData);
                binarized.UnlockBits(binarizedData);
            }
            
            return binarized;
        }

        /// <summary>
        /// Chuẩn hóa text OCR (trim, remove noise) - v2.0 improved for CJK
        /// </summary>
        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Trim mỗi dòng và remove empty lines
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrWhiteSpace(l))
                           .ToList();

            if (lines.Count == 0)
                return string.Empty;

            // Join lines
            var result = string.Join("\n", lines);
            
            // Normalize common OCR mistakes for punctuation
            result = result.Replace("\u201C", "\"")  // Left double quote
                          .Replace("\u201D", "\"")  // Right double quote
                          .Replace("\u2018", "'")   // Left single quote
                          .Replace("\u2019", "'")   // Right single quote
                          .Replace("\u2026", "...");  // Ellipsis

            // Remove ký tự không in được (keep newlines)
            result = new string(result.Where(c => !char.IsControl(c) || c == '\n').ToArray());

            // Remove common OCR noise patterns
            // Single pipes/bars that aren't meaningful
            result = System.Text.RegularExpressions.Regex.Replace(result, @"^\|+\s*", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s*\|+$", "");
            
            return result.Trim();
        }

        /// <summary>
        /// Crop ảnh theo ROI (Region of Interest)
        /// </summary>
        public static Bitmap CropImage(string imagePath, Rectangle roi)
        {
            using var originalImage = new Bitmap(imagePath);
            return CropImage(originalImage, roi);
        }

        /// <summary>
        /// Crop bitmap theo ROI
        /// </summary>
        public static Bitmap CropImage(Bitmap originalImage, Rectangle roi)
        {
            // Đảm bảo ROI nằm trong ảnh
            var actualRoi = Rectangle.Intersect(roi, new Rectangle(0, 0, originalImage.Width, originalImage.Height));
            
            if (actualRoi.Width <= 0 || actualRoi.Height <= 0)
                throw new ArgumentException("ROI không hợp lệ");

            var croppedImage = new Bitmap(actualRoi.Width, actualRoi.Height);
            using (var g = Graphics.FromImage(croppedImage))
            {
                g.DrawImage(originalImage, 
                    new Rectangle(0, 0, actualRoi.Width, actualRoi.Height),
                    actualRoi, 
                    GraphicsUnit.Pixel);
            }

            return croppedImage;
        }

        /// <summary>
        /// Kiểm tra OCR Engine có sẵn không
        /// </summary>
        public bool IsAvailable()
        {
            return _engine != null && _engine.IsAvailable();
        }

        public void Dispose()
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
