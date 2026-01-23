using System.Drawing;
using System.Drawing.Imaging;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service to automatically detect subtitle regions in video frames
    /// </summary>
    public class SubtitleRegionDetector
    {
        private float _lastConfidence = 0;
        
        /// <summary>
        /// Get confidence score of last detection (0-100)
        /// </summary>
        public float LastConfidence => _lastConfidence;

        /// <summary>
        /// Detect subtitle region from a single frame
        /// </summary>
        public Rectangle DetectSubtitleRegion(Bitmap frame)
        {
            // Subtitles are typically in bottom 15-30% of the video
            // We'll analyze this region for text-like patterns
            
            int width = frame.Width;
            int height = frame.Height;
            
            // Define candidate regions (from most common to less common)
            var candidateRegions = new List<(Rectangle region, string name, float priority)>
            {
                // Bottom center (most common)
                (new Rectangle(width / 6, (int)(height * 0.78), width * 2 / 3, (int)(height * 0.18)), "Bottom Center", 1.0f),
                
                // Bottom full width
                (new Rectangle(0, (int)(height * 0.75), width, (int)(height * 0.22)), "Bottom Full", 0.9f),
                
                // Bottom with more padding
                (new Rectangle(width / 10, (int)(height * 0.80), width * 4 / 5, (int)(height * 0.16)), "Bottom Padded", 0.85f),
                
                // Top (for some Asian content)
                (new Rectangle(width / 6, (int)(height * 0.02), width * 2 / 3, (int)(height * 0.12)), "Top Center", 0.5f),
            };

            Rectangle bestRegion = candidateRegions[0].region;
            float bestScore = 0;
            string bestName = "";

            foreach (var (region, name, priority) in candidateRegions)
            {
                float score = AnalyzeRegionForText(frame, region) * priority;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRegion = region;
                    bestName = name;
                }
            }

            // Refine the detected region
            var refinedRegion = RefineRegion(frame, bestRegion);
            
            _lastConfidence = Math.Min(100, bestScore * 100);
            
            Console.WriteLine($"[AutoDetect] Best region: {bestName}, Score: {bestScore:F2}, Confidence: {_lastConfidence:F1}%");
            
            return refinedRegion;
        }

        /// <summary>
        /// Detect from multiple frames for better accuracy
        /// </summary>
        public Rectangle DetectFromMultipleFrames(List<Bitmap> frames)
        {
            if (frames.Count == 0)
                return Rectangle.Empty;

            var detectedRegions = new List<(Rectangle region, float confidence)>();

            foreach (var frame in frames)
            {
                var region = DetectSubtitleRegion(frame);
                detectedRegions.Add((region, _lastConfidence));
            }

            // Weight by confidence and average the regions
            float totalWeight = detectedRegions.Sum(r => r.confidence);
            if (totalWeight == 0)
                return detectedRegions[0].region;

            float avgX = detectedRegions.Sum(r => r.region.X * r.confidence) / totalWeight;
            float avgY = detectedRegions.Sum(r => r.region.Y * r.confidence) / totalWeight;
            float avgW = detectedRegions.Sum(r => r.region.Width * r.confidence) / totalWeight;
            float avgH = detectedRegions.Sum(r => r.region.Height * r.confidence) / totalWeight;

            _lastConfidence = detectedRegions.Max(r => r.confidence);

            return new Rectangle((int)avgX, (int)avgY, (int)avgW, (int)avgH);
        }

        /// <summary>
        /// Analyze a region for text-like patterns
        /// Returns score 0-1 indicating likelihood of containing text
        /// </summary>
        private unsafe float AnalyzeRegionForText(Bitmap frame, Rectangle region)
        {
            // Ensure region is within bounds
            region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
            if (region.Width <= 0 || region.Height <= 0)
                return 0;

            var rect = new Rectangle(0, 0, frame.Width, frame.Height);
            var data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                byte* ptr = (byte*)data.Scan0;
                int stride = data.Stride;

                int highContrastPixels = 0;
                int edgePixels = 0;
                int totalPixels = 0;
                int brightPixels = 0;

                // Analyze the region
                for (int y = region.Top; y < region.Bottom; y++)
                {
                    for (int x = region.Left; x < region.Right; x++)
                    {
                        int idx = y * stride + x * 3;
                        int b = ptr[idx];
                        int g = ptr[idx + 1];
                        int r = ptr[idx + 2];

                        // Grayscale value
                        int gray = (r * 77 + g * 150 + b * 29) >> 8;
                        totalPixels++;

                        // Count bright pixels (potential text)
                        if (gray > 200)
                            brightPixels++;

                        // Check for high contrast with neighbors (edge detection)
                        if (x > region.Left && y > region.Top)
                        {
                            int leftIdx = y * stride + (x - 1) * 3;
                            int topIdx = (y - 1) * stride + x * 3;

                            int leftGray = (ptr[leftIdx + 2] * 77 + ptr[leftIdx + 1] * 150 + ptr[leftIdx] * 29) >> 8;
                            int topGray = (ptr[topIdx + 2] * 77 + ptr[topIdx + 1] * 150 + ptr[topIdx] * 29) >> 8;

                            int diffH = Math.Abs(gray - leftGray);
                            int diffV = Math.Abs(gray - topGray);

                            // Strong edge
                            if (diffH > 50 || diffV > 50)
                                edgePixels++;

                            // High contrast
                            if (diffH > 100 || diffV > 100)
                                highContrastPixels++;
                        }
                    }
                }

                // Calculate scores
                float brightRatio = (float)brightPixels / totalPixels;
                float edgeRatio = (float)edgePixels / totalPixels;
                float contrastRatio = (float)highContrastPixels / totalPixels;

                // Text typically has:
                // - Some bright pixels (5-40%)
                // - Moderate edges (1-15%)
                // - Some high contrast areas (0.5-10%)

                float score = 0;

                // Bright pixels score (text is usually white/yellow)
                if (brightRatio >= 0.02 && brightRatio <= 0.50)
                    score += 0.3f * (1 - Math.Abs(brightRatio - 0.15f) / 0.35f);

                // Edge score (text has horizontal lines)
                if (edgeRatio >= 0.005 && edgeRatio <= 0.20)
                    score += 0.4f * (edgeRatio / 0.10f);

                // Contrast score
                if (contrastRatio >= 0.002 && contrastRatio <= 0.15)
                    score += 0.3f * (contrastRatio / 0.05f);

                return Math.Min(1.0f, score);
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        /// <summary>
        /// Refine the detected region to better fit the actual text
        /// </summary>
        private unsafe Rectangle RefineRegion(Bitmap frame, Rectangle region)
        {
            // Add some padding
            int padX = region.Width / 20;
            int padY = region.Height / 10;

            int newX = Math.Max(0, region.X - padX);
            int newY = Math.Max(0, region.Y - padY);
            int newWidth = Math.Min(frame.Width - newX, region.Width + padX * 2);
            int newHeight = Math.Min(frame.Height - newY, region.Height + padY * 2);

            return new Rectangle(newX, newY, newWidth, newHeight);
        }

        /// <summary>
        /// Quick check if a frame likely contains subtitles
        /// </summary>
        public bool HasSubtitleLikely(Bitmap frame)
        {
            // Check bottom region quickly
            var bottomRegion = new Rectangle(
                frame.Width / 6,
                (int)(frame.Height * 0.78),
                frame.Width * 2 / 3,
                (int)(frame.Height * 0.18)
            );

            float score = AnalyzeRegionForText(frame, bottomRegion);
            return score > 0.15f;
        }
    }
}
