using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service làm sạch và chuẩn hóa subtitle - v2.0
    /// Enhanced with better OCR noise removal and CJK support
    /// </summary>
    public class SubtitleCleaner
    {
        /// <summary>
        /// Làm sạch toàn bộ subtitle
        /// </summary>
        public List<SubtitleItem> CleanSubtitles(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return subtitles;

            var cleaned = new List<SubtitleItem>(subtitles);

            // Bước 1: Clean text của mỗi subtitle (remove OCR noise)
            cleaned = CleanOcrText(cleaned);

            // Bước 2: Remove duplicate lines trong mỗi subtitle
            cleaned = RemoveDuplicateLines(cleaned);

            // Bước 3: Merge subtitle quá ngắn (v5: lowered from 300 to 150)
            cleaned = MergeShortSubtitles(cleaned, minDuration: 150);

            // Bước 4: Split subtitle quá dài (giữ tối đa 2 dòng)
            cleaned = SplitLongSubtitles(cleaned, maxLines: 2);

            // Bước 5: Remove subtitle trống hoặc rác
            cleaned = RemoveInvalidSubtitles(cleaned);

            // Bước 6: Fix overlapping times
            cleaned = FixOverlappingTimes(cleaned);

            // Bước 7: Re-index
            cleaned = ReIndexSubtitles(cleaned);

            return cleaned;
        }

        /// <summary>
        /// v2.0: Clean OCR noise from each subtitle's text
        /// </summary>
        private List<SubtitleItem> CleanOcrText(List<SubtitleItem> subtitles)
        {
            foreach (var sub in subtitles)
            {
                var text = sub.Text;
                
                // Remove common OCR noise characters
                text = text.Replace("|", "");
                text = text.Replace("_", " ");
                
                // Remove isolated single special characters (likely noise)
                text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\s|^)[^\w\s\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af](?=\s|$)", "");
                
                // Clean multiple spaces
                text = System.Text.RegularExpressions.Regex.Replace(text, @"  +", " ");
                
                // Clean multiple newlines
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
                
                // Remove leading/trailing whitespace per line
                var lines = text.Split('\n')
                               .Select(l => l.Trim())
                               .Where(l => !string.IsNullOrWhiteSpace(l));
                
                sub.Text = string.Join("\n", lines);
            }
            
            return subtitles;
        }

        /// <summary>
        /// Remove các dòng duplicate trong mỗi subtitle
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

                // Nếu subtitle hiện tại quá ngắn, merge với subtitle kế tiếp
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
        /// Split subtitle có quá nhiều dòng (giữ tối đa maxLines dòng)
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

                // Split thành nhiều subtitle
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
        /// Remove subtitle không hợp lệ (trống, quá ngắn, toàn ký tự rác)
        /// </summary>
        private List<SubtitleItem> RemoveInvalidSubtitles(List<SubtitleItem> subtitles)
        {
            return subtitles.Where(sub =>
            {
                // Subtitle trống
                if (string.IsNullOrWhiteSpace(sub.Text))
                    return false;

                // Subtitle quá ngắn (< 80ms) - v5: lowered from 100
                if (sub.Duration < 80)
                    return false;

                // Subtitle chỉ có 1 ký tự (but allow single CJK)
                var trimmedText = sub.Text.Trim();
                if (trimmedText.Length < 2)
                {
                    // v5: Allow single CJK character
                    if (trimmedText.Length == 1)
                    {
                        char c = trimmedText[0];
                        bool isCjk = c >= 0x4E00 && c <= 0x9FFF || 
                                     c >= 0x3040 && c <= 0x30FF || 
                                     c >= 0xAC00 && c <= 0xD7AF;
                        if (isCjk) return true;
                    }
                    return false;
                }

                // v5: Check for CJK content - be lenient
                bool hasCjk = sub.Text.Any(c => c >= 0x4E00 && c <= 0x9FFF || 
                                                c >= 0x3040 && c <= 0x30FF || 
                                                c >= 0xAC00 && c <= 0xD7AF);
                
                // Subtitle toàn ký tự đặc biệt (rác OCR)
                var alphanumericCount = sub.Text.Count(c => char.IsLetterOrDigit(c));
                if (hasCjk)
                {
                    // CJK: even a single recognizable character is valid
                    if (alphanumericCount < 1)
                        return false;
                }
                else
                {
                    if (alphanumericCount < 2)
                        return false;
                }

                // Single repeated character (OCR artifact)
                var cleanText = sub.Text.Replace(" ", "").Replace("\n", "");
                if (cleanText.Length > 2 && cleanText.Distinct().Count() <= 1)
                    return false;

                return true;
            }).ToList();
        }

        /// <summary>
        /// v2.0: Fix overlapping subtitle times
        /// </summary>
        private List<SubtitleItem> FixOverlappingTimes(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            for (int i = 0; i < subtitles.Count - 1; i++)
            {
                var current = subtitles[i];
                var next = subtitles[i + 1];

                // If current end time overlaps with next start time
                if (current.EndTime > next.StartTime)
                {
                    // Set current end time to just before next start time
                    current.EndTime = next.StartTime - 30; // v5: 30ms gap (was 50ms)
                    
                    // Ensure minimum duration
                    if (current.EndTime - current.StartTime < 100)
                    {
                        current.EndTime = current.StartTime + 100;
                    }
                }
            }

            return subtitles;
        }

        /// <summary>
        /// Re-index subtitle từ 1
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
        /// Remove các ký tự rác thường gặp từ OCR
        /// </summary>
        public string RemoveOcrNoise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Replace các pattern rác thường gặp
            var cleanText = text;

            // Remove | thường xuất hiện từ border
            cleanText = cleanText.Replace("|", "");
            
            // Remove _ thường xuất hiện từ underline
            cleanText = cleanText.Replace("_", "");

            // Remove multiple spaces
            while (cleanText.Contains("  "))
                cleanText = cleanText.Replace("  ", " ");

            return cleanText.Trim();
        }

        /// <summary>
        /// Đếm số subtitle
        /// </summary>
        public int CountValidSubtitles(List<SubtitleItem> subtitles)
        {
            return subtitles.Count(s => !string.IsNullOrWhiteSpace(s.Text));
        }

        /// <summary>
        /// Lấy thống kê subtitle
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
    /// Thống kê subtitle
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
