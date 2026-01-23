using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service to detect subtitle changes and create timeline - ENHANCED v3
    /// With adaptive thresholds, better similarity, and smart merging
    /// </summary>
    public class SubtitleDetector
    {
        private readonly double _baseSimilarityThreshold;
        private readonly int _minSubtitleDuration;
        private readonly int _maxGapBetweenSame;
        private readonly int _minTextLength;

        public SubtitleDetector(
            double similarityThreshold = 0.70,
            int minSubtitleDuration = 250,
            int maxGapBetweenSame = 600,
            int minTextLength = 2)
        {
            _baseSimilarityThreshold = similarityThreshold;
            _minSubtitleDuration = minSubtitleDuration;
            _maxGapBetweenSame = maxGapBetweenSame;
            _minTextLength = minTextLength;
        }

        /// <summary>
        /// Detect subtitles from OCR results with adaptive threshold based on FPS
        /// </summary>
        public List<SubtitleItem> DetectSubtitles(Dictionary<long, string> ocrResults, int fps = 4)
        {
            if (ocrResults.Count == 0)
                return new List<SubtitleItem>();

            // Adaptive threshold based on FPS
            double similarityThreshold = GetAdaptiveThreshold(fps);
            int frameInterval = 1000 / fps;
            int maxGap = Math.Max(_maxGapBetweenSame, frameInterval * 3);

            var subtitles = new List<SubtitleItem>();
            var sortedResults = ocrResults.OrderBy(kvp => kvp.Key).ToList();

            string? currentText = null;
            long currentStartTime = 0;
            long lastSeenTime = 0;
            int emptyFrameCount = 0;
            int maxEmptyFrames = Math.Max(2, fps / 2);

            foreach (var kvp in sortedResults)
            {
                long timestamp = kvp.Key;
                string text = CleanText(kvp.Value);

                if (string.IsNullOrWhiteSpace(text) || text.Length < _minTextLength)
                {
                    emptyFrameCount++;

                    if (currentText != null && emptyFrameCount >= maxEmptyFrames)
                    {
                        var duration = lastSeenTime - currentStartTime;
                        if (duration >= _minSubtitleDuration && currentText.Length >= _minTextLength)
                        {
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = lastSeenTime + 200,
                                Text = currentText
                            });
                        }
                        currentText = null;
                    }
                    continue;
                }

                emptyFrameCount = 0;

                if (currentText == null)
                {
                    currentText = text;
                    currentStartTime = timestamp;
                    lastSeenTime = timestamp;
                }
                else
                {
                    var similarity = CalculateSimilarityEnhanced(currentText, text);
                    var timeSinceLastSeen = timestamp - lastSeenTime;
                    bool isSceneChange = timeSinceLastSeen > maxGap;

                    if (similarity >= similarityThreshold && !isSceneChange)
                    {
                        lastSeenTime = timestamp;
                        if (IsBetterText(text, currentText))
                            currentText = text;
                    }
                    else
                    {
                        var duration = lastSeenTime - currentStartTime;
                        if (duration >= _minSubtitleDuration && currentText.Length >= _minTextLength)
                        {
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = lastSeenTime + 200,
                                Text = currentText
                            });
                        }

                        currentText = text;
                        currentStartTime = timestamp;
                        lastSeenTime = timestamp;
                    }
                }
            }

            if (currentText != null && currentText.Length >= _minTextLength)
            {
                var duration = lastSeenTime - currentStartTime;
                if (duration >= _minSubtitleDuration)
                {
                    subtitles.Add(new SubtitleItem
                    {
                        StartTime = currentStartTime,
                        EndTime = lastSeenTime + 200,
                        Text = currentText
                    });
                }
            }

            subtitles = MergeNearDuplicates(subtitles);
            subtitles = MergeContinuations(subtitles);
            subtitles = FilterNoiseSubtitles(subtitles);

            return subtitles;
        }

        private double GetAdaptiveThreshold(int fps)
        {
            return fps switch
            {
                <= 2 => _baseSimilarityThreshold + 0.05,
                3 => _baseSimilarityThreshold,
                4 => _baseSimilarityThreshold - 0.05,
                5 => _baseSimilarityThreshold - 0.08,
                _ => _baseSimilarityThreshold - 0.10 // Covers >= 6
            };
        }

        public double CalculateSimilarityEnhanced(string text1, string text2)
        {
            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
                return 0;

            var normalized1 = NormalizeForComparison(text1);
            var normalized2 = NormalizeForComparison(text2);

            if (normalized1 == normalized2)
                return 1.0;

            if (string.IsNullOrEmpty(normalized1) || string.IsNullOrEmpty(normalized2))
                return 0;

            int distance = LevenshteinDistance(normalized1, normalized2);
            int maxLen = Math.Max(normalized1.Length, normalized2.Length);

            double similarity = 1.0 - (double)distance / maxLen;

            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
            {
                double substringSimilarity = (double)Math.Min(normalized1.Length, normalized2.Length) / maxLen;
                similarity = Math.Max(similarity, substringSimilarity);
            }

            return Math.Max(0, Math.Min(1, similarity));
        }

        private string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = text.ToLower().Trim();

            result = result.Replace('0', 'o')
                          .Replace('1', 'i')
                          .Replace('|', 'i')
                          .Replace('!', 'i')
                          .Replace('$', 's')
                          .Replace('@', 'a');

            result = System.Text.RegularExpressions.Regex.Replace(result, @"[^\w\s\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();

            return result;
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            int len1 = s1.Length;
            int len2 = s2.Length;

            var prev = new int[len2 + 1];
            var curr = new int[len2 + 1];

            for (int j = 0; j <= len2; j++)
                prev[j] = j;

            for (int i = 1; i <= len1; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= len2; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            return prev[len2];
        }

        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var result = text.Trim();
            result = System.Text.RegularExpressions.Regex.Replace(result, @"^[\s\-_=+]+", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[\s\-_=+]+$", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

            return result.Trim();
        }

        private bool IsBetterText(string newText, string currentText)
        {
            if (string.IsNullOrEmpty(currentText))
                return true;
            if (string.IsNullOrEmpty(newText))
                return false;

            if (newText.Length > currentText.Length + 3)
                return true;

            var newRatio = GetAlphaNumericRatio(newText);
            var currentRatio = GetAlphaNumericRatio(currentText);

            return newRatio > currentRatio + 0.05;
        }

        private double GetAlphaNumericRatio(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int alphaNum = text.Count(c => char.IsLetterOrDigit(c));
            return (double)alphaNum / text.Length;
        }

        private List<SubtitleItem> MergeNearDuplicates(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var result = new List<SubtitleItem> { subtitles[0] };

            for (int i = 1; i < subtitles.Count; i++)
            {
                var current = subtitles[i];
                var last = result[^1];

                var similarity = CalculateSimilarityEnhanced(current.Text, last.Text);
                var gap = current.StartTime - last.EndTime;

                if (similarity >= 0.85 && gap < 1000)
                {
                    last.EndTime = Math.Max(last.EndTime, current.EndTime);
                    if (IsBetterText(current.Text, last.Text))
                        last.Text = current.Text;
                }
                else
                {
                    result.Add(current);
                }
            }

            return result;
        }

        private List<SubtitleItem> MergeContinuations(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var result = new List<SubtitleItem>();

            for (int i = 0; i < subtitles.Count; i++)
            {
                var current = subtitles[i];

                if (result.Count > 0)
                {
                    var last = result[^1];
                    var gap = current.StartTime - last.EndTime;

                    bool isContinuation = gap < 1500 &&
                        (last.Text.EndsWith("...") ||
                         last.Text.EndsWith("..") ||
                         last.Text.EndsWith("-"));

                    if (isContinuation)
                    {
                        var combinedText = last.Text.TrimEnd('.', '-', ' ') + " " + current.Text;
                        last.Text = combinedText;
                        last.EndTime = current.EndTime;
                        continue;
                    }
                }

                result.Add(current);
            }

            return result;
        }

        private List<SubtitleItem> FilterNoiseSubtitles(List<SubtitleItem> subtitles)
        {
            return subtitles.Where(s =>
            {
                var text = s.Text;

                if (text.Length < _minTextLength)
                    return false;

                if (GetAlphaNumericRatio(text) < 0.3)
                    return false;

                var duration = s.EndTime - s.StartTime;
                if (duration < 150 && text.Length < 5)
                    return false;

                return true;
            }).ToList();
        }
    }
}
