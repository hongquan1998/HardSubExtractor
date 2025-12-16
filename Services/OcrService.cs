using System.Drawing;
using System.Drawing.Imaging;
using Tesseract;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service OCR sử dụng Tesseract - OPTIMIZED v2
    /// </summary>
    public class OcrService : IDisposable
    {
        private TesseractEngine? _engine;
        private readonly string _tessDataPath;
        private readonly string _language;
        private static int _debugCounter = 0;
        private static bool _debugMode = false;
        private static readonly object _debugLock = new object();

        public OcrService(string tessDataPath, string language = "eng")
        {
            _tessDataPath = tessDataPath;
            _language = language;
        }

        /// <summary>
        /// Enable debug mode to save preprocessed images
        /// </summary>
        public static void EnableDebugMode(bool enable = true)
        {
            _debugMode = enable;
            if (enable)
            {
                _debugCounter = 0; // Reset counter when enabling
            }
        }

        /// <summary>
        /// Khởi tạo Tesseract engine
        /// </summary>
        public void Initialize()
        {
            if (_engine != null)
                return;

            if (!Directory.Exists(_tessDataPath))
            {
                throw new DirectoryNotFoundException(
                    $"Tessdata không tồn tại: {_tessDataPath}\n" +
                    "Hãy tải tessdata từ: https://github.com/tesseract-ocr/tessdata_fast");
            }

            try
            {
                _engine = new TesseractEngine(_tessDataPath, _language, EngineMode.Default);
                
                // Cấu hình tối ưu cho OCR subtitle
                bool isCJK = _language.StartsWith("chi") || _language == "jpn" || _language == "kor";
                
                if (!isCJK)
                {
                    // Chỉ set whitelist cho Latin languages (English, Vietnamese)
                    _engine.SetVariable("tessedit_char_whitelist", 
                        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?'\"-:;()[] " +
                        "áàảãạăắằẳẵặâấầ̉ẫậéèẻẽẹêếềểễệíì̉ĩịóòỏõọôốồổỗộơớờởỡợúùủũụưứừửữựýỳ̉ỹỵđ" +
                        "ÁÀẢÃẠĂẮẰẲẴẶÂẤẦ̉ẪẬÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỬỮỰÝỲỶŸỴĐ");
                }
                
                // PSM 7 = Single text line (BEST for subtitles)
                _engine.SetVariable("tessedit_pageseg_mode", "7");
                
                // Improve accuracy for CJK
                if (isCJK)
                {
                    _engine.SetVariable("preserve_interword_spaces", "1");
                }
                
                // Disable dictionary loading for speed
                _engine.SetVariable("load_system_dawg", "0");
                _engine.SetVariable("load_freq_dawg", "0");
            }
            catch (Exception ex)
            {
                throw new Exception($"Không thể khởi tạo Tesseract: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// OCR một ảnh từ file path
        /// </summary>
        public string RecognizeText(string imagePath)
        {
            if (_engine == null)
                Initialize();

            try
            {
                using var img = Pix.LoadFromFile(imagePath);
                using var page = _engine!.Process(img);
                var text = page.GetText();
                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR lỗi tại {imagePath}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// OCR một ảnh từ bitmap (sau khi crop ROI) - VỚI PREPROCESSING
        /// </summary>
        public string RecognizeText(Bitmap bitmap)
        {
            if (_engine == null)
                Initialize();

            try
            {
                // Save original if debug mode (thread-safe)
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

                // PREPROCESSING: Cải thiện ảnh trước khi OCR
                using var preprocessedBitmap = PreprocessImage(bitmap);
                
                // Save preprocessed if debug mode
                if (_debugMode)
                {
                    lock (_debugLock)
                    {
                        var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
                        preprocessedBitmap.Save(Path.Combine(debugFolder, $"1_preprocessed_{currentCounter:D4}.png"));
                    }
                }

                // Convert Bitmap to byte array
                using var ms = new MemoryStream();
                preprocessedBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var bytes = ms.ToArray();

                using var img = Pix.LoadFromMemory(bytes);
                using var page = _engine!.Process(img);
                var text = page.GetText();
                
                // Log confidence if very low (for debugging)
                var confidence = page.GetMeanConfidence();
                if (confidence < 40 && _debugMode)
                {
                    Console.WriteLine($"[OCR] Very low confidence: {confidence}% - Text: '{text?.Trim()}'");
                }
                
                return NormalizeText(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR lỗi: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Preprocessing ảnh để tăng độ chính xác OCR - IMPROVED VERSION
        /// </summary>
        private Bitmap PreprocessImage(Bitmap original)
        {
            // Step 1: Upscale if too small
            var scaled = UpscaleImage(original, minWidth: 800);
            
            // Step 2: Convert to grayscale
            var grayscale = ConvertToGrayscaleOptimized(scaled);
            
            // Step 3: Increase contrast (moderate)
            var contrasted = AdjustContrastOptimized(grayscale, contrast: 1.8f);
            
            // Step 4: Apply Otsu threshold (best for high-contrast subtitles)
            var binarized = ApplyOtsuThresholdOptimized(contrasted);
            
            // Cleanup intermediate images
            if (scaled != original) scaled.Dispose();
            grayscale.Dispose();
            contrasted.Dispose();
            
            return binarized;
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
        /// Kiểm tra Tesseract có sẵn không
        /// </summary>
        public bool IsAvailable()
        {
            try
            {
                Initialize();
                return _engine != null;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
