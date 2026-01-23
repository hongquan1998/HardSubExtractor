using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using HardSubExtractor.Models;
using HardSubExtractor.Services;
using HardSubExtractor.UI;

namespace HardSubExtractor
{
    public partial class Form1 : Form
    {
        private string? _videoPath;
        private Rectangle _selectedRoi;
        private List<FrameInfo>? _extractedFrames;
        private List<SubtitleItem> _subtitles = new();
        private CancellationTokenSource? _cancellationTokenSource;

        // Services
        private FrameExtractor? _frameExtractor;
        private OcrService? _ocrService;
        private SubtitleDetector? _subtitleDetector;
        private SubtitleCleaner? _subtitleCleaner;
        private SubtitleTimeFixer? _subtitleTimeFixer;
        private SubtitleExporter? _subtitleExporter;

        private readonly string _tempFolder;

        public Form1()
        {
            InitializeComponent();
            
            // Setup paths
            _tempFolder = Path.Combine(Path.GetTempPath(), "HardSubExtractor");

            // Event handlers
            btnLoadVideo.Click += BtnLoadVideo_Click;
            btnSelectRoi.Click += BtnSelectRoi_Click;
            btnStartOcr.Click += BtnStartOcr_Click;
            btnCleanSubtitle.Click += BtnCleanSubtitle_Click;
            btnFixTime.Click += BtnFixTime_Click;
            btnExportSrt.Click += BtnExportSrt_Click;
            btnCreatePrompt.Click += BtnCreatePrompt_Click;
            btnCancel.Click += BtnCancel_Click;
            cmbLanguage.SelectedIndexChanged += CmbLanguage_SelectedIndexChanged;
            btnOptimizeFps.Click += BtnOptimizeFps_Click;

            this.Load += Form1_Load;
        }

        private async void Form1_Load(object? sender, EventArgs e)
        {
            Log("Hard Subtitle Extractor v1.3.0 - Optimized Edition");
            Log("Checking dependencies...");

            // Check FFmpeg
            _frameExtractor = new FrameExtractor();
            var ffmpegAvailable = await _frameExtractor.CheckFFmpegAvailableAsync();
            
            if (!ffmpegAvailable)
            {
                Log("ERROR: FFmpeg không tìm thấy!");
                
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var ffmpegLocations = new[]
                {
                    Path.Combine(appDir, "ffmpeg.exe"),
                    Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe"),
                    Path.Combine(appDir, "bin", "ffmpeg.exe")
                };
                
                var foundLocation = ffmpegLocations.FirstOrDefault(File.Exists);
                
                if (foundLocation != null)
                {
                    Log($"FFmpeg found at: {foundLocation}");
                    Log("But cannot execute. Check file permissions.");
                    MessageBox.Show(
                        $"FFmpeg tìm thấy nhưng không chạy được!\n\n" +
                        $"Vị trí: {foundLocation}\n\n" +
                        $"Giải pháp:\n" +
                        $"1. Check file có bị block không (Properties → Unblock)\n" +
                        $"2. Chạy lại auto-setup.ps1",
                        "FFmpeg Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    Log("FFmpeg not found in application directory.");
                    MessageBox.Show(
                        "FFmpeg không tìm thấy!\n\n" +
                        "Giải pháp:\n" +
                        "1. Chạy: auto-setup.ps1\n" +
                        "   (Download FFmpeg tự động)\n\n" +
                        "2. Hoặc download thủ công:\n" +
                        "   https://ffmpeg.org/download.html\n" +
                        "   Giải nén vào thư mục: ffmpeg/bin/",
                        "Missing Dependency",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            else
            {
                Log("✅ FFmpeg OK");
            }

            // Initialize OCR service
            InitializeOcrService();

            // Initialize services with optimized thresholds
            _subtitleDetector = new SubtitleDetector(
                similarityThreshold: 0.70,
                minSubtitleDuration: 250,
                maxGapBetweenSame: 600);
            _subtitleCleaner = new SubtitleCleaner();
            _subtitleTimeFixer = new SubtitleTimeFixer();
            _subtitleExporter = new SubtitleExporter();

            Log("Ready!");
            Log("📊 Thresholds: similarity=0.70, minDuration=250ms, maxGap=600ms");
            Log("⚠️ Khuyến nghị: FPS >= 4 để detect subtitle tốt nhất!");
        }

        private void CmbLanguage_SelectedIndexChanged(object? sender, EventArgs e)
        {
            // Reinitialize OCR service với ngôn ngữ mới
            InitializeOcrService();
        }

        private void InitializeOcrService()
        {
            try
            {
                var winLang = GetWindowsLanguageCode(cmbLanguage.SelectedIndex);
                Log($"Initializing Windows OCR: {winLang}");

                // Dispose old service
                _ocrService?.Dispose();

                // Create new service with Windows OCR Engine
                var engine = new WindowsOcrEngine(winLang);
                _ocrService = new OcrService(engine);

                if (!engine.IsAvailable())
                {
                    Log($"⚠️ WARNING: Windows OCR language '{winLang}' not installed!");
                    MessageBox.Show(
                        $"Windows OCR chưa hỗ trợ ngôn ngữ: {winLang}\n\n" +
                        $"Vui lòng cài đặt Language Pack trong Settings của Windows:\n" +
                        $"Settings > Time & Language > Language > Add a language",
                        "Missing Language Pack",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    Log($"✅ Windows OCR initialized: {winLang}");
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR initializing OCR: {ex.Message}");
            }
        }

        private string GetLanguageCode(int selectedIndex)
        {
            return selectedIndex switch
            {
                0 => "chi_sim",      // 简体中文
                1 => "chi_tra",      // 繁體中文
                2 => "eng",          // English
                3 => "jpn",          // 日本語
                4 => "kor",          // 한국어
                5 => "vie",          // Tiếng Việt
                _ => "chi_sim"       // Default: Chinese Simplified
            };
        }

        private PreprocessMode GetPreprocessMode(int selectedIndex)
        {
            return selectedIndex switch
            {
                0 => PreprocessMode.Auto,       // Auto (Recommended)
                1 => PreprocessMode.Otsu,       // High Contrast
                2 => PreprocessMode.Adaptive,   // Adaptive
                3 => PreprocessMode.ColorBased, // Color Detection
                4 => PreprocessMode.Invert,     // Invert
                _ => PreprocessMode.Auto
            };
        }

        private void BtnLoadVideo_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv|All Files|*.*",
                Title = "Select Video File"
            };

            if (ofd.ShowDialog() == DialogResult.OK)
            {
                _videoPath = ofd.FileName;
                txtVideoPath.Text = _videoPath;
                btnSelectRoi.Enabled = true; // Enable preview button directly
                Log($"Video loaded: {Path.GetFileName(_videoPath)}");
            }
        }

        private void BtnSelectRoi_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
                return;

            Log("Opening video preview for ROI selection...");
            SetStatus("Opening video preview...");

            try
            {
                // Show video preview form - user chọn ROI và vị trí phụ đề cùng lúc
                using var previewForm = new VideoPreviewForm(_videoPath);
                if (previewForm.ShowDialog() != DialogResult.OK)
                {
                    Log("User cancelled video preview");
                    return;
                }
                
                // Get selected ROI and time
                _selectedRoi = previewForm.SelectedRoi;
                Log($"ROI selected: {_selectedRoi.Width}x{_selectedRoi.Height} at ({_selectedRoi.X},{_selectedRoi.Y})");
                Log($"Time selected: {previewForm.SelectedTime}");
                
                // Enable OCR button
                btnStartOcr.Enabled = true;
                SetStatus($"ROI selected, ready for OCR");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void BtnStartOcr_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath) || _selectedRoi.IsEmpty)
                return;

            if (_ocrService == null)
            {
                MessageBox.Show("OCR Engine is not initialized!", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Hỏi user có muốn bắt đầu OCR toàn bộ video không
            var result = MessageBox.Show(
                $"Bắt đầu OCR toàn bộ video?\n\n" +
                $"Video: {Path.GetFileName(_videoPath)}\n" +
                $"ROI: {_selectedRoi.Width}x{_selectedRoi.Height}\n" +
                $"FPS: {numFps.Value}\n" +
                $"Threads: {numThreads.Value}\n\n" +
                $"Quá trình này có thể mất 5-10 phút.",
                "Start OCR?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
                
            if (result != DialogResult.Yes)
            {
                Log("User cancelled OCR");
                return;
            }

            // CRITICAL: Warn if FPS too low
            if (numFps.Value < 3)
            {
                var fpsWarning = MessageBox.Show(
                    $"⚠️ WARNING: Low FPS Detected!\n\n" +
                    $"Current FPS: {numFps.Value}\n" +
                    $"Expected detection rate: ~60-70% of subtitles\n\n" +
                    $"RECOMMENDED: FPS = 4 or higher\n" +
                    $"  → FPS 4: ~90-95% detection\n" +
                    $"  → FPS 6: ~95-98% detection\n\n" +
                    $"⏱️ Higher FPS = Longer processing but MUCH better detection!\n\n" +
                    $"Continue with FPS={numFps.Value} anyway?",
                    "Low FPS Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                    
                if (fpsWarning == DialogResult.No)
                {
                    Log($"User cancelled due to low FPS ({numFps.Value})");
                    SetStatus("Cancelled - Increase FPS for better detection");
                    return;
                }
                else
                {
                    Log($"⚠️ WARNING: Proceeding with low FPS ({numFps.Value}) - may miss many subtitles");
                }
            }

            // Disable buttons
            SetButtonsEnabled(false);
            btnCancel.Enabled = true;

            _cancellationTokenSource = new CancellationTokenSource();
            IProgress<int> progress = new Progress<int>(percent =>
            {
                progressBar.Value = percent;
            });

            try
            {
                // Step 1: Extract frames
                Log($"Step 1: Extracting frames (FPS={numFps.Value})...");
                SetStatus($"Extracting frames at {numFps.Value} FPS...");

                var framesFolder = Path.Combine(_tempFolder, "frames");
                Directory.CreateDirectory(framesFolder);

                _extractedFrames = await _frameExtractor!.ExtractFramesAsync(
                    _videoPath,
                    framesFolder,
                    (int)numFps.Value,
                    progress,
                    _cancellationTokenSource.Token);

                Log($"Extracted {_extractedFrames.Count} frames");

                // Step 2: OCR each frame - OPTIMIZED PARALLEL PROCESSING
                Log("Step 2: OCR frames (multi-threaded, optimized)...");
                
                // Enable debug mode if checked
                if (chkDebugMode.Checked)
                {
                    OcrService.EnableDebugMode(true);
                    
                    var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
                    Directory.CreateDirectory(debugFolder); // Create immediately
                    
                    Log($"⚠️ DEBUG MODE ENABLED: Saving images to: {debugFolder}");
                    
                    var debugMessage = MessageBox.Show(
                        "🔍 Debug Mode Enabled!\n\n" +
                        "OCR preprocessing images will be saved to:\n\n" +
                        $"{debugFolder}\n\n" +
                        "Files:\n" +
                        "  • 0_original_XXXX.png - Cropped ROI (original)\n" +
                        "  • 1_preprocessed_XXXX.png - After preprocessing\n\n" +
                        "Use this to troubleshoot OCR issues:\n" +
                        "  ✅ Check if text is visible\n" +
                        "  ✅ Check if preprocessing destroys text\n" +
                        "  ✅ Verify ROI is correct\n\n" +
                        "Open folder now?",
                        "Debug Mode",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                        
                    if (debugMessage == DialogResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", debugFolder);
                            Log($"Opened debug folder: {debugFolder}");
                        }
                        catch (Exception ex)
                        {
                            Log($"ERROR opening folder: {ex.Message}");
                            MessageBox.Show(
                                $"Cannot open folder automatically.\n\n" +
                                $"Please open manually:\n{debugFolder}",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                    }
                }
                
                // Get thread count from UI
                int threadCount = (int)numThreads.Value;
                Log($"Using {threadCount} threads (CPU cores: {Environment.ProcessorCount})");
                SetStatus($"OCR processing ({threadCount} threads)...");

                var ocrResults = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();
                var processedCount = 0;
                var emptyResults = 0;
                var startTime = DateTime.Now;

                // Get language code and preprocess mode
                var languageCode = GetLanguageCode(cmbLanguage.SelectedIndex);
                var preprocessMode = GetPreprocessMode(cmbPreprocessMode.SelectedIndex);
                Log($"Using preprocess mode: {preprocessMode}");

                // Create Engine (Windows Media OCR) - Shared instance is thread-safe
                var winLangCode = GetWindowsLanguageCode(cmbLanguage.SelectedIndex);
                Log($"Initializing Windows OCR for {winLangCode}...");
                
                using var engine = new WindowsOcrEngine(winLangCode);
                
                if (!engine.IsAvailable())
                {
                    MessageBox.Show(
                        $"Windows OCR language data for '{winLangCode}' is not installed!\n\n" +
                        "Please install the language pack in Windows Settings:\n" +
                        "Settings -> Time & Language -> Language -> Add a language",
                        "Language Pack Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Create Service - Shared instance
                using var ocrService = new OcrService(engine);
                ocrService.CurrentMode = preprocessMode;
                
                // Parallel Options
                var parallelOptions = new ParallelOptions
                {
                     MaxDegreeOfParallelism = threadCount,
                     CancellationToken = _cancellationTokenSource.Token
                };

                try
                {
                    await Task.Run(() =>
                    {
                        Parallel.ForEach(_extractedFrames, parallelOptions, (frame, loopState) =>
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                loopState.Stop();
                                return;
                            }

                            try
                            {
                                // Crop ROI
                                using var croppedBitmap = OcrService.CropImage(frame.FilePath, _selectedRoi);
                                
                                // OCR with shared instance (Sync wrapper)
                                var text = ocrService.RecognizeText(croppedBitmap);
                                
                                ocrResults.TryAdd(frame.Timestamp, text); // Use TryAdd for ConcurrentDictionary
                                
                                // Track empty results
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    Interlocked.Increment(ref emptyResults);
                                }

                                // Update progress (thread-safe)
                                var count = Interlocked.Increment(ref processedCount);
                                
                                // Update UI less frequently (every 2%)
                                if (count % Math.Max(1, _extractedFrames.Count / 50) == 0)
                                {
                                    var percent = (int)((count / (float)_extractedFrames.Count) * 100);
                                    var elapsed = DateTime.Now - startTime;
                                    var framesPerSecond = count / elapsed.TotalSeconds;
                                    var remaining = TimeSpan.FromSeconds((_extractedFrames.Count - count) / (framesPerSecond + 0.001));
                                    
                                    this.Invoke(() =>
                                    {
                                        progressBar.Value = percent;
                                        SetStatus($"OCR: {count}/{_extractedFrames.Count} ({percent}%) - {framesPerSecond:F1} fps - ETA: {remaining:mm\\:ss}");
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"ERROR processing frame {frame.Timestamp}: {ex.Message}");
                            }
                        });
                    }, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    Log("OCR cancelled");
                    return;
                }

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Log("OCR cancelled");
                    return;
                }

                var totalTime = DateTime.Now - startTime;
                var avgSpeed = _extractedFrames.Count / totalTime.TotalSeconds;
                var nonEmptyResults = ocrResults.Count(kvp => !string.IsNullOrWhiteSpace(kvp.Value));
                
                Log($"OCR completed: {ocrResults.Count} frames in {totalTime:mm\\:ss} (avg: {avgSpeed:F1} fps)");
                Log($"Results: {nonEmptyResults} frames with text, {emptyResults} empty");
                
                // Warning if too many empty results
                if (emptyResults > ocrResults.Count * 0.8)
                {
                    Log("⚠️ WARNING: Most frames returned empty! Check:");
                    Log("  1. ROI selection is correct (covers subtitle area)");
                    Log("  2. Language selection matches video");
                    Log("  3. Enable Debug Mode to see preprocessed images");
                }

                // Step 3: Detect subtitles
                Log("Step 3: Detecting subtitles...");
                SetStatus("Detecting subtitles...");

                // Convert ConcurrentDictionary to Dictionary for SubtitleDetector
                var ocrResultsDict = new Dictionary<long, string>(ocrResults);
                
                // Pass FPS to detector for dynamic threshold
                _subtitles = _subtitleDetector!.DetectSubtitles(ocrResultsDict, (int)numFps.Value);
                Log($"Detected {_subtitles.Count} subtitles");

                // Display results
                DisplaySubtitles();
                btnCleanSubtitle.Enabled = true;
                btnFixTime.Enabled = true;
                btnExportSrt.Enabled = true;
                btnCreatePrompt.Enabled = true;

                SetStatus($"OCR completed! {_subtitles.Count} subtitles detected");
                progressBar.Value = 100;

                // Debug mode reminder
                string debugInfo = "";
                if (chkDebugMode.Checked)
                {
                    var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
                    debugInfo = $"\n\n🔍 Debug images saved to:\n{debugFolder}\n\n" +
                               $"Total images: {_extractedFrames.Count * 2} files\n" +
                               $"(original + preprocessed)";
                }

                MessageBox.Show(
                    $"OCR completed!\n\n" +
                    $"Frames processed: {ocrResults.Count}\n" +
                    $"Subtitles detected: {_subtitles.Count}" +
                    debugInfo,
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                Log("Operation cancelled by user");
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
                btnCancel.Enabled = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void BtnCleanSubtitle_Click(object? sender, EventArgs e)
        {
            if (_subtitles.Count == 0)
                return;

            Log("Cleaning subtitles...");
            SetStatus("Cleaning subtitles...");

            var beforeCount = _subtitles.Count;
            _subtitles = _subtitleCleaner!.CleanSubtitles(_subtitles);
            var afterCount = _subtitles.Count;

            DisplaySubtitles();

            var stats = _subtitleCleaner.GetStatistics(_subtitles);
            Log($"Cleaned: {beforeCount} → {afterCount} subtitles");
            Log($"Stats: {stats}");

            SetStatus($"Cleaned: {afterCount} subtitles");

            MessageBox.Show(
                $"Subtitle cleaned!\n\n" +
                $"Before: {beforeCount}\n" +
                $"After: {afterCount}\n\n" +
                $"{stats}",
                "Cleaned",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async void BtnFixTime_Click(object? sender, EventArgs e)
        {
            if (_subtitles.Count == 0)
                return;

            // Ask for Deep Refinement
            var result = MessageBox.Show(
                "Bạn có muốn chạy 'Deep Timing Refinement' (Chính xác từng frame)?\n\n" +
                "Tính năng này sẽ quét lại video tại điểm bắt đầu/kết thúc cushion để tìm timing chính xác nhất.\n" +
                "⚠️ LƯU Ý: Rất chậm (khoảng 0.5s mỗi subtitle)!\n\n" +
                "Chọn 'No' để chỉ fix logic thông thường (Nhanh).",
                "Deep Timing Refinement",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
                return;

            SetButtonsEnabled(false);
            progressBar.Value = 0;
            progressBar.Maximum = 100;

            try
            {
                if (result == DialogResult.Yes)
                {
                    Log("Running Deep Timing Refinement (this may take a while)...");
                    SetStatus("Refining timing...");
                    
                    var progress = new Progress<int>(p => progressBar.Value = p);
                    
                    _subtitles = await Task.Run(async () => 
                        await _subtitleTimeFixer!.RefineTimingAsync(_subtitles, _videoPath!, _tempFolder, _selectedRoi, progress));
                        
                    Log("Deep refinement completed.");
                }

                Log("Applying logical time fixes...");
                SetStatus("Fixing logic...");
                _subtitles = _subtitleTimeFixer!.AutoFix(_subtitles);

                DisplaySubtitles();

                var stats = _subtitleTimeFixer.GetTimeStatistics(_subtitles);
                Log($"Time fixed: {stats}");
                SetStatus("Time fixed");

                MessageBox.Show(
                    $"Time fixed successfully!\n\n" +
                    $"Stats: {stats}",
                    "Fixed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"ERROR fixing time: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
                progressBar.Value = 0;
            }
        }

        private async void BtnOptimizeFps_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_videoPath))
            {
                MessageBox.Show("Vui lòng mở video trước!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_selectedRoi.IsEmpty)
            {
                MessageBox.Show("Vui lòng chọn vùng phụ đề (ROI) trước!\n(Click nút 'Preview & Select ROI')", 
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                SetButtonsEnabled(false);
                Log("Analyzing video complexity for optimal FPS...");
                SetStatus("Analyzing video...");
                this.Cursor = Cursors.WaitCursor;

                var optimalFps = await _subtitleTimeFixer!.EstimateOptimalFpsAsync(_videoPath, _tempFolder, _selectedRoi);
                
                numFps.Value = optimalFps;
                Log($"Analysis complete. Optimal FPS: {optimalFps}");
                
                MessageBox.Show(
                    $"Video Analysis Complete!\n\n" +
                    $"Based on subtitle change rate, suggested FPS is: {optimalFps}\n" +
                    $"(Updated automatically)",
                    "Optimization",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"Error analyzing FPS: {ex.Message}");
            }
            finally
            {
                SetButtonsEnabled(true);
                SetStatus("Ready");
                this.Cursor = Cursors.Default;
            }
        }

        private void BtnExportSrt_Click(object? sender, EventArgs e)
        {
            if (_subtitles.Count == 0)
            {
                MessageBox.Show("No subtitles to export!", "Warning", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter = "SRT Files|*.srt|All Files|*.*",
                Title = "Save SRT File",
                FileName = Path.GetFileNameWithoutExtension(_videoPath) + ".srt"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _subtitleExporter!.ExportToSrt(_subtitles, sfd.FileName);
                    Log($"Exported to: {sfd.FileName}");
                    SetStatus("SRT exported successfully");

                    MessageBox.Show(
                        $"SRT exported successfully!\n\n" +
                        $"File: {Path.GetFileName(sfd.FileName)}\n" +
                        $"Subtitles: {_subtitles.Count}",
                        "Success",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                    MessageBox.Show($"Error exporting: {ex.Message}", "Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnCreatePrompt_Click(object? sender, EventArgs e)
        {
            if (_subtitles.Count == 0)
            {
                MessageBox.Show("Chưa có subtitle để tạo prompt!\n\nHãy chạy OCR trước.", "Warning", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                this.Cursor = Cursors.WaitCursor;
                Log("Creating translation prompt...");
                SetStatus("Generating prompt...");

                // Tạo prompt với tối ưu hóa
                var prompt = GenerateTranslationPrompt(_subtitles);

                // Kiểm tra độ dài prompt
                const int MAX_RECOMMENDED_LENGTH = 100_000;
                if (prompt.Length > MAX_RECOMMENDED_LENGTH)
                {
                    var result = MessageBox.Show(
                        $"⚠️ Prompt rất dài ({prompt.Length:N0} ký tự)\n\n" +
                        $"Một số AI có thể từ chối hoặc cắt bớt.\n" +
                        $"Khuyến nghị: < {MAX_RECOMMENDED_LENGTH:N0} ký tự\n\n" +
                        $"💡 Gợi ý: Chia subtitle thành nhiều phần nhỏ hơn\n\n" +
                        $"Vẫn muốn copy vào Clipboard?",
                        "Cảnh báo: Prompt quá dài",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                        
                    if (result == DialogResult.No)
                    {
                        SetStatus("Cancelled");
                        return;
                    }
                }

                // Copy vào clipboard với retry logic
                if (!TrySetClipboard(prompt))
                {
                    Log("ERROR: Failed to copy to clipboard");
                    MessageBox.Show(
                        "Không thể copy vào Clipboard!\n\n" +
                        "Clipboard có thể đang bị ứng dụng khác sử dụng.\n" +
                        "Hãy thử lại sau.",
                        "Lỗi Clipboard",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Log($"Prompt created: {prompt.Length} characters, {_subtitles.Count} subtitles");
                SetStatus("Prompt copied to clipboard");

                // Hiển thị thông báo với thống kê
                var stats = GetPromptStatistics(prompt);
                MessageBox.Show(
                    $"✅ Đã tạo prompt dịch và copy vào Clipboard!\n\n" +
                    $"📊 Thống kê:\n" +
                    $"  • Số subtitle: {_subtitles.Count}\n" +
                    $"  • Độ dài prompt: {stats.CharCount:N0} ký tự\n" +
                    $"  • Số dòng: {stats.LineCount}\n" +
                    $"  • Ngôn ngữ nguồn: {GetSourceLanguageName()}\n" +
                    $"  • Ngôn ngữ đích: Tiếng Việt\n\n" +
                    $"📝 Bước tiếp theo:\n" +
                    $"  1. Mở ChatGPT/Claude/Gemini\n" +
                    $"  2. Paste prompt (Ctrl+V)\n" +
                    $"  3. Nhận bản dịch hoàn chỉnh\n" +
                    $"  4. Copy kết quả vào file .srt mới",
                    "Prompt Created Successfully",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                MessageBox.Show($"Lỗi tạo prompt: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Try to set clipboard text with retry logic (handles clipboard busy state)
        /// </summary>
        private bool TrySetClipboard(string text, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    if (i == maxRetries - 1)
                        return false;
                    
                    // Wait a bit before retry
                    System.Threading.Thread.Sleep(100);
                }
            }
            return false;
        }

        private string GenerateTranslationPrompt(List<SubtitleItem> subtitles)
        {
            var sb = new StringBuilder();

            // Header với context đúng
            var sourceLang = GetSourceLanguageName();
            var genre = DetermineGenre(subtitles);
            
            sb.AppendLine("# NHIỆM VỤ DỊCH PHỤ ĐỀ");
            sb.AppendLine();
            sb.AppendLine($"Bạn là dịch giả phụ đề chuyên nghiệp {genre}.");
            sb.AppendLine($"Hãy dịch phụ đề sau từ **{sourceLang}** sang **Tiếng Việt**.");
            sb.AppendLine();
            
            // Yêu cầu bắt buộc với format rõ ràng
            sb.AppendLine("## YÊU CẦU BẮT BUỘC:");
            sb.AppendLine();
            sb.AppendLine("📋 **CẤU TRÚC:**");
            sb.AppendLine("  • Giữ NGUYÊN số thứ tự subtitle");
            sb.AppendLine("  • Giữ NGUYÊN timestamp (00:00:00,000 --> 00:00:00,000)");
            sb.AppendLine("  • CHỈ dịch phần TEXT, không chỉnh sửa gì khác");
            sb.AppendLine();
            
            sb.AppendLine("📝 **NỘI DUNG:**");
            sb.AppendLine($"  • Văn phong: {GetStyleGuide(genre)}");
            sb.AppendLine("  • Xưng hô: Tự nhiên, nhất quán, phù hợp ngữ cảnh");
            sb.AppendLine("  • Thuật ngữ: Giữ tên riêng, không phiên âm nếu không cần");
            sb.AppendLine("  • Độ dài: Tương đương bản gốc, không quá dài");
            sb.AppendLine();
            
            sb.AppendLine("✅ **FORMAT OUTPUT:**");
            sb.AppendLine("  • Xuất ĐÚNG format SRT");
            sb.AppendLine("  • Mỗi subtitle cách nhau 1 dòng trống");
            sb.AppendLine("  • KHÔNG thêm giải thích, chú thích, markdown");
            sb.AppendLine("  • KHÔNG tự ý gộp hoặc tách subtitle");
            sb.AppendLine();
            
            // Ví dụ format
            sb.AppendLine("## VÍ DỤ FORMAT:");
            sb.AppendLine("```");
            sb.AppendLine("1");
            sb.AppendLine("00:00:01,000 --> 00:00:03,000");
            sb.AppendLine("Văn bản đã dịch dòng 1");
            sb.AppendLine();
            sb.AppendLine("2");
            sb.AppendLine("00:00:03,500 --> 00:00:06,000");
            sb.AppendLine("Văn bản đã dịch dòng 2");
            sb.AppendLine("```");
            sb.AppendLine();
            
            // Context hints (nếu có nhiều subtitle)
            if (subtitles.Count > 50)
            {
                sb.AppendLine("## LƯU Ý:");
                sb.AppendLine($"  • Đây là phụ đề dài ({subtitles.Count} entries)");
                sb.AppendLine("  • Giữ nhịp độ câu chuyện mạch lạc");
                sb.AppendLine("  • Chú ý ngữ cảnh trước/sau để dịch chính xác");
                sb.AppendLine();
            }
            
            // Phần subtitle cần dịch
            sb.AppendLine("==================================================");
            sb.AppendLine("PHỤ ĐỀ CẦN DỊCH:");
            sb.AppendLine("==================================================");
            sb.AppendLine();

            // Export subtitle theo format SRT
            foreach (var sub in subtitles.OrderBy(s => s.StartTime))
            {
                sb.AppendLine(sub.ToSrtFormat());
            }

            sb.AppendLine("==================================================");
            sb.AppendLine("HẾT PHỤ ĐỀ");
            sb.AppendLine("==================================================");
            
            return sb.ToString();
        }

        private string GetSourceLanguageName()
        {
            var languageCode = GetLanguageCode(cmbLanguage.SelectedIndex);
            return languageCode switch
            {
                "chi_sim" => "Tiếng Trung Giản Thể",
                "chi_tra" => "Tiếng Trung Phồn Thể",
                "eng" => "Tiếng Anh",
                "jpn" => "Tiếng Nhật",
                "kor" => "Tiếng Hàn",
                "vie" => "Tiếng Việt",
                _ => "Unknown"
            };
        }

        private string DetermineGenre(List<SubtitleItem> subtitles)
        {
            // Phân tích nội dung để xác định thể loại
            var languageCode = GetLanguageCode(cmbLanguage.SelectedIndex);
            
            if (languageCode.StartsWith("chi"))
            {
                // Kiểm tra keywords cổ trang/tu tiên
                var allText = string.Join(" ", subtitles.Select(s => s.Text));
                if (ContainsAncientTerms(allText))
                {
                    return "phim cổ trang/tu tiên Trung Quốc (donghua)";
                }
                return "phim hoạt hình Trung Quốc (donghua)";
            }
            else if (languageCode == "jpn")
            {
                return "anime Nhật Bản";
            }
            else if (languageCode == "kor")
            {
                return "phim hoạt hình Hàn Quốc";
            }
            
            return "phim hoạt hình";
        }

        private bool ContainsAncientTerms(string text)
        {
            // Một số keywords phổ biến trong phim cổ trang/tu tiên
            var ancientKeywords = new[] { "宗", "仙", "修", "道", "师", "宗门", "长老", "弟子", "师兄", "师姐" };
            return ancientKeywords.Any(keyword => text.Contains(keyword));
        }

        private string GetStyleGuide(string genre)
        {
            if (genre.Contains("cổ trang") || genre.Contains("tu tiên"))
            {
                return "Cổ điển, trang trọng, phù hợp bối cảnh tu tiên/võ hiệp";
            }
            else if (genre.Contains("anime"))
            {
                return "Tự nhiên, trẻ trung, phù hợp văn hóa Nhật Bản";
            }
            return "Tự nhiên, dễ hiểu, phù hợp đối tượng khán giả";
        }

        private (int CharCount, int LineCount) GetPromptStatistics(string prompt)
        {
            return (
                CharCount: prompt.Length,
                LineCount: prompt.Split('\n').Length
            );
        }

        private void BtnCancel_Click(object? sender, EventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            Log("Cancelling...");
        }

        private void BtnOpenDebugFolder_Click(object? sender, EventArgs e)
        {
            var debugFolder = Path.Combine(Path.GetTempPath(), "OCR_Debug");
            
            if (!Directory.Exists(debugFolder))
            {
                Directory.CreateDirectory(debugFolder);
                MessageBox.Show(
                    "Debug folder is empty.\n\n" +
                    "Enable 'Debug OCR' checkbox and run OCR to see debug images.\n\n" +
                    $"Folder: {debugFolder}",
                    "Debug Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", debugFolder);
                Log($"Opened debug folder: {debugFolder}");
            }
            catch (Exception ex)
            {
                Log($"ERROR opening debug folder: {ex.Message}");
                MessageBox.Show(
                    $"Cannot open folder automatically.\n\n" +
                    $"Please open manually:\n{debugFolder}\n\n" +
                    $"Error: {ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void DisplaySubtitles()
        {
            dgvSubtitles.Rows.Clear();

            foreach (var sub in _subtitles)
            {
                dgvSubtitles.Rows.Add(
                    sub.Index,
                    sub.StartTimeFormatted,
                    sub.EndTimeFormatted,
                    $"{sub.Duration}ms",
                    sub.Text.Replace("\n", " | ")
                );
            }

            Log($"Displaying {_subtitles.Count} subtitles");
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btnLoadVideo.Enabled = enabled;
            btnSelectRoi.Enabled = enabled && !string.IsNullOrEmpty(_videoPath);
            btnStartOcr.Enabled = enabled && !_selectedRoi.IsEmpty;
            btnCleanSubtitle.Enabled = enabled && _subtitles.Count > 0;
            btnFixTime.Enabled = enabled && _subtitles.Count > 0;
            btnExportSrt.Enabled = enabled && _subtitles.Count > 0;
            btnCreatePrompt.Enabled = enabled && _subtitles.Count > 0;
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                Invoke(() => Log(message));
                return;
            }
            
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{timestamp}] {message}\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired)
            {
                Invoke(() => SetStatus(status));
                return;
            }
            
            lblStatus.Text = status;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // Cancel any running operation
            _cancellationTokenSource?.Cancel();

            // Cleanup
            _ocrService?.Dispose();

            // Cleanup temp files
            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch
            {
                // Non-critical, ignore on close
            }
        }
        private string GetWindowsLanguageCode(int index)
        {
            return index switch
            {
                0 => "zh-Hans", // Chinese Simplified
                1 => "zh-Hant", // Chinese Traditional
                2 => "en-US",   // English
                3 => "ja-JP",   // Japanese
                4 => "ko-KR",   // Korean
                5 => "vi-VN",   // Vietnamese
                _ => "en-US"
            };
        }
    }
}
