using System.Diagnostics;

namespace HardSubExtractor.Services
{
    /// <summary>
    /// Service trích xu?t frame t? video s? d?ng FFmpeg
    /// </summary>
    public class FrameExtractor
    {
        private readonly string _ffmpegPath;
        private Process? _ffmpegProcess;

        public FrameExtractor(string? ffmpegPath = null)
        {
            // Priority:
            // 1. User-specified path
            // 2. FFmpeg in application directory (for portable build)
            // 3. FFmpeg in PATH
            
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                _ffmpegPath = ffmpegPath;
            }
            else
            {
                // Try to find FFmpeg in application directory first
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                
                // Check in root (for single-file publish)
                var ffmpegInAppDir = Path.Combine(appDir, "ffmpeg.exe");
                
                // Check in ffmpeg/bin subfolder (for development)
                var ffmpegInBin = Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe");
                
                // Check in bin subfolder (for regular publish)
                var ffmpegInBinDirect = Path.Combine(appDir, "bin", "ffmpeg.exe");
                
                if (File.Exists(ffmpegInAppDir))
                {
                    _ffmpegPath = ffmpegInAppDir;
                }
                else if (File.Exists(ffmpegInBin))
                {
                    _ffmpegPath = ffmpegInBin;
                }
                else if (File.Exists(ffmpegInBinDirect))
                {
                    _ffmpegPath = ffmpegInBinDirect;
                }
                else
                {
                    // Fallback to system PATH
                    _ffmpegPath = "ffmpeg";
                }
            }
        }

        /// <summary>
        /// Ki?m tra FFmpeg có s?n không
        /// </summary>
        public async Task<bool> CheckFFmpegAvailableAsync()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Trích xu?t frame t? video theo FPS
        /// </summary>
        /// <param name="videoPath">???ng d?n video</param>
        /// <param name="outputFolder">Th? m?c l?u frame</param>
        /// <param name="fps">S? frame m?i giây (1-4)</param>
        /// <param name="progress">Callback báo ti?n trình (0-100)</param>
        /// <param name="cancellationToken">Token ?? cancel</param>
        public async Task<List<FrameInfo>> ExtractFramesAsync(
            string videoPath,
            string outputFolder,
            int fps = 2,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(videoPath))
                throw new FileNotFoundException("Video không tồn tại", videoPath);

            if (fps < 1 || fps > 10)
                throw new ArgumentException("FPS phải từ 1-10", nameof(fps));

            // T?o th? m?c output n?u ch?a có
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            // Clear th? m?c c?
            foreach (var file in Directory.GetFiles(outputFolder, "frame_*.png"))
            {
                File.Delete(file);
            }

            // L?y th?i l??ng video
            var duration = await GetVideoDurationAsync(videoPath);
            if (duration <= 0)
                throw new Exception("Không thể lấy thời lượng video");

            // FFmpeg command: trích frame theo fps
            // -vf fps=2 : 2 frame/giây
            // frame_%06d.png : frame_000001.png, frame_000002.png...
            var outputPattern = Path.Combine(outputFolder, "frame_%06d.png");
            var arguments = $"-i \"{videoPath}\" -vf fps={fps} \"{outputPattern}\"";

            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            var tcs = new TaskCompletionSource<bool>();
            var errorOutput = new List<string>();

            _ffmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.Add(e.Data);
                    // Parse ti?n trình t? FFmpeg output
                    // FFmpeg output: "time=00:01:23.45"
                    if (e.Data.Contains("time=") && duration > 0)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"time=(\d+):(\d+):(\d+\.\d+)");
                        if (match.Success)
                        {
                            var hours = int.Parse(match.Groups[1].Value);
                            var minutes = int.Parse(match.Groups[2].Value);
                            var seconds = double.Parse(match.Groups[3].Value);
                            var currentTime = hours * 3600 + minutes * 60 + seconds;
                            var percent = (int)((currentTime / duration) * 100);
                            progress?.Report(Math.Min(percent, 100));
                        }
                    }
                }
            };

            _ffmpegProcess.Exited += (sender, e) => tcs.TrySetResult(true);
            _ffmpegProcess.EnableRaisingEvents = true;

            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();
            _ffmpegProcess.BeginOutputReadLine();

            // Ch? process hoàn thành ho?c cancel
            using (cancellationToken.Register(() =>
            {
                try
                {
                    _ffmpegProcess?.Kill();
                    tcs.TrySetCanceled();
                }
                catch { }
            }))
            {
                await tcs.Task;
            }

            if (_ffmpegProcess.ExitCode != 0)
            {
                throw new Exception($"FFmpeg lỗi: {string.Join("\n", errorOutput.TakeLast(5))}");
            }

            // Collect frame info
            var frames = new List<FrameInfo>();
            var frameFiles = Directory.GetFiles(outputFolder, "frame_*.png")
                                     .OrderBy(f => f)
                                     .ToList();

            var frameInterval = 1000.0 / fps; // ms per frame
            for (int i = 0; i < frameFiles.Count; i++)
            {
                frames.Add(new FrameInfo
                {
                    FrameNumber = i + 1,
                    FilePath = frameFiles[i],
                    Timestamp = (long)(i * frameInterval)
                });
            }

            progress?.Report(100);
            return frames;
        }

        /// <summary>
        /// Extract frames for a specific time range at high FPS (for precise timing)
        /// </summary>
        public async Task<List<FrameInfo>> ExtractFramesInRangeAsync(
            string videoPath,
            string outputFolder,
            TimeSpan start,
            TimeSpan end,
            int fps = 10)
        {
            if (!File.Exists(videoPath))
                throw new FileNotFoundException("Video not found", videoPath);

            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            // ffmpeg -ss 00:00:10 -to 00:00:15 -i video.mp4 -vf fps=10 frame_%03d.png
            string startStr = start.ToString(@"hh\:mm\:ss\.fff");
            string endStr = end.ToString(@"hh\:mm\:ss\.fff");
            
            // Output pattern
            var outputPattern = Path.Combine(outputFolder, "refine_%03d.png");
            
            // Use -ss before -i for faster seeking
            var arguments = $"-ss {startStr} -to {endStr} -i \"{videoPath}\" -vf fps={fps} \"{outputPattern}\"";

            _ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            await Task.Run(() => 
            {
                _ffmpegProcess.Start();
                _ffmpegProcess.WaitForExit();
            });

            // Collect results
            var frames = new List<FrameInfo>();
            var files = Directory.GetFiles(outputFolder, "refine_*.png").OrderBy(f => f).ToList();
            
            double interval = 1000.0 / fps;
            long startMs = (long)start.TotalMilliseconds;

            for (int i = 0; i < files.Count; i++)
            {
                frames.Add(new FrameInfo
                {
                    FrameNumber = i,
                    FilePath = files[i],
                    Timestamp = startMs + (long)(i * interval)
                });
            }

            return frames;
        }

        /// <summary>
        /// L?y th?i l??ng video (giây)
        /// </summary>
        private async Task<double> GetVideoDurationAsync(string videoPath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = $"-i \"{videoPath}\"",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                var output = new List<string>();
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.Add(e.Data);
                };

                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                // Parse "Duration: 00:01:23.45"
                foreach (var line in output)
                {
                    if (line.Contains("Duration:"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"Duration: (\d+):(\d+):(\d+\.\d+)");
                        if (match.Success)
                        {
                            var hours = int.Parse(match.Groups[1].Value);
                            var minutes = int.Parse(match.Groups[2].Value);
                            var seconds = double.Parse(match.Groups[3].Value);
                            return hours * 3600 + minutes * 60 + seconds;
                        }
                    }
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Cancel quá trình extract
        /// </summary>
        public void Cancel()
        {
            try
            {
                _ffmpegProcess?.Kill();
            }
            catch { }
        }
    }

    /// <summary>
    /// Thông tin frame ?ã extract
    /// </summary>
    public class FrameInfo
    {
        public int FrameNumber { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public long Timestamp { get; set; } // milliseconds
    }
}
