using System.Text;
using HardSubExtractor.Models;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service xu?t subtitle ra file SRT
    /// </summary>
    public class SubtitleExporter
    {
        /// <summary>
        /// Xu?t subtitle ra file SRT
        /// </summary>
        public void ExportToSrt(List<SubtitleItem> subtitles, string outputPath)
        {
            if (subtitles.Count == 0)
                throw new ArgumentException("Không có subtitle để xuất");

            var sb = new StringBuilder();

            foreach (var sub in subtitles.OrderBy(s => s.StartTime))
            {
                sb.AppendLine(sub.ToSrtFormat());
            }

            // Ghi file UTF-8 with BOM
            File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// Xu?t subtitle ra string SRT format
        /// </summary>
        public string ExportToSrtString(List<SubtitleItem> subtitles)
        {
            if (subtitles.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();

            foreach (var sub in subtitles.OrderBy(s => s.StartTime))
            {
                sb.AppendLine(sub.ToSrtFormat());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Validate subtitle tr??c khi xu?t
        /// </summary>
        public List<string> ValidateSubtitles(List<SubtitleItem> subtitles)
        {
            var errors = new List<string>();

            if (subtitles.Count == 0)
            {
                errors.Add("Không có subtitle");
                return errors;
            }

            for (int i = 0; i < subtitles.Count; i++)
            {
                var sub = subtitles[i];

                // Ki?m tra text tr?ng
                if (string.IsNullOrWhiteSpace(sub.Text))
                {
                    errors.Add($"Subtitle {sub.Index}: Text trống");
                }

                // Ki?m tra th?i gian không h?p l?
                if (sub.StartTime < 0)
                {
                    errors.Add($"Subtitle {sub.Index}: StartTime < 0");
                }

                if (sub.EndTime <= sub.StartTime)
                {
                    errors.Add($"Subtitle {sub.Index}: EndTime <= StartTime");
                }

                // Ki?m tra overlap v?i subtitle k? ti?p
                if (i < subtitles.Count - 1)
                {
                    var nextSub = subtitles[i + 1];
                    if (sub.EndTime > nextSub.StartTime)
                    {
                        errors.Add($"Subtitle {sub.Index}: Overlap với subtitle {nextSub.Index}");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Import subtitle t? file SRT
        /// </summary>
        public List<SubtitleItem> ImportFromSrt(string srtPath)
        {
            if (!File.Exists(srtPath))
                throw new FileNotFoundException("File SRT không tồn tại", srtPath);

            var subtitles = new List<SubtitleItem>();
            var lines = File.ReadAllLines(srtPath, Encoding.UTF8);

            SubtitleItem? currentSub = null;
            var textLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Dòng tr?ng -> k?t thúc subtitle hi?n t?i
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentSub != null && textLines.Count > 0)
                    {
                        currentSub.Text = string.Join("\n", textLines);
                        subtitles.Add(currentSub);
                        currentSub = null;
                        textLines.Clear();
                    }
                    continue;
                }

                // Dòng index (s?)
                if (int.TryParse(line, out var index))
                {
                    currentSub = new SubtitleItem { Index = index };
                    continue;
                }

                // Dòng time (00:00:00,000 --> 00:00:00,000)
                if (line.Contains("-->"))
                {
                    var parts = line.Split(new[] { "-->" }, StringSplitOptions.None);
                    if (parts.Length == 2 && currentSub != null)
                    {
                        currentSub.StartTime = ParseSrtTime(parts[0].Trim());
                        currentSub.EndTime = ParseSrtTime(parts[1].Trim());
                    }
                    continue;
                }

                // Dòng text
                if (currentSub != null)
                {
                    textLines.Add(line);
                }
            }

            // Subtitle cu?i cùng
            if (currentSub != null && textLines.Count > 0)
            {
                currentSub.Text = string.Join("\n", textLines);
                subtitles.Add(currentSub);
            }

            return subtitles;
        }

        /// <summary>
        /// Parse SRT time format (00:00:00,000) sang milliseconds
        /// </summary>
        private long ParseSrtTime(string timeStr)
        {
            try
            {
                // Format: 00:00:00,000 ho?c 00:00:00.000
                timeStr = timeStr.Replace(',', '.');
                var parts = timeStr.Split(':');
                
                if (parts.Length == 3)
                {
                    var hours = int.Parse(parts[0]);
                    var minutes = int.Parse(parts[1]);
                    var secondsParts = parts[2].Split('.');
                    var seconds = int.Parse(secondsParts[0]);
                    var milliseconds = secondsParts.Length > 1 ? int.Parse(secondsParts[1]) : 0;

                    return (hours * 3600000L) + (minutes * 60000L) + (seconds * 1000L) + milliseconds;
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Backup file SRT tr??c khi ghi ?è
        /// </summary>
        public void BackupFile(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var backupPath = filePath + ".bak";
            var counter = 1;

            while (File.Exists(backupPath))
            {
                backupPath = $"{filePath}.bak{counter}";
                counter++;
            }

            File.Copy(filePath, backupPath);
        }

        /// <summary>
        /// L?y thông tin file SRT
        /// </summary>
        public SrtFileInfo GetFileInfo(string srtPath)
        {
            if (!File.Exists(srtPath))
                throw new FileNotFoundException("File SRT không tồn tại", srtPath);

            var subtitles = ImportFromSrt(srtPath);

            return new SrtFileInfo
            {
                FilePath = srtPath,
                SubtitleCount = subtitles.Count,
                TotalDuration = subtitles.Count > 0 ? subtitles.Max(s => s.EndTime) : 0,
                FileSize = new FileInfo(srtPath).Length,
                Encoding = DetectEncoding(srtPath)
            };
        }

        /// <summary>
        /// Detect encoding c?a file
        /// </summary>
        private string DetectEncoding(string filePath)
        {
            try
            {
                using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
                reader.Peek();
                return reader.CurrentEncoding.EncodingName;
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    /// <summary>
    /// Thông tin file SRT
    /// </summary>
    public class SrtFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public int SubtitleCount { get; set; }
        public long TotalDuration { get; set; }
        public long FileSize { get; set; }
        public string Encoding { get; set; } = string.Empty;

        public string TotalDurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromMilliseconds(TotalDuration);
                return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024)
                    return $"{FileSize} B";
                else if (FileSize < 1024 * 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                else
                    return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }

        public override string ToString()
        {
            return $"{SubtitleCount} subs | {TotalDurationFormatted} | {FileSizeFormatted}";
        }
    }
}
