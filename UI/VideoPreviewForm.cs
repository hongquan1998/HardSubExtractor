using System.Drawing;

namespace HardSubExtractor.UI
{
    /// <summary>
    /// Form preview video voi seek bar de chon vi tri phu de
    /// </summary>
    public class VideoPreviewForm : Form
    {
        private readonly string _videoPath;
        private PictureBox _pictureBox = null!;
        private TrackBar _seekBar = null!;
        private Button _btnPlay = null!;
        private Button _btnPause = null!;
        private Button _btnPrevFrame = null!;
        private Button _btnNextFrame = null!;
        private Label _lblTime = null!;
        private Label _lblPosition = null!;
        private TextBox _txtSeekTime = null!;
        private Button _btnSeekGo = null!;
        private NumericUpDown _numFps = null!;
        private Button _btnOk = null!;
        private Button _btnCancel = null!;
        private Button _btnResetRoi = null!;
        private Button _btnAutoDetect = null!;
        private Label _lblRoiInfo = null!;
        
        private string? _currentFramePath;
        private int _currentFrameIndex;
        private List<string> _frameFiles = new();
        private System.Windows.Forms.Timer _playbackTimer = null!;
        
        // ROI selection
        private bool _isSelectingRoi = false;
        private Point _roiStartPoint;
        private Rectangle _selectedRoi = Rectangle.Empty;
        
        public string? SelectedFramePath { get; private set; }
        public TimeSpan SelectedTime { get; private set; }
        public Rectangle SelectedRoi { get; private set; }
        
        public VideoPreviewForm(string videoPath)
        {
            _videoPath = videoPath;
            InitializeComponent();
            this.Load += VideoPreviewForm_Load;
        }
        
        private void InitializeComponent()
        {
            this.Text = "Video Preview - Chon vi tri va vung phu de";
            this.Size = new Size(1280, 820);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            
            // Picture Box - Hien thi frame
            _pictureBox = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(1240, 600),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Cursor = Cursors.Cross
            };
            
            // Add mouse events for ROI selection
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;
            
            // Panel Controls
            var panelControls = new Panel
            {
                Location = new Point(10, 620),
                Size = new Size(1240, 180),
                BorderStyle = BorderStyle.FixedSingle
            };
            
            // Playback controls
            _btnPrevFrame = new Button
            {
                Text = "<< Frame",
                Location = new Point(10, 10),
                Size = new Size(80, 30)
            };
            _btnPrevFrame.Click += BtnPrevFrame_Click;
            
            _btnPlay = new Button
            {
                Text = "▶ Play",
                Location = new Point(100, 10),
                Size = new Size(80, 30)
            };
            _btnPlay.Click += BtnPlay_Click;
            
            _btnPause = new Button
            {
                Text = "⏸ Pause",
                Location = new Point(190, 10),
                Size = new Size(80, 30),
                Enabled = false
            };
            _btnPause.Click += BtnPause_Click;
            
            _btnNextFrame = new Button
            {
                Text = "Frame >>",
                Location = new Point(280, 10),
                Size = new Size(80, 30)
            };
            _btnNextFrame.Click += BtnNextFrame_Click;
            
            // Time display
            _lblTime = new Label
            {
                Text = "00:00:00",
                Location = new Point(380, 15),
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
            };
            
            _lblPosition = new Label
            {
                Text = "Frame: 0 / 0",
                Location = new Point(480, 15),
                AutoSize = true
            };
            
            // Seek controls
            var lblSeek = new Label
            {
                Text = "Tua den:",
                Location = new Point(650, 15),
                AutoSize = true
            };
            
            _txtSeekTime = new TextBox
            {
                Text = "00:00:00",
                Location = new Point(720, 12),
                Size = new Size(80, 25)
            };
            
            _btnSeekGo = new Button
            {
                Text = "Go",
                Location = new Point(810, 10),
                Size = new Size(50, 30)
            };
            _btnSeekGo.Click += BtnSeekGo_Click;
            
            var lblFps = new Label
            {
                Text = "FPS:",
                Location = new Point(880, 15),
                AutoSize = true
            };
            
            _numFps = new NumericUpDown
            {
                Location = new Point(920, 12),
                Size = new Size(60, 25),
                Minimum = 1,
                Maximum = 10,
                Value = 2
            };
            
            // Seek bar
            _seekBar = new TrackBar
            {
                Location = new Point(10, 50),
                Size = new Size(1210, 45),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10
            };
            _seekBar.ValueChanged += SeekBar_ValueChanged;
            
            // ROI Info
            _lblRoiInfo = new Label
            {
                Text = "ROI: Chua chon (Keo chuot tren video de chon vung phu de)",
                Location = new Point(10, 100),
                AutoSize = true,
                ForeColor = Color.Red,
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
            };
            
            _btnResetRoi = new Button
            {
                Text = "Reset ROI",
                Location = new Point(750, 95),
                Size = new Size(90, 30)
            };
            _btnResetRoi.Click += BtnResetRoi_Click;
            
            _btnAutoDetect = new Button
            {
                Text = "🔍 Auto Detect",
                Location = new Point(850, 95),
                Size = new Size(110, 30),
                BackColor = Color.LightBlue,
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
            };
            _btnAutoDetect.Click += BtnAutoDetect_Click;
            
            var lblInstructions = new Label
            {
                Text = "Huong dan: KEO CHUOT tren video hoac nhan Auto Detect de chon vung phu de",
                Location = new Point(10, 130),
                Size = new Size(700, 20),
                ForeColor = Color.Blue
            };
            
            // OK / Cancel
            _btnOk = new Button
            {
                Text = "OK - Xac nhan",
                Location = new Point(1020, 130),
                Size = new Size(150, 35),
                BackColor = Color.LightGreen,
                Enabled = false
            };
            _btnOk.Click += BtnOk_Click;
            
            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(1020, 170),
                Size = new Size(70, 25)
            };
            _btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            
            panelControls.Controls.AddRange(new Control[] {
                _btnPrevFrame, _btnPlay, _btnPause, _btnNextFrame,
                _lblTime, _lblPosition,
                lblSeek, _txtSeekTime, _btnSeekGo,
                lblFps, _numFps,
                _seekBar,
                _lblRoiInfo, _btnResetRoi, _btnAutoDetect,
                lblInstructions,
                _btnOk, _btnCancel
            });
            
            this.Controls.Add(_pictureBox);
            this.Controls.Add(panelControls);
            
            // Playback timer
            _playbackTimer = new System.Windows.Forms.Timer
            {
                Interval = 500 // 2 FPS default
            };
            _playbackTimer.Tick += PlaybackTimer_Tick;
        }
        
        private async void VideoPreviewForm_Load(object? sender, EventArgs e)
        {
            try
            {
                this.Cursor = Cursors.WaitCursor;
                
                // Extract frames cho preview
                var tempFolder = Path.Combine(Path.GetTempPath(), "HardSubExtractor", "preview");
                Directory.CreateDirectory(tempFolder);
                
                // Xoa frames cu
                foreach (var file in Directory.GetFiles(tempFolder, "*.png"))
                    File.Delete(file);
                
                var frameExtractor = new Services.FrameExtractor();
                
                IProgress<int> progress = new Progress<int>(percent =>
                {
                    _lblPosition.Text = $"Extracting frames... {percent}%";
                });
                
                var frames = await frameExtractor.ExtractFramesAsync(
                    _videoPath,
                    tempFolder,
                    (int)_numFps.Value,
                    progress,
                    CancellationToken.None);
                
                _frameFiles = frames.OrderBy(f => f.Timestamp).Select(f => f.FilePath).ToList();
                
                if (_frameFiles.Count == 0)
                {
                    MessageBox.Show("Khong extract duoc frame tu video!", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    this.DialogResult = DialogResult.Cancel;
                    return;
                }
                
                _seekBar.Maximum = _frameFiles.Count - 1;
                _seekBar.Value = 0;
                
                LoadFrame(0);
                
                _lblPosition.Text = $"Frame: 1 / {_frameFiles.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Loi khi load video: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }
        
        private void LoadFrame(int index)
        {
            if (index < 0 || index >= _frameFiles.Count)
                return;
            
            _currentFrameIndex = index;
            _currentFramePath = _frameFiles[index];
            
            // Load va hien thi frame
            if (_pictureBox.Image != null)
            {
                _pictureBox.Image.Dispose();
            }
            
            _pictureBox.Image = Image.FromFile(_currentFramePath);
            
            // Cap nhat time (gia su video bat dau tu 0:00:00)
            var fps = (double)_numFps.Value;
            var totalSeconds = index / fps;
            var time = TimeSpan.FromSeconds(totalSeconds);
            
            _lblTime.Text = time.ToString(@"hh\:mm\:ss");
            _lblPosition.Text = $"Frame: {index + 1} / {_frameFiles.Count}";
            
            SelectedTime = time;
            
            // Redraw ROI if selected
            _pictureBox.Invalidate();
        }
        
        #region ROI Selection
        
        private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _pictureBox.Image != null)
            {
                _isSelectingRoi = true;
                _roiStartPoint = ConvertToImageCoordinates(e.Location);
            }
        }
        
        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_isSelectingRoi && _pictureBox.Image != null)
            {
                var currentPoint = ConvertToImageCoordinates(e.Location);
                
                var x = Math.Min(_roiStartPoint.X, currentPoint.X);
                var y = Math.Min(_roiStartPoint.Y, currentPoint.Y);
                var width = Math.Abs(currentPoint.X - _roiStartPoint.X);
                var height = Math.Abs(currentPoint.Y - _roiStartPoint.Y);
                
                _selectedRoi = new Rectangle(x, y, width, height);
                _pictureBox.Invalidate();
            }
        }
        
        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _isSelectingRoi)
            {
                _isSelectingRoi = false;
                
                if (_selectedRoi.Width > 10 && _selectedRoi.Height > 10)
                {
                    UpdateRoiInfo();
                    _btnOk.Enabled = true;
                }
                else
                {
                    _selectedRoi = Rectangle.Empty;
                    _lblRoiInfo.Text = "ROI: Qua nho! Keo lai de chon vung lon hon";
                    _lblRoiInfo.ForeColor = Color.Red;
                    _btnOk.Enabled = false;
                }
            }
        }
        
        private void PictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_selectedRoi.Width > 0 && _selectedRoi.Height > 0 && _pictureBox.Image != null)
            {
                // Convert image coordinates to display coordinates
                var displayRect = ConvertToDisplayCoordinates(_selectedRoi);
                
                // Draw selection rectangle
                using var pen = new Pen(Color.Red, 2);
                e.Graphics.DrawRectangle(pen, displayRect);
                
                // Draw semi-transparent overlay outside ROI
                using var brush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
                var clientRect = _pictureBox.ClientRectangle;
                
                // Top
                e.Graphics.FillRectangle(brush, 0, 0, clientRect.Width, displayRect.Top);
                // Bottom
                e.Graphics.FillRectangle(brush, 0, displayRect.Bottom, clientRect.Width, clientRect.Height - displayRect.Bottom);
                // Left
                e.Graphics.FillRectangle(brush, 0, displayRect.Top, displayRect.Left, displayRect.Height);
                // Right
                e.Graphics.FillRectangle(brush, displayRect.Right, displayRect.Top, clientRect.Width - displayRect.Right, displayRect.Height);
            }
        }
        
        private Point ConvertToImageCoordinates(Point displayPoint)
        {
            if (_pictureBox.Image == null)
                return Point.Empty;
            
            var imageSize = _pictureBox.Image.Size;
            var displaySize = _pictureBox.ClientSize;
            
            // Calculate scale to fit
            float scaleX = (float)displaySize.Width / imageSize.Width;
            float scaleY = (float)displaySize.Height / imageSize.Height;
            float scale = Math.Min(scaleX, scaleY);
            
            // Calculate offset to center
            int displayWidth = (int)(imageSize.Width * scale);
            int displayHeight = (int)(imageSize.Height * scale);
            int offsetX = (displaySize.Width - displayWidth) / 2;
            int offsetY = (displaySize.Height - displayHeight) / 2;
            
            // Convert to image coordinates
            int imageX = (int)((displayPoint.X - offsetX) / scale);
            int imageY = (int)((displayPoint.Y - offsetY) / scale);
            
            // Clamp to image bounds
            imageX = Math.Max(0, Math.Min(imageX, imageSize.Width));
            imageY = Math.Max(0, Math.Min(imageY, imageSize.Height));
            
            return new Point(imageX, imageY);
        }
        
        private Rectangle ConvertToDisplayCoordinates(Rectangle imageRect)
        {
            if (_pictureBox.Image == null)
                return Rectangle.Empty;
            
            var imageSize = _pictureBox.Image.Size;
            var displaySize = _pictureBox.ClientSize;
            
            float scaleX = (float)displaySize.Width / imageSize.Width;
            float scaleY = (float)displaySize.Height / imageSize.Height;
            float scale = Math.Min(scaleX, scaleY);
            
            int displayWidth = (int)(imageSize.Width * scale);
            int displayHeight = (int)(imageSize.Height * scale);
            int offsetX = (displaySize.Width - displayWidth) / 2;
            int offsetY = (displaySize.Height - displayHeight) / 2;
            
            return new Rectangle(
                offsetX + (int)(imageRect.X * scale),
                offsetY + (int)(imageRect.Y * scale),
                (int)(imageRect.Width * scale),
                (int)(imageRect.Height * scale)
            );
        }
        
        private void UpdateRoiInfo()
        {
            _lblRoiInfo.Text = $"ROI: {_selectedRoi.Width}x{_selectedRoi.Height} at ({_selectedRoi.X},{_selectedRoi.Y})";
            _lblRoiInfo.ForeColor = Color.Green;
        }
        
        private void BtnResetRoi_Click(object? sender, EventArgs e)
        {
            _selectedRoi = Rectangle.Empty;
            _lblRoiInfo.Text = "ROI: Chua chon (Keo chuot tren video de chon vung phu de)";
            _lblRoiInfo.ForeColor = Color.Red;
            _btnOk.Enabled = false;
            _pictureBox.Invalidate();
        }
        
        private void BtnAutoDetect_Click(object? sender, EventArgs e)
        {
            if (_pictureBox.Image == null)
            {
                MessageBox.Show("Chua load frame nao!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            try
            {
                this.Cursor = Cursors.WaitCursor;
                _lblRoiInfo.Text = "Dang detect vung phu de...";
                _lblRoiInfo.ForeColor = Color.Orange;
                Application.DoEvents();
                
                // Use SubtitleRegionDetector to auto-detect
                var detector = new Services.SubtitleRegionDetector();
                
                // Get current frame as bitmap
                using var frameBitmap = new System.Drawing.Bitmap(_currentFramePath!);
                
                // Detect subtitle region
                _selectedRoi = detector.DetectSubtitleRegion(frameBitmap);
                var confidence = detector.LastConfidence;
                
                if (_selectedRoi.Width > 10 && _selectedRoi.Height > 10)
                {
                    _lblRoiInfo.Text = $"ROI: {_selectedRoi.Width}x{_selectedRoi.Height} at ({_selectedRoi.X},{_selectedRoi.Y}) - Confidence: {confidence:F0}%";
                    _lblRoiInfo.ForeColor = confidence >= 50 ? Color.Green : Color.Orange;
                    _btnOk.Enabled = true;
                    _pictureBox.Invalidate();
                    
                    if (confidence < 50)
                    {
                        MessageBox.Show(
                            $"Auto-detect tim thay vung phu de nhung confidence thap ({confidence:F0}%).\n\n" +
                            "Ban co the:\n" +
                            "1. Chap nhan va thu OCR\n" +
                            "2. Keo chuot de chinh sua ROI\n" +
                            "3. Tua den frame co phu de ro rang hon va detect lai",
                            "Low Confidence",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else
                {
                    _lblRoiInfo.Text = "Auto-detect khong tim thay vung phu de. Hay keo chuot de chon.";
                    _lblRoiInfo.ForeColor = Color.Red;
                    _btnOk.Enabled = false;
                    
                    MessageBox.Show(
                        "Khong tim thay vung phu de!\n\n" +
                        "Thu:\n" +
                        "1. Tua den frame co phu de ro rang\n" +
                        "2. Keo chuot de chon vung phu de thu cong",
                        "Detection Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                _lblRoiInfo.Text = $"Loi: {ex.Message}";
                _lblRoiInfo.ForeColor = Color.Red;
                MessageBox.Show($"Loi khi detect: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.Cursor = Cursors.Default;
            }
        }
        
        #endregion
        
        private void SeekBar_ValueChanged(object? sender, EventArgs e)
        {
            LoadFrame(_seekBar.Value);
        }
        
        private void BtnPlay_Click(object? sender, EventArgs e)
        {
            _playbackTimer.Interval = (int)(1000.0 / (double)_numFps.Value);
            _playbackTimer.Start();
            _btnPlay.Enabled = false;
            _btnPause.Enabled = true;
        }
        
        private void BtnPause_Click(object? sender, EventArgs e)
        {
            _playbackTimer.Stop();
            _btnPlay.Enabled = true;
            _btnPause.Enabled = false;
        }
        
        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentFrameIndex < _frameFiles.Count - 1)
            {
                _seekBar.Value = _currentFrameIndex + 1;
            }
            else
            {
                // End of video
                _playbackTimer.Stop();
                _btnPlay.Enabled = true;
                _btnPause.Enabled = false;
            }
        }
        
        private void BtnPrevFrame_Click(object? sender, EventArgs e)
        {
            if (_currentFrameIndex > 0)
            {
                _seekBar.Value = _currentFrameIndex - 1;
            }
        }
        
        private void BtnNextFrame_Click(object? sender, EventArgs e)
        {
            if (_currentFrameIndex < _frameFiles.Count - 1)
            {
                _seekBar.Value = _currentFrameIndex + 1;
            }
        }
        
        private void BtnSeekGo_Click(object? sender, EventArgs e)
        {
            try
            {
                // Parse time (HH:mm:ss)
                var timeParts = _txtSeekTime.Text.Split(':');
                if (timeParts.Length != 3)
                {
                    MessageBox.Show("Format sai! Dung: HH:mm:ss (vi du: 00:01:30)", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                var hours = int.Parse(timeParts[0]);
                var minutes = int.Parse(timeParts[1]);
                var seconds = int.Parse(timeParts[2]);
                
                var totalSeconds = hours * 3600 + minutes * 60 + seconds;
                var fps = (double)_numFps.Value;
                var frameIndex = (int)(totalSeconds * fps);
                
                if (frameIndex < 0 || frameIndex >= _frameFiles.Count)
                {
                    MessageBox.Show($"Thoi gian vuot qua video! Max: {_frameFiles.Count / fps}s", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                
                _seekBar.Value = frameIndex;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Loi: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void BtnOk_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFramePath))
            {
                MessageBox.Show("Chua co frame nao duoc chon!", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (_selectedRoi.IsEmpty || _selectedRoi.Width < 10 || _selectedRoi.Height < 10)
            {
                MessageBox.Show("Chua chon vung phu de (ROI)!\n\nKeo chuot tren video de chon vung phu de.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            SelectedFramePath = _currentFramePath;
            SelectedRoi = _selectedRoi;
            this.DialogResult = DialogResult.OK;
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _playbackTimer?.Dispose();
                _pictureBox?.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
