namespace HardSubExtractor.Models
{
    /// <summary>
    /// ??i di?n cho m?t subtitle entry v?i thông tin th?i gian và n?i dung
    /// </summary>
    public class SubtitleItem
    {
        /// <summary>
        /// S? th? t? subtitle (1, 2, 3...)
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Th?i gian b?t ??u (milliseconds)
        /// </summary>
        public long StartTime { get; set; }

        /// <summary>
        /// Th?i gian k?t thúc (milliseconds)
        /// </summary>
        public long EndTime { get; set; }

        /// <summary>
        /// N?i dung subtitle (có th? nhi?u dòng)
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Convert StartTime t? ms sang ??nh d?ng SRT (00:00:00,000)
        /// </summary>
        public string StartTimeFormatted => FormatTime(StartTime);

        /// <summary>
        /// Convert EndTime t? ms sang ??nh d?ng SRT (00:00:00,000)
        /// </summary>
        public string EndTimeFormatted => FormatTime(EndTime);

        /// <summary>
        /// Th?i l??ng subtitle (milliseconds)
        /// </summary>
        public long Duration => EndTime - StartTime;

        /// <summary>
        /// Format th?i gian ms sang SRT format (HH:MM:SS,mmm)
        /// </summary>
        private string FormatTime(long milliseconds)
        {
            var ts = TimeSpan.FromMilliseconds(milliseconds);
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        /// <summary>
        /// Xu?t subtitle d??i d?ng SRT format
        /// </summary>
        public string ToSrtFormat()
        {
            return $"{Index}\r\n{StartTimeFormatted} --> {EndTimeFormatted}\r\n{Text}\r\n";
        }

        public override string ToString()
        {
            return $"[{Index}] {StartTimeFormatted} --> {EndTimeFormatted} | {Text}";
        }
    }
}
