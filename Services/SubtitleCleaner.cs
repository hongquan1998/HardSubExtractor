using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service làm s?ch và chu?n hóa subtitle
    /// </summary>
    public class SubtitleCleaner
    {
        /// <summary>
        /// Làm s?ch toàn b? subtitle
        /// </summary>
        public List<SubtitleItem> CleanSubtitles(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return subtitles;

            var cleaned = new List<SubtitleItem>(subtitles);

            // Bước 1: Remove duplicate lines trong mỗi subtitle
            cleaned = RemoveDuplicateLines(cleaned);

            // Bước 2: Merge subtitle quá ngắn
            cleaned = MergeShortSubtitles(cleaned, minDuration: 300);

            // Bước 3: Split subtitle quá dài (gi? t?i ?a 2 dòng)
            cleaned = SplitLongSubtitles(cleaned, maxLines: 2);

            // Bước 4: Remove subtitle tr?ng ho?c rác
            cleaned = RemoveInvalidSubtitles(cleaned);

            // Bước 5: Re-index
            cleaned = ReIndexSubtitles(cleaned);

            return cleaned;
        }

        /// <summary>
        /// Remove các dòng duplicate trong m?i subtitle
        /// </summary>
        private List<SubtitleItem> RemoveDuplicateLines(List<SubtitleItem> subtitles)
        {
            foreach (var sub in subtitles)
            {
                var lines = sub.Text.Split('\n')
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrWhiteSpace(l))
                                   .Distinct()
                                   .ToList();

                sub.Text = string.Join("\n", lines);
            }

            return subtitles;
        }

        /// <summary>
        /// Merge các subtitle quá ngắn (< minDuration ms)
        /// </summary>
        private List<SubtitleItem> MergeShortSubtitles(List<SubtitleItem> subtitles, long minDuration)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var merged = new List<SubtitleItem>();
            var currentSub = subtitles[0];

            for (int i = 1; i < subtitles.Count; i++)
            {
                var nextSub = subtitles[i];

                // N?u subtitle hi?n t?i quá ng?n, merge v?i subtitle k? ti?p
                if (currentSub.Duration < minDuration && 
                    nextSub.StartTime - currentSub.EndTime < 500) // gap < 500ms
                {
                    // Merge text
                    var mergedText = currentSub.Text;
                    if (!mergedText.EndsWith("\n"))
                        mergedText += "\n";
                    mergedText += nextSub.Text;

                    currentSub = new SubtitleItem
                    {
                        Index = currentSub.Index,
                        StartTime = currentSub.StartTime,
                        EndTime = nextSub.EndTime,
                        Text = mergedText
                    };
                }
                else
                {
                    merged.Add(currentSub);
                    currentSub = nextSub;
                }
            }

            merged.Add(currentSub);
            return merged;
        }

        /// <summary>
        /// Split subtitle có quá nhi?u dòng (gi? t?i ?a maxLines dòng)
        /// </summary>
        private List<SubtitleItem> SplitLongSubtitles(List<SubtitleItem> subtitles, int maxLines)
        {
            var result = new List<SubtitleItem>();

            foreach (var sub in subtitles)
            {
                var lines = sub.Text.Split('\n')
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrWhiteSpace(l))
                                   .ToList();

                if (lines.Count <= maxLines)
                {
                    result.Add(sub);
                    continue;
                }

                // Split thành nhi?u subtitle
                var duration = sub.Duration;
                var durationPerLine = duration / lines.Count;

                for (int i = 0; i < lines.Count; i += maxLines)
                {
                    var splitLines = lines.Skip(i).Take(maxLines).ToList();
                    var splitStartTime = sub.StartTime + (i * durationPerLine);
                    var splitEndTime = sub.StartTime + ((i + splitLines.Count) * durationPerLine);

                    result.Add(new SubtitleItem
                    {
                        Index = sub.Index,
                        StartTime = splitStartTime,
                        EndTime = Math.Min(splitEndTime, sub.EndTime),
                        Text = string.Join("\n", splitLines)
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Remove subtitle không h?p l? (tr?ng, quá ng?n, toàn ký t? rác)
        /// </summary>
        private List<SubtitleItem> RemoveInvalidSubtitles(List<SubtitleItem> subtitles)
        {
            return subtitles.Where(sub =>
            {
                // Subtitle tr?ng
                if (string.IsNullOrWhiteSpace(sub.Text))
                    return false;

                // Subtitle quá ng?n (< 100ms)
                if (sub.Duration < 100)
                    return false;

                // Subtitle ch? có 1 ký t?
                if (sub.Text.Trim().Length < 2)
                    return false;

                // Subtitle toàn ký t? ??c bi?t (rác OCR)
                var alphanumericCount = sub.Text.Count(c => char.IsLetterOrDigit(c));
                if (alphanumericCount < 2)
                    return false;

                return true;
            }).ToList();
        }

        /// <summary>
        /// Re-index subtitle t? 1
        /// </summary>
        private List<SubtitleItem> ReIndexSubtitles(List<SubtitleItem> subtitles)
        {
            for (int i = 0; i < subtitles.Count; i++)
            {
                subtitles[i].Index = i + 1;
            }
            return subtitles;
        }

        /// <summary>
        /// Trim text OCR (remove leading/trailing whitespace)
        /// </summary>
        public string TrimOcrText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var lines = text.Split('\n')
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrWhiteSpace(l));

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Remove các ký t? rác th??ng g?p t? OCR
        /// </summary>
        public string RemoveOcrNoise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Replace các pattern rác th??ng g?p
            var cleanText = text;

            // Remove | th??ng xu?t hi?n t? border
            cleanText = cleanText.Replace("|", "");
            
            // Remove _ th??ng xu?t hi?n t? underline
            cleanText = cleanText.Replace("_", "");

            // Remove multiple spaces
            while (cleanText.Contains("  "))
                cleanText = cleanText.Replace("  ", " ");

            return cleanText.Trim();
        }

        /// <summary>
        /// ??m s? subtitle
        /// </summary>
        public int CountValidSubtitles(List<SubtitleItem> subtitles)
        {
            return subtitles.Count(s => !string.IsNullOrWhiteSpace(s.Text));
        }

        /// <summary>
        /// L?y th?ng kê subtitle
        /// </summary>
        public SubtitleStatistics GetStatistics(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return new SubtitleStatistics();

            return new SubtitleStatistics
            {
                TotalCount = subtitles.Count,
                TotalDuration = subtitles.Sum(s => s.Duration),
                AverageDuration = (long)subtitles.Average(s => s.Duration),
                MinDuration = subtitles.Min(s => s.Duration),
                MaxDuration = subtitles.Max(s => s.Duration),
                AverageLineCount = subtitles.Average(s => s.Text.Split('\n').Length)
            };
        }
    }

    /// <summary>
    /// Th?ng kê subtitle
    /// </summary>
    public class SubtitleStatistics
    {
        public int TotalCount { get; set; }
        public long TotalDuration { get; set; }
        public long AverageDuration { get; set; }
        public long MinDuration { get; set; }
        public long MaxDuration { get; set; }
        public double AverageLineCount { get; set; }

        public override string ToString()
        {
            return $"Total: {TotalCount} | Avg: {AverageDuration}ms | Min: {MinDuration}ms | Max: {MaxDuration}ms | Avg Lines: {AverageLineCount:F1}";
        }
    }
}
