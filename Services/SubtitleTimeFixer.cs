using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service fix th?i gian subtitle (shift, overlap, duration)
    /// </summary>
    public class SubtitleTimeFixer
    {
        /// <summary>
        /// Shift toàn b? subtitle ± milliseconds
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
        /// Fix overlap gi?a các subtitle
        /// N?u subtitle A k?t thúc sau khi subtitle B b?t ??u, c?t A ng?n l?i
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

                // N?u current overlap v?i next
                if (current.EndTime > next.StartTime)
                {
                    // C?t current ?? k?t thúc tr??c next
                    current.EndTime = next.StartTime - minGap;

                    // ??m b?o duration t?i thi?u 100ms
                    if (current.EndTime <= current.StartTime)
                    {
                        current.EndTime = current.StartTime + 100;
                    }
                }
            }

            return sorted;
        }

        /// <summary>
        /// Auto kéo endTime n?u subtitle ??i ch?m
        /// Kéo dài subtitle n?u gap v?i subtitle k? ti?p quá l?n
        /// </summary>
        public List<SubtitleItem> ExtendDuration(List<SubtitleItem> subtitles, long maxGap = 1000)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var sorted = subtitles.OrderBy(s => s.StartTime).ToList();

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var current = sorted[i];
                var next = sorted[i + 1];

                var gap = next.StartTime - current.EndTime;

                // N?u gap quá l?n, kéo dài current
                if (gap > maxGap)
                {
                    current.EndTime = next.StartTime - 50; // gi? 50ms gap
                }
            }

            return sorted;
        }

        /// <summary>
        /// Set minimum duration cho t?t c? subtitle
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
        /// Set maximum duration cho t?t c? subtitle
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
        /// Fix gap gi?a các subtitle (thêm/b?t th?i gian)
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

                // N?u gap khác target, adjust
                if (currentGap != targetGap)
                {
                    current.EndTime = next.StartTime - targetGap;

                    // ??m b?o duration t?i thi?u
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
        /// Phát hi?n các subtitle có v?n ?? v? th?i gian
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

                // Subtitle quá ng?n (< 200ms)
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
        /// Auto fix t?t c? v?n ?? v? th?i gian
        /// </summary>
        public List<SubtitleItem> AutoFix(List<SubtitleItem> subtitles)
        {
            var fixedSubtitles = new List<SubtitleItem>(subtitles);

            // B??c 1: Set minimum duration
            fixedSubtitles = SetMinimumDuration(fixedSubtitles, 500);

            // B??c 2: Set maximum duration
            fixedSubtitles = SetMaximumDuration(fixedSubtitles, 10000);

            // B??c 3: Fix overlap
            fixedSubtitles = FixOverlap(fixedSubtitles, minGap: 50);

            // B??c 4: Extend duration n?u gap quá l?n
            fixedSubtitles = ExtendDuration(fixedSubtitles, maxGap: 2000);

            return fixedSubtitles;
        }

        /// <summary>
        /// L?y th?ng kê v? th?i gian
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
    }

    /// <summary>
    /// V?n ?? v? th?i gian
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
    /// Th?ng kê th?i gian
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
