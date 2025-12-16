using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service phát hi?n thay ??i subtitle và t?o timeline - OPTIMIZED v2
    /// </summary>
    public class SubtitleDetector
    {
        private readonly double _similarityThreshold;
        private readonly int _minSubtitleDuration; // ms
        private readonly int _maxGapBetweenSame; // ms
        
        public SubtitleDetector(
            double similarityThreshold = 0.70,
            int minSubtitleDuration = 250,
            int maxGapBetweenSame = 600)
        {
            _similarityThreshold = similarityThreshold;
            _minSubtitleDuration = minSubtitleDuration;
            _maxGapBetweenSame = maxGapBetweenSame;
        }

        /// <summary>
        /// Phát hi?n subtitle t? danh sách frame OCR - ENHANCED v2
        /// </summary>
        public List<SubtitleItem> DetectSubtitles(Dictionary<long, string> ocrResults, int fps = 4)
        {
            var subtitles = new List<SubtitleItem>();
            
            if (ocrResults.Count == 0)
                return subtitles;

            var sortedResults = ocrResults.OrderBy(kvp => kvp.Key).ToList();

            string? currentText = null;
            long currentStartTime = 0;
            long lastSeenTime = 0;
            int index = 1;
            int emptyFrameCount = 0;
            
            // Dynamic threshold based on FPS
            int maxEmptyFrames = Math.Max(3, fps + 1);

            for (int i = 0; i < sortedResults.Count; i++)
            {
                var timestamp = sortedResults[i].Key;
                var text = CleanOcrText(sortedResults[i].Value);

                // B? qua frame tr?ng ho?c text quá ng?n (noise)
                if (string.IsNullOrWhiteSpace(text) || text.Length < 1)
                {
                    emptyFrameCount++;
                    
                    // N?u ?ang có subtitle hi?n t?i
                    if (currentText != null)
                    {
                        var gap = timestamp - lastSeenTime;
                        
                        // K?t thúc subtitle n?u gap quá l?n HO?C quá nhi?u frame tr?ng
                        if (gap > _maxGapBetweenSame || emptyFrameCount > maxEmptyFrames)
                        {
                            var duration = lastSeenTime - currentStartTime;
                            
                            // Ch? thêm n?u subtitle ?? dài
                            if (duration >= _minSubtitleDuration)
                            {
                                subtitles.Add(new SubtitleItem
                                {
                                    Index = index++,
                                    StartTime = currentStartTime,
                                    EndTime = lastSeenTime + 200,  // +200ms buffer
                                    Text = currentText
                                });
                            }
                            
                            currentText = null;
                            emptyFrameCount = 0;
                        }
                    }
                    continue;
                }

                // Reset empty counter khi có text
                emptyFrameCount = 0;

                // Frame ??u tiên có text
                if (currentText == null)
                {
                    currentText = text;
                    currentStartTime = timestamp;
                    lastSeenTime = timestamp;
                    continue;
                }

                // So sánh v?i subtitle hi?n t?i
                var similarity = CalculateSimilarity(currentText, text);

                if (similarity >= _similarityThreshold)
                {
                    // Text gi?ng nhau, ti?p t?c subtitle hi?n t?i
                    lastSeenTime = timestamp;
                    
                    // Update text n?u OCR t?t h?n
                    if (IsBetterText(text, currentText))
                        currentText = text;
                }
                else
                {
                    // Text khác, save subtitle hi?n t?i
                    var duration = lastSeenTime - currentStartTime;
                    
                    if (duration >= _minSubtitleDuration)
                    {
                        subtitles.Add(new SubtitleItem
                        {
                            Index = index++,
                            StartTime = currentStartTime,
                            EndTime = lastSeenTime + 200,
                            Text = currentText
                        });
                    }

                    // B?t ??u subtitle m?i
                    currentText = text;
                    currentStartTime = timestamp;
                    lastSeenTime = timestamp;
                }
            }

            // X? lý subtitle cu?i cùng
            if (currentText != null)
            {
                var duration = lastSeenTime - currentStartTime;
                
                if (duration >= _minSubtitleDuration)
                {
                    subtitles.Add(new SubtitleItem
                    {
                        Index = index++,
                        StartTime = currentStartTime,
                        EndTime = lastSeenTime + 500,
                        Text = currentText
                    });
                }
            }

            // Post-processing: merge và remove duplicates
            var merged = MergeCloseSubtitles(subtitles);
            var deduped = RemoveDuplicates(merged);
            
            return deduped;
        }

        /// <summary>
        /// Overload cho backward compatibility
        /// </summary>
        public List<SubtitleItem> DetectSubtitles(Dictionary<long, string> ocrResults)
        {
            return DetectSubtitles(ocrResults, fps: 4);
        }

        /// <summary>
        /// Clean OCR text ?? so sánh t?t h?n
        /// </summary>
        private string CleanOcrText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            
            // Remove extra spaces
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            
            return text.Trim();
        }

        /// <summary>
        /// Ki?m tra text nào t?t h?n (ít noise h?n)
        /// </summary>
        private bool IsBetterText(string newText, string currentText)
        {
            // Text dài h?n th??ng t?t h?n (full sentence)
            if (newText.Length > currentText.Length + 2)
                return true;
            
            // Ki?m tra ratio ch?/ký t? ??c bi?t
            var newAlphaRatio = GetAlphaNumericRatio(newText);
            var currentAlphaRatio = GetAlphaNumericRatio(currentText);
            
            return newAlphaRatio > currentAlphaRatio + 0.1;
        }

        /// <summary>
        /// Tính ratio ch?/s? so v?i t?ng ký t?
        /// </summary>
        private double GetAlphaNumericRatio(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            
            int alphaNumeric = text.Count(c => char.IsLetterOrDigit(c));
            return (double)alphaNumeric / text.Length;
        }

        /// <summary>
        /// Remove duplicates AFTER merging
        /// </summary>
        private List<SubtitleItem> RemoveDuplicates(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return subtitles;
            
            var result = new List<SubtitleItem>();
            
            foreach (var sub in subtitles)
            {
                bool isDuplicate = false;
                
                for (int i = 0; i < result.Count; i++)
                {
                    var existing = result[i];
                    var similarity = CalculateSimilarity(sub.Text, existing.Text);
                    
                    // More aggressive deduplication
                    if (similarity >= 0.90)
                    {
                        // Check if time overlaps or very close
                        var timeOverlap = !(sub.EndTime < existing.StartTime - 500 || sub.StartTime > existing.EndTime + 500);
                        
                        if (timeOverlap)
                        {
                            // Keep the longer/better one, extend time
                            if (sub.Text.Length > existing.Text.Length)
                            {
                                existing.Text = sub.Text;
                            }
                            existing.StartTime = Math.Min(existing.StartTime, sub.StartTime);
                            existing.EndTime = Math.Max(existing.EndTime, sub.EndTime);
                            isDuplicate = true;
                            break;
                        }
                    }
                }
                
                if (!isDuplicate)
                {
                    result.Add(sub);
                }
            }
            
            // Re-index
            for (int i = 0; i < result.Count; i++)
                result[i].Index = i + 1;
            
            return result;
        }

        /// <summary>
        /// Merge các subtitle g?n nhau
        /// </summary>
        private List<SubtitleItem> MergeCloseSubtitles(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return subtitles;
            
            var merged = new List<SubtitleItem>();
            SubtitleItem? current = null;
            
            foreach (var sub in subtitles.OrderBy(s => s.StartTime))
            {
                if (current == null)
                {
                    current = new SubtitleItem
                    {
                        StartTime = sub.StartTime,
                        EndTime = sub.EndTime,
                        Text = sub.Text
                    };
                    continue;
                }
                
                var gap = sub.StartTime - current.EndTime;
                var similarity = CalculateSimilarity(current.Text, sub.Text);
                
                // Merge if gap small AND similar text
                bool shouldMerge = gap < 300 && similarity >= 0.75;
                
                // Or if very close and likely continuation
                if (!shouldMerge && gap < 100 && IsLikelyContinuation(current.Text, sub.Text))
                {
                    current.Text = current.Text + " " + sub.Text;
                    current.EndTime = sub.EndTime;
                    continue;
                }
                
                if (shouldMerge)
                {
                    current.EndTime = sub.EndTime;
                    if (IsBetterText(sub.Text, current.Text))
                        current.Text = sub.Text;
                }
                else
                {
                    merged.Add(current);
                    current = new SubtitleItem
                    {
                        StartTime = sub.StartTime,
                        EndTime = sub.EndTime,
                        Text = sub.Text
                    };
                }
            }
            
            if (current != null)
                merged.Add(current);
            
            // Re-index
            for (int i = 0; i < merged.Count; i++)
                merged[i].Index = i + 1;
            
            return merged;
        }

        /// <summary>
        /// Check if second text is likely a continuation
        /// </summary>
        private bool IsLikelyContinuation(string text1, string text2)
        {
            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
                return false;
            
            // If first text ends with incomplete sentence marker
            if (text1.EndsWith("...") || text1.EndsWith("..") || text1.EndsWith(",") || text1.EndsWith(";"))
                return true;
            
            // If second text starts with lowercase (continuation)
            if (char.IsLower(text2[0]))
                return true;
            
            return false;
        }

        /// <summary>
        /// Tính ?? t??ng ??ng gi?a 2 text (0.0 - 1.0) s? d?ng Levenshtein Distance
        /// </summary>
        public double CalculateSimilarity(string text1, string text2)
        {
            if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
                return 1.0;

            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
                return 0.0;

            // Normalize ?? so sánh
            text1 = text1.ToLower().Trim();
            text2 = text2.ToLower().Trim();

            if (text1 == text2)
                return 1.0;

            var distance = LevenshteinDistance(text1, text2);
            var maxLength = Math.Max(text1.Length, text2.Length);

            return 1.0 - ((double)distance / maxLength);
        }

        /// <summary>
        /// Tính Levenshtein Distance (edit distance) - OPTIMIZED
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            var len1 = s1.Length;
            var len2 = s2.Length;
            
            // Use single array instead of 2D matrix for memory efficiency
            var previous = new int[len2 + 1];
            var current = new int[len2 + 1];

            // Initialize
            for (int j = 0; j <= len2; j++)
                previous[j] = j;

            // Calculate
            for (int i = 1; i <= len1; i++)
            {
                current[0] = i;
                
                for (int j = 1; j <= len2; j++)
                {
                    var cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + cost
                    );
                }
                
                // Swap arrays
                (previous, current) = (current, previous);
            }

            return previous[len2];
        }

        /// <summary>
        /// ??c tính s? subtitle t? s? frame
        /// </summary>
        public int EstimateSubtitleCount(int frameCount, double avgSubtitleDuration = 3000)
        {
            var avgFramesPerSubtitle = (avgSubtitleDuration / 1000.0) * 2;
            return Math.Max(1, (int)(frameCount / avgFramesPerSubtitle));
        }
    }
}
