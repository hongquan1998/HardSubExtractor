using HardSubExtractor.Models;
using System.Drawing;
using System.Drawing.Imaging;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service fix thời gian subtitle (shift, overlap, duration)
    /// </summary>
    public class SubtitleTimeFixer
    {
        /// <summary>
        /// Shift toàn bộ subtitle ± milliseconds
        /// </summary>
        public List<SubtitleItem> ShiftTime(List<SubtitleItem> subtitles, long shiftMs)
        {
            foreach (var sub in subtitles)
            {
                sub.StartTime = Math.Max(0, sub.StartTime + shiftMs);
                sub.EndTime = Math.Max(sub.StartTime + 100, sub.EndTime + shiftMs);
            }
            return subtitles;
        }

        /// <summary>
        /// Fix overlap giữa các subtitle
        /// Nếu subtitle A kết thúc sau khi subtitle B bắt đầu, cắt A ngắn lại
        /// </summary>
        public List<SubtitleItem> FixOverlap(List<SubtitleItem> subtitles, long minGap = 0)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];

                // Nếu current overlap với next
                if (current.EndTime > next.StartTime)
                {
                    // Cắt current để kết thúc trước next
                    current.EndTime = next.StartTime - minGap;

                    // Đảm bảo duration tối thiểu 100ms
                    if (current.EndTime <= current.StartTime)
                    {
                        current.EndTime = current.StartTime + 100;
                    }
                }
            }

            return sorted;
        }

        /// <summary>
        /// Auto kéo endTime nếu subtitle đổi chậm
        /// Kéo dài subtitle nếu gap với subtitle kế tiếp quá lớn
        /// v5: Cap extension to avoid absurdly long subtitles
        /// </summary>
        public List<SubtitleItem> ExtendDuration(List<SubtitleItem> subtitles, long maxGap = 1000, long maxExtension = 3000)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];

                var gap = next.StartTime - current.EndTime;

                // v5: Only extend if gap is reasonable, cap to maxExtension
                if (gap > maxGap)
                {
                    long newEndTime = next.StartTime - 50; // 50ms gap
                    long extension = newEndTime - current.EndTime;
                    
                    // Don't extend more than maxExtension
                    if (extension > maxExtension)
                    {
                        current.EndTime = current.EndTime + maxExtension;
                    }
                    else
                    {
                        current.EndTime = newEndTime;
                    }
                }
            }

            return sorted;
        }

        /// <summary>
        /// Set minimum duration cho tất cả subtitle
        /// </summary>
        public List<SubtitleItem> SetMinimumDuration(List<SubtitleItem> subtitles, long minDuration = 500)
        {
            foreach (var sub in subtitles)
            {
                if (sub.Duration < minDuration)
                {
                    sub.EndTime = sub.StartTime + minDuration;
                }
            }
            return subtitles;
        }

        /// <summary>
        /// Set maximum duration cho tất cả subtitle
        /// </summary>
        public List<SubtitleItem> SetMaximumDuration(List<SubtitleItem> subtitles, long maxDuration = 10000)
        {
            foreach (var sub in subtitles)
            {
                if (sub.Duration > maxDuration)
                {
                    sub.EndTime = sub.StartTime + maxDuration;
                }
            }
            return subtitles;
        }

        /// <summary>
        /// Fix gap giữa các subtitle (thêm/bớt thời gian)
        /// </summary>
        public List<SubtitleItem> AdjustGaps(List<SubtitleItem> subtitles, long targetGap = 100)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];

                var currentGap = next.StartTime - current.EndTime;

                // Nếu gap khác target, adjust
                if (currentGap != targetGap)
                {
                    current.EndTime = next.StartTime - targetGap;

                    // Đảm bảo duration tối thiểu
                    if (current.EndTime <= current.StartTime)
                    {
                        current.EndTime = current.StartTime + 100;
                        next.StartTime = current.EndTime + targetGap;
                    }
                }
            }

            return sorted;
        }

        /// <summary>
        /// Phát hiện các subtitle có vấn đề về thời gian
        /// </summary>
        public List<TimeIssue> DetectTimeIssues(List<SubtitleItem> subtitles)
        {
            var issues = new List<TimeIssue>();

            foreach (var sub in subtitles)
            {
                // Subtitle có duration <= 0
                if (sub.Duration <= 0)
                {
                    issues.Add(new TimeIssue
                    {
                        SubtitleIndex = sub.Index,
                        IssueType = TimeIssueType.InvalidDuration,
                        Description = $"Duration <= 0: {sub.Duration}ms"
                    });
                }

                // Subtitle quá ngắn (< 200ms)
                if (sub.Duration < 200)
                {
                    issues.Add(new TimeIssue
                    {
                        SubtitleIndex = sub.Index,
                        IssueType = TimeIssueType.TooShort,
                        Description = $"Duration quá ngắn: {sub.Duration}ms"
                    });
                }

                // Subtitle quá dài (> 15s)
                if (sub.Duration > 15000)
                {
                    issues.Add(new TimeIssue
                    {
                        SubtitleIndex = sub.Index,
                        IssueType = TimeIssueType.TooLong,
                        Description = $"Duration quá dài: {sub.Duration}ms"
                    });
                }
            }

            // Check overlap
            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];

                if (current.EndTime > next.StartTime)
                {
                    issues.Add(new TimeIssue
                    {
                        SubtitleIndex = current.Index,
                        IssueType = TimeIssueType.Overlap,
                        Description = $"Overlap với subtitle {next.Index}"
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Auto fix tất cả vấn đề về thời gian
        /// v5: Less aggressive - preserve original timing where possible
        /// </summary>
        public List<SubtitleItem> AutoFix(List<SubtitleItem> subtitles)
        {
            var fixedSubtitles = new List<SubtitleItem>(subtitles);

            // Step 1: Set minimum duration (300ms - enough for single word flash)
            fixedSubtitles = SetMinimumDuration(fixedSubtitles, 300);

            // Step 2: Set maximum duration
            fixedSubtitles = SetMaximumDuration(fixedSubtitles, 10000);

            // Step 3: Fix overlap using midpoint strategy
            fixedSubtitles = FixOverlap(fixedSubtitles, minGap: 30);

            // Step 4: Extend duration if gap is large, but cap extension
            fixedSubtitles = ExtendDuration(fixedSubtitles, maxGap: 2000, maxExtension: 3000);

            return fixedSubtitles;
        }

        /// <summary>
        /// Lấy thống kê về thời gian
        /// </summary>
        public TimeStatistics GetTimeStatistics(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return new TimeStatistics();

            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();
            var gaps = new List<long>();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var gap = sorted[i + 1].StartTime - sorted[i].EndTime;
                gaps.Add(gap);
            }

            return new TimeStatistics
            {
                TotalSubtitles = subtitles.Count,
                AverageDuration = (long)subtitles.Average(s => s.Duration),
                MinDuration = subtitles.Min(s => s.Duration),
                MaxDuration = subtitles.Max(s => s.Duration),
                AverageGap = gaps.Count > 0 ? (long)gaps.Average() : 0,
                MinGap = gaps.Count > 0 ? gaps.Min() : 0,
                MaxGap = gaps.Count > 0 ? gaps.Max() : 0,
                OverlapCount = gaps.Count(g => g < 0)
            };
        }

        /// <summary>
        /// Refine timing using video analysis (Extract frames -> Check text presence)
        /// Precision: 100ms (10 FPS)
        /// </summary>
        public async Task<List<SubtitleItem>> RefineTimingAsync(
            List<SubtitleItem> subtitles, 
            string videoPath, 
            string tempFolder,
            Rectangle roi,
            IProgress<int>? progress = null)
        {
            if (subtitles.Count == 0 || !File.Exists(videoPath))
                return subtitles;

            var frameExtractor = new FrameExtractor();
            var regionDetector = new SubtitleRegionDetector();
            var refinedList = new List<SubtitleItem>();

            // Ensure temp folder exists
            var refineFolder = Path.Combine(tempFolder, "refine");
            if (!Directory.Exists(refineFolder))
                Directory.CreateDirectory(refineFolder);

            int processed = 0;
            int total = subtitles.Count;

            foreach (var sub in subtitles)
            {
                var refinedSub = new SubtitleItem 
                { 
                    Index = sub.Index,
                    Text = sub.Text,
                    StartTime = sub.StartTime,
                    EndTime = sub.EndTime
                };

                try 
                {
                    // 1. Refine Start Time
                    // Extract frames around start time: [Start-500ms, Start+200ms]
                    var startContext = TimeSpan.FromMilliseconds(Math.Max(0, sub.StartTime - 800));
                    var endContext = TimeSpan.FromMilliseconds(sub.StartTime + 400);
                    
                    var startFrames = await frameExtractor.ExtractFramesInRangeAsync(
                        videoPath, refineFolder, startContext, endContext, fps: 10);
                    
                    // Find the first frame that has text
                    long bestStart = -1;
                    foreach (var frame in startFrames)
                    {
                        using var bmp = new Bitmap(frame.FilePath);
                        
                        // Crop ROI
                        using var cropped = OcrService.CropImage(frame.FilePath, roi);
                        
                        // Check if text is present
                        if (regionDetector.HasSubtitleLikely(cropped))
                        {
                            bestStart = frame.Timestamp;
                            break;
                        }
                    }
                    
                    if (bestStart != -1)
                        refinedSub.StartTime = Math.Max(0, bestStart); // Adjust to detected start

                    // 2. Refine End Time
                    // Extract frames around end time: [End-200ms, End+800ms]
                    var startEndContext = TimeSpan.FromMilliseconds(Math.Max(0, sub.EndTime - 400));
                    var endEndContext = TimeSpan.FromMilliseconds(sub.EndTime + 800);
                    
                    var endFrames = await frameExtractor.ExtractFramesInRangeAsync(
                        videoPath, refineFolder, startEndContext, endEndContext, fps: 10);
                        
                    // Find the first frame where text disappears
                    long bestEnd = -1;
                    bool textWasPresent = true;
                    
                    foreach (var frame in endFrames)
                    {
                        using var bmp = new Bitmap(frame.FilePath);
                        using var cropped = OcrService.CropImage(frame.FilePath, roi);
                        
                        bool hasText = regionDetector.HasSubtitleLikely(cropped);
                        
                        if (!hasText && textWasPresent)
                        {
                            bestEnd = frame.Timestamp;
                            break;
                        }
                        textWasPresent = hasText;
                    }
                    
                    if (bestEnd != -1)
                        refinedSub.EndTime = bestEnd;
                        
                    // Cleanup frames
                    foreach(var f in startFrames) try { File.Delete(f.FilePath); } catch {}
                    foreach(var f in endFrames) try { File.Delete(f.FilePath); } catch {}
                }
                catch 
                {
                    // Keep original if error
                }

                refinedList.Add(refinedSub);
                
                processed++;
                progress?.Report((int)((double)processed / total * 100));
            }

            try { Directory.Delete(refineFolder, true); } catch {}
            
            return refinedList;
        }

        /// <summary>
        /// Analyze video to estimate optimal FPS
        /// </summary>
        public async Task<int> EstimateOptimalFpsAsync(
            string videoPath, 
            string tempFolder,
            Rectangle roi)
        {
            try
            {
                var frameExtractor = new FrameExtractor();
                var regionDetector = new SubtitleRegionDetector();
                
                // Extract 10 seconds from middle of video
                // We need to know duration first, but let's just take regular intervals
                // Actually, extracting 1 minute at 1 FPS is fast
                var sampleFolder = Path.Combine(tempFolder, "fps_sample");
                
                // Extract 00:05:00 to 00:06:00 (1 minute) at 4 FPS
                // Assume video is at least that long. If not, take detected duration.
                
                var frames = await frameExtractor.ExtractFramesAsync(videoPath, sampleFolder, fps: 4);
                if (frames.Count == 0) return 4;
                
                // Sample middle 100 frames
                var midIndex = frames.Count / 2;
                var sampleCount = Math.Min(frames.Count, 100);
                var startIdx = Math.Max(0, midIndex - sampleCount / 2);
                var subset = frames.Skip(startIdx).Take(sampleCount).ToList();
                
                int rapidChanges = 0;
                bool wasText = false;
                
                foreach (var frame in subset)
                {
                    using var bmp = new Bitmap(frame.FilePath);
                    using var cropped = OcrService.CropImage(frame.FilePath, roi);
                    bool hasText = regionDetector.HasSubtitleLikely(cropped);
                    
                    if (hasText != wasText)
                    {
                        rapidChanges++;
                    }
                    wasText = hasText;
                }
                
                // Cleanup
                try { Directory.Delete(sampleFolder, true); } catch {}
                
                // Calculate density
                // 4 FPS = 250ms per frame
                // If many changes, suggest higher FPS
                // rapidChanges per 100 frames (25 seconds)
                // If > 10 changes (every 2.5s), standard rate
                // If > 20 changes (every 1.25s), fast rate
                
                if (rapidChanges > 25) return 6; // Very fast
                if (rapidChanges > 15) return 4; // Normal
                return 2; // Slow
            }
            catch
            {
                return 4; // Default
            }
        }
    }

    /// <summary>
    /// Vấn đề về thời gian
    /// </summary>
    public class TimeIssue
    {
        public int SubtitleIndex { get; set; }
        public TimeIssueType IssueType { get; set; }
        public string Description { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"[{SubtitleIndex}] {IssueType}: {Description}";
        }
    }

    public enum TimeIssueType
    {
        InvalidDuration,
        TooShort,
        TooLong,
        Overlap,
        LargeGap
    }

    /// <summary>
    /// Thống kê thời gian
    /// </summary>
    public class TimeStatistics
    {
        public int TotalSubtitles { get; set; }
        public long AverageDuration { get; set; }
        public long MinDuration { get; set; }
        public long MaxDuration { get; set; }
        public long AverageGap { get; set; }
        public long MinGap { get; set; }
        public long MaxGap { get; set; }
        public int OverlapCount { get; set; }

        public override string ToString()
        {
            return $"Subs: {TotalSubtitles} | Avg Duration: {AverageDuration}ms | Overlaps: {OverlapCount}";
        }
    }
}
