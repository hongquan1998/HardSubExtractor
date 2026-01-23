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
    /// </summary>
    public class OcrService : IDisposable
    {
        private IOcrEngine? _engine;
        private static int _debugCounter = 0;
        private static bool _debugMode = false;
        private static readonly object _debugLock = new object();
        
        // Configurable settings
        public PreprocessMode CurrentMode { get; set; } = PreprocessMode.Auto;
        public float MinConfidence { get; set; } = 40f;
        public bool UseMorphology { get; set; } = true;

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
        /// </summary>
        private (string text, float confidence) RecognizeTextMultiPass(Bitmap bitmap, int debugCounter)
        {
            var results = new List<(string text, float confidence, PreprocessMode mode)>();
            
            // Try each preprocessing method
            var modesToTry = new[] { PreprocessMode.Otsu, PreprocessMode.Adaptive, PreprocessMode.ColorBased };
            
            foreach (var mode in modesToTry)
            {
                try
                {
                    using var preprocessed = PreprocessImage(bitmap, mode);
                    SaveDebugImage(preprocessed, debugCounter, mode.ToString());
                    
                    var (text, conf) = RecognizeWithConfidence(preprocessed);
                    
                    if (!string.IsNullOrWhiteSpace(text) && conf >= MinConfidence)
                    {
                        results.Add((text, conf, mode));
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
                // Fallback: try inverted
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
            
            // Return result with highest confidence
            var best = results.OrderByDescending(r => r.confidence).First();
            
            if (_debugMode)
            {
                Console.WriteLine($"[OCR] Best: {best.mode} @ {best.confidence:F1}% - '{best.text.Trim().Replace("\n", " ").Substring(0, Math.Min(50, best.text.Length))}'");
            }
            
            return (best.text, best.confidence);
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
        /// Main preprocessing dispatcher
        /// </summary>
        private Bitmap PreprocessImage(Bitmap original, PreprocessMode mode)
        {
            // Step 1: Upscale if too small
            var scaled = UpscaleImage(original, minWidth: 800);
            
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
            
            // Apply morphology if enabled
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
        /// Otsu preprocessing (original method - best for high contrast)
        /// </summary>
        private Bitmap PreprocessOtsu(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            var contrasted = AdjustContrastOptimized(grayscale, contrast: 1.8f);
            var binarized = ApplyOtsuThresholdOptimized(contrasted);
            
            grayscale.Dispose();
            contrasted.Dispose();
            
            return binarized;
        }

        /// <summary>
        /// Adaptive threshold preprocessing (better for varied lighting)
        /// </summary>
        private unsafe Bitmap PreprocessAdaptive(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            
            // Apply local adaptive thresholding
            var result = new Bitmap(grayscale.Width, grayscale.Height, PixelFormat.Format24bppRgb);
            
            var rect = new Rectangle(0, 0, grayscale.Width, grayscale.Height);
            var srcData = grayscale.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            try
            {
                byte* ptrSrc = (byte*)srcData.Scan0;
                byte* ptrDst = (byte*)dstData.Scan0;
                
                int stride = srcData.Stride;
                int width = grayscale.Width;
                int height = grayscale.Height;
                int blockSize = 15; // Local window size
                int C = 8; // Threshold constant
                
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // Calculate local mean
                        int sum = 0;
                        int count = 0;
                        
                        int yStart = Math.Max(0, y - blockSize / 2);
                        int yEnd = Math.Min(height - 1, y + blockSize / 2);
                        int xStart = Math.Max(0, x - blockSize / 2);
                        int xEnd = Math.Min(width - 1, x + blockSize / 2);
                        
                        for (int by = yStart; by <= yEnd; by++)
                        {
                            byte* blockRow = ptrSrc + (by * stride);
                            for (int bx = xStart; bx <= xEnd; bx++)
                            {
                                sum += blockRow[bx * 3];
                                count++;
                            }
                        }
                        
                        int threshold = (sum / count) - C;
                        byte* srcRow = ptrSrc + (y * stride);
                        byte* dstRow = ptrDst + (y * stride);
                        
                        byte pixelValue = srcRow[x * 3];
                        byte outputValue = pixelValue > threshold ? (byte)255 : (byte)0;
                        
                        dstRow[x * 3] = outputValue;
                        dstRow[x * 3 + 1] = outputValue;
                        dstRow[x * 3 + 2] = outputValue;
                    }
                }
            }
            finally
            {
                grayscale.UnlockBits(srcData);
                result.UnlockBits(dstData);
            }
            
            grayscale.Dispose();
            return result;
        }

        /// <summary>
        /// Color-based preprocessing (detects white/light text - most subtitles)
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
                
                int stride = srcData.Stride;
                int width = original.Width;
                int height = original.Height;
                
                // Thresholds for detecting white/light colored text (most subtitles)
                const int brightnessThreshold = 180; // Pixels brighter than this = text
                const int saturationThreshold = 60; // Low saturation = white/gray
                
                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = ptrSrc + (y * stride);
                    byte* dstRow = ptrDst + (y * stride);
                    
                    for (int x = 0; x < width; x++)
                    {
                        int idx = x * 3;
                        int b = srcRow[idx];
                        int g = srcRow[idx + 1];
                        int r = srcRow[idx + 2];
                        
                        // Calculate brightness (value in HSV)
                        int maxVal = Math.Max(Math.Max(r, g), b);
                        int minVal = Math.Min(Math.Min(r, g), b);
                        
                        // Calculate saturation
                        int saturation = maxVal == 0 ? 0 : (maxVal - minVal) * 255 / maxVal;
                        
                        // Check if pixel is white/light colored text
                        bool isWhiteText = maxVal >= brightnessThreshold && saturation <= saturationThreshold;
                        
                        // Also check for yellow text (common in Asian subtitles)
                        // Yellow: high R, high G, low B
                        bool isYellowText = r >= 200 && g >= 180 && b < 120;
                        
                        byte outputValue = (isWhiteText || isYellowText) ? (byte)255 : (byte)0;
                        
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
            
            return result;
        }

        /// <summary>
        /// Invert preprocessing (for dark text on light background)
        /// </summary>
        private Bitmap PreprocessInvert(Bitmap original)
        {
            var grayscale = ConvertToGrayscaleOptimized(original);
            var contrasted = AdjustContrastOptimized(grayscale, contrast: 1.8f);
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
        /// Upscale ảnh nếu quá nhỏ
        /// </summary>
        private Bitmap UpscaleImage(Bitmap original, int minWidth = 800)
        {
            if (original.Width >= minWidth)
                return original;
            
            float scale = (float)minWidth / original.Width;
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
        /// Tăng contrast - OPTIMIZED with unsafe code
        /// </summary>
        private unsafe Bitmap AdjustContrastOptimized(Bitmap original, float contrast = 1.8f)
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
        /// Chuẩn hóa text OCR (trim, remove noise)
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

            // Remove các ký tự rác thường gặp
            var result = string.Join("\n", lines);
            
            // Normalize quotes and dashes
            result = result.Replace("—", "-")
                          .Replace("–", "-")
                          .Replace("\u201C", "\"")  // Left double quote
                          .Replace("\u201D", "\"")  // Right double quote
                          .Replace("\u2018", "'")   // Left single quote
                          .Replace("\u2019", "'")   // Right single quote
                          .Replace("…", "...");

            // Remove ký tự không in được (keep newlines)
            result = new string(result.Where(c => !char.IsControl(c) || c == '\n').ToArray());

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
