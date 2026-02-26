using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service to detect subtitle changes and create timeline - ENHANCED v5
    /// Zero-miss detection with precise timeline, adaptive to FPS
    /// </summary>
    public class SubtitleDetector
    {
        private readonly double _baseSimilarityThreshold;
        private readonly int _minSubtitleDuration;
        private readonly int _maxGapBetweenSame;
        private readonly int _minTextLength;

        public SubtitleDetector(
            double similarityThreshold = 0.65,
            int minSubtitleDuration = 100,
            int maxGapBetweenSame = 800,
            int minTextLength = 2)
        {
            _baseSimilarityThreshold = similarityThreshold;
            _minSubtitleDuration = minSubtitleDuration;
            _maxGapBetweenSame = maxGapBetweenSame;
            _minTextLength = minTextLength;
        }

        /// <summary>
        /// Detect subtitles from OCR results with adaptive threshold based on FPS
        /// v5: Zero-miss detection - prefer false positives over false negatives
        /// </summary>
        public List<SubtitleItem> DetectSubtitles(Dictionary<long, string> ocrResults, int fps = 4)
        {
            if (ocrResults.Count == 0)
                return new List<SubtitleItem>();

            // Adaptive threshold based on FPS
            double similarityThreshold = GetAdaptiveThreshold(fps);
            int frameInterval = 1000 / Math.Max(1, fps);
            int maxGap = Math.Max(_maxGapBetweenSame, frameInterval * 4); // v5: increased from 3 to 4

            var subtitles = new List<SubtitleItem>();
            var sortedResults = ocrResults.OrderBy(kvp => kvp.Key).ToList();

            string? currentText = null;
            long currentStartTime = 0;
            long lastSeenTime = 0;
            int emptyFrameCount = 0;
            
            // v5: More tolerant of empty frames - OCR can fail intermittently
            // At FPS=2: allow up to 4 empty frames (2 seconds) 
            // At FPS=4: allow up to 5 empty frames (1.25 seconds)
            // At FPS>=6: allow up to fps empty frames (1 second)
            int maxEmptyFrames = fps <= 2 ? 4 : Math.Max(4, fps);

            // v5: Keep track of best text seen during current subtitle span
            string? bestText = null;
            int bestTextScore = 0;

            foreach (var kvp in sortedResults)
            {
                long timestamp = kvp.Key;
                string text = CleanText(kvp.Value);

                if (string.IsNullOrWhiteSpace(text) || text.Length < _minTextLength)
                {
                    emptyFrameCount++;

                    if (currentText != null && emptyFrameCount >= maxEmptyFrames)
                    {
                        // v5: Use the best text seen, not just current
                        var textToUse = bestText ?? currentText;
                        var duration = lastSeenTime - currentStartTime;
                        
                        if (duration >= _minSubtitleDuration && textToUse.Length >= _minTextLength)
                        {
                            // v5: End time = last seen + half frame interval (more precise)
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = lastSeenTime + (frameInterval / 2),
                                Text = textToUse
                            });
                        }
                        else if (textToUse.Length >= 3) // v5: Keep even very short flashes if text is meaningful
                        {
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = lastSeenTime + frameInterval,
                                Text = textToUse
                            });
                        }
                        currentText = null;
                        bestText = null;
                        bestTextScore = 0;
                    }
                    continue;
                }

                // We have text - reset empty counter
                emptyFrameCount = 0;

                if (currentText == null)
                {
                    // Start new subtitle
                    currentText = text;
                    bestText = text;
                    bestTextScore = ScoreText(text);
                    currentStartTime = timestamp;
                    lastSeenTime = timestamp;
                }
                else
                {
                    var similarity = CalculateSimilarityEnhanced(currentText, text);
                    // v5: Also compare against bestText for more accurate similarity
                    if (bestText != null && bestText != currentText)
                    {
                        var bestSimilarity = CalculateSimilarityEnhanced(bestText, text);
                        similarity = Math.Max(similarity, bestSimilarity);
                    }
                    
                    var timeSinceLastSeen = timestamp - lastSeenTime;
                    bool isSceneChange = timeSinceLastSeen > maxGap;

                    if (similarity >= similarityThreshold && !isSceneChange)
                    {
                        // Same subtitle continues
                        lastSeenTime = timestamp;
                        
                        // v5: Track best text by scoring
                        int textScore = ScoreText(text);
                        if (textScore > bestTextScore)
                        {
                            bestText = text;
                            bestTextScore = textScore;
                        }
                        
                        if (IsBetterText(text, currentText))
                            currentText = text;
                    }
                    else
                    {
                        // Different subtitle - save current and start new
                        var textToUse = bestText ?? currentText;
                        var duration = lastSeenTime - currentStartTime;
                        
                        if (duration >= _minSubtitleDuration && textToUse.Length >= _minTextLength)
                        {
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = lastSeenTime + (frameInterval / 2),
                                Text = textToUse
                            });
                        }
                        else if (textToUse.Length >= 3) // v5: Keep flash subtitles
                        {
                            subtitles.Add(new SubtitleItem
                            {
                                StartTime = currentStartTime,
                                EndTime = Math.Max(lastSeenTime + frameInterval, currentStartTime + 500),
                                Text = textToUse
                            });
                        }

                        currentText = text;
                        bestText = text;
                        bestTextScore = ScoreText(text);
                        currentStartTime = timestamp;
                        lastSeenTime = timestamp;
                    }
                }
            }

            // Don't forget the last subtitle - v5: always try to include it
            if (currentText != null)
            {
                var textToUse = bestText ?? currentText;
                if (textToUse.Length >= _minTextLength)
                {
                    var duration = lastSeenTime - currentStartTime;
                    long endTime;
                    
                    if (duration >= _minSubtitleDuration)
                    {
                        endTime = lastSeenTime + (frameInterval / 2);
                    }
                    else
                    {
                        // Short subtitle at the end - give minimum display time
                        endTime = Math.Max(lastSeenTime + frameInterval, currentStartTime + 500);
                    }
                    
                    subtitles.Add(new SubtitleItem
                    {
                        StartTime = currentStartTime,
                        EndTime = endTime,
                        Text = textToUse
                    });
                }
            }

            // Post-processing pipeline - v5: order matters!
            subtitles = MergeNearDuplicates(subtitles, frameInterval);
            subtitles = MergeContinuations(subtitles);
            subtitles = MergeGappedSameText(subtitles, frameInterval);
            subtitles = MergeAdjacentSameText(subtitles); // v5: new pass
            subtitles = FilterNoiseSubtitles(subtitles, fps);
            subtitles = FixTimeline(subtitles, frameInterval); // v5: new pass

            return subtitles;
        }

        private double GetAdaptiveThreshold(int fps)
        {
            // v5: Lower thresholds to avoid missing subtitles due to OCR variation
            return fps switch
            {
                <= 2 => _baseSimilarityThreshold + 0.03,
                3 => _baseSimilarityThreshold - 0.02,
                4 => _baseSimilarityThreshold - 0.05,
                5 => _baseSimilarityThreshold - 0.08,
                _ => _baseSimilarityThreshold - 0.10 // Covers >= 6
            };
        }

        /// <summary>
        /// Score text quality (higher = better) for choosing best text representation
        /// </summary>
        private int ScoreText(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            int score = text.Length;
            
            // Bonus for alphanumeric ratio
            int alphaNum = text.Count(c => char.IsLetterOrDigit(c));
            score += alphaNum * 2;
            
            // Bonus for CJK characters (more reliable)
            int cjkCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF || 
                                           c >= 0x3040 && c <= 0x30FF || 
                                           c >= 0xAC00 && c <= 0xD7AF);
            score += cjkCount * 3;
            
            // Penalty for special characters
            int special = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
            score -= special * 2;
            
            return score;
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

            // Substring containment check
            if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
            {
                double substringSimilarity = (double)Math.Min(normalized1.Length, normalized2.Length) / maxLen;
                similarity = Math.Max(similarity, substringSimilarity);
            }

            // v5: Word-level comparison for longer texts
            if (normalized1.Length > 5 && normalized2.Length > 5)
            {
                var words1 = normalized1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var words2 = normalized2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (words1.Length > 1 && words2.Length > 1)
                {
                    int commonWords = words1.Intersect(words2).Count();
                    int totalWords = Math.Max(words1.Length, words2.Length);
                    double wordSimilarity = (double)commonWords / totalWords;
                    similarity = Math.Max(similarity, wordSimilarity);
                }
            }

            return Math.Max(0, Math.Min(1, similarity));
        }

        private string NormalizeForComparison(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var result = text.ToLower().Trim();

            // Common OCR substitution errors
            result = result.Replace('0', 'o')
                          .Replace('1', 'i')
                          .Replace('|', 'i')
                          .Replace('!', 'i')
                          .Replace('$', 's')
                          .Replace('@', 'a');

            // Keep CJK characters, basic alphanumeric, and spaces
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[^\w\s\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af\u0600-\u06ff]", "");
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
            
            // Remove leading/trailing noise characters
            result = System.Text.RegularExpressions.Regex.Replace(result, @"^[\s\-_=+|]+", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"[\s\-_=+|]+$", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

            return result.Trim();
        }

        private bool IsBetterText(string newText, string currentText)
        {
            if (string.IsNullOrEmpty(currentText))
                return true;
            if (string.IsNullOrEmpty(newText))
                return false;

            // Significantly longer text is better
            if (newText.Length > currentText.Length + 3)
                return true;

            // Higher alphanumeric ratio is better
            var newRatio = GetAlphaNumericRatio(newText);
            var currentRatio = GetAlphaNumericRatio(currentText);

            if (newRatio > currentRatio + 0.05)
                return true;

            // Same length, same ratio - prefer fewer special characters
            if (Math.Abs(newText.Length - currentText.Length) <= 2)
            {
                int newSpecial = newText.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
                int curSpecial = currentText.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));
                return newSpecial < curSpecial;
            }

            return false;
        }

        private double GetAlphaNumericRatio(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int alphaNum = text.Count(c => char.IsLetterOrDigit(c));
            return (double)alphaNum / text.Length;
        }

        /// <summary>
        /// v5: FPS-aware merge with adaptive gap tolerance
        /// </summary>
        private List<SubtitleItem> MergeNearDuplicates(List<SubtitleItem> subtitles, int frameInterval)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var result = new List<SubtitleItem> { subtitles[0] };

            // v5: Gap tolerance scales with frame interval
            int maxMergeGap = Math.Max(1500, frameInterval * 5);

            for (int i = 1; i < subtitles.Count; i++)
            {
                var current = subtitles[i];
                var last = result[^1];

                var similarity = CalculateSimilarityEnhanced(current.Text, last.Text);
                var gap = current.StartTime - last.EndTime;

                if (similarity >= 0.75 && gap < maxMergeGap)
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

        /// <summary>
        /// v5: Merge subtitles with same text separated by gaps (OCR failed in middle)
        /// </summary>
        private List<SubtitleItem> MergeGappedSameText(List<SubtitleItem> subtitles, int frameInterval)
        {
            if (subtitles.Count <= 1)
                return subtitles;

            var result = new List<SubtitleItem> { subtitles[0] };

            // v5: Max gap for merge is adaptive to frame interval
            int maxGapForMerge = Math.Max(2000, frameInterval * 6);

            for (int i = 1; i < subtitles.Count; i++)
            {
                var current = subtitles[i];
                var last = result[^1];

                var similarity = CalculateSimilarityEnhanced(current.Text, last.Text);
                var gap = current.StartTime - last.EndTime;

                // If same text with reasonable gap, merge
                if (similarity >= 0.85 && gap < maxGapForMerge && gap >= 0)
                {
                    last.EndTime = current.EndTime;
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

        /// <summary>
        /// v5 NEW: Final pass to merge adjacent subtitles with very similar text
        /// Catches cases where the same subtitle got split by the detection logic
        /// </summary>
        private List<SubtitleItem> MergeAdjacentSameText(List<SubtitleItem> subtitles)
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

                // v5: If gap is small and texts are nearly identical (>0.92), always merge
                if (similarity >= 0.92 && gap < 3000 && gap >= -500)
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

        /// <summary>
        /// v5: Less aggressive noise filter - prefer keeping subtitles over removing them
        /// </summary>
        private List<SubtitleItem> FilterNoiseSubtitles(List<SubtitleItem> subtitles, int fps)
        {
            int frameInterval = 1000 / Math.Max(1, fps);
            
            return subtitles.Where(s =>
            {
                var text = s.Text;

                // Text too short (only filter single chars)
                if (text.Length < _minTextLength)
                    return false;

                // v5: Be more lenient with alphanumeric ratio for CJK text
                var alphaRatio = GetAlphaNumericRatio(text);
                bool hasCjk = text.Any(c => c >= 0x4E00 && c <= 0x9FFF || 
                                            c >= 0x3040 && c <= 0x30FF || 
                                            c >= 0xAC00 && c <= 0xD7AF);
                
                if (hasCjk)
                {
                    // CJK text: very lenient - CJK chars count as alphanumeric
                    if (alphaRatio < 0.15)
                        return false;
                }
                else
                {
                    // Latin text: slightly more strict
                    if (alphaRatio < 0.20)
                        return false;
                }

                // v5: Only filter truly degenerate cases
                // Very short duration with very short text AND low FPS = likely noise
                var duration = s.EndTime - s.StartTime;
                if (duration < 80 && text.Length < 3)
                    return false;

                // All same character repeated = OCR artifact
                var cleanText = text.Replace(" ", "");
                if (cleanText.Length > 2 && cleanText.Distinct().Count() <= 1)
                    return false;

                return true;
            }).ToList();
        }

        /// <summary>
        /// v5 NEW: Fix timeline issues after all merging is done
        /// Ensures no overlaps and proper minimum duration
        /// </summary>
        private List<SubtitleItem> FixTimeline(List<SubtitleItem> subtitles, int frameInterval)
        {
            if (subtitles.Count == 0)
                return subtitles;

            // Sort by start time
            subtitles = subtitles.OrderBy(s => s.StartTime).ToList();

            // Ensure minimum duration
            int minDuration = Math.Max(200, frameInterval);
            foreach (var sub in subtitles)
            {
                if (sub.EndTime - sub.StartTime < minDuration)
                {
                    sub.EndTime = sub.StartTime + minDuration;
                }
            }

            // Fix overlaps
            for (int i = 0; i < subtitles.Count - 1; i++)
            {
                var current = subtitles[i];
                var next = subtitles[i + 1];

                if (current.EndTime > next.StartTime)
                {
                    // v5: Don't cut too aggressively - use midpoint for overlap
                    long midPoint = (current.EndTime + next.StartTime) / 2;
                    
                    // But ensure current doesn't become too short
                    if (midPoint - current.StartTime >= minDuration)
                    {
                        current.EndTime = midPoint - 10; // 10ms safety gap
                    }
                    else
                    {
                        current.EndTime = next.StartTime - 10;
                    }

                    // Ensure minimum duration after fix
                    if (current.EndTime <= current.StartTime)
                    {
                        current.EndTime = current.StartTime + minDuration;
                    }
                }
            }

            // Re-index
            for (int i = 0; i < subtitles.Count; i++)
            {
                subtitles[i].Index = i + 1;
            }

            return subtitles;
        }
    }
}
