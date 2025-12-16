using System.Drawing;

namespace HardSubExtractor.UI
{
    /// <summary>
    /// Form ?? user ch?n vùng ph? ?? (ROI - Region of Interest)
    /// </summary>
    public partial class RoiSelectorForm : Form
    {
        private Image? _videoFrame;
        private Rectangle _selectedRoi;
        private Point _startPoint;
        private bool _isSelecting;

        public Rectangle SelectedRoi => _selectedRoi;

        public RoiSelectorForm(string frameImagePath)
        {
            InitializeComponent();
            LoadFrame(frameImagePath);
        }

        public RoiSelectorForm(Image frameImage)
        {
            InitializeComponent();
            _videoFrame = frameImage;
            InitializeUI();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Subtitle Region (ROI)";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // PictureBox hi?n th? frame
            var pictureBox = new PictureBox
            {
                Name = "pictureBoxFrame",
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            pictureBox.MouseDown += PictureBox_MouseDown;
            pictureBox.MouseMove += PictureBox_MouseMove;
            pictureBox.MouseUp += PictureBox_MouseUp;
            pictureBox.Paint += PictureBox_Paint;

            // Panel ch?a buttons
            var panelButtons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                Padding = new Padding(10)
            };

            // Button OK
            var btnOk = new Button
            {
                Text = "OK - Use Selected Area",
                DialogResult = DialogResult.OK,
                Size = new Size(180, 35),
                Location = new Point(10, 10)
            };

            // Button Cancel
            var btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 35),
                Location = new Point(200, 10)
            };

            // Button Reset
            var btnReset = new Button
            {
                Text = "Reset Selection",
                Size = new Size(120, 35),
                Location = new Point(310, 10)
            };
            btnReset.Click += (s, e) =>
            {
                _selectedRoi = Rectangle.Empty;
                pictureBox.Invalidate();
            };

            // Label h??ng d?n
            var lblInstruction = new Label
            {
                Text = "Drag mouse to select subtitle area",
                AutoSize = true,
                Location = new Point(450, 20),
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
            };

            panelButtons.Controls.AddRange(new Control[] { btnOk, btnCancel, btnReset, lblInstruction });

            this.Controls.Add(pictureBox);
            this.Controls.Add(panelButtons);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void LoadFrame(string framePath)
        {
            if (!File.Exists(framePath))
            {
                MessageBox.Show("Frame image không t?n t?i!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return;
            }

            _videoFrame = Image.FromFile(framePath);
            InitializeUI();
        }

        private void InitializeUI()
        {
            var pictureBox = this.Controls.Find("pictureBoxFrame", true).FirstOrDefault() as PictureBox;
            if (pictureBox != null && _videoFrame != null)
            {
                pictureBox.Image = _videoFrame;
            }
        }

        private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSelecting = true;
                _startPoint = ConvertToImageCoordinates(e.Location);
            }
        }

        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_isSelecting)
            {
                var currentPoint = ConvertToImageCoordinates(e.Location);
                
                var x = Math.Min(_startPoint.X, currentPoint.X);
                var y = Math.Min(_startPoint.Y, currentPoint.Y);
                var width = Math.Abs(currentPoint.X - _startPoint.X);
                var height = Math.Abs(currentPoint.Y - _startPoint.Y);

                _selectedRoi = new Rectangle(x, y, width, height);

                var pictureBox = sender as PictureBox;
                pictureBox?.Invalidate();
            }
        }

        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isSelecting = false;
            }
        }

        private void PictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_selectedRoi.Width > 0 && _selectedRoi.Height > 0)
            {
                var pictureBox = sender as PictureBox;
                if (pictureBox?.Image == null) return;

                // Convert image coordinates to control coordinates
                var controlRect = ConvertToControlCoordinates(_selectedRoi, pictureBox);

                // V? rectangle selection
                using var pen = new Pen(Color.Red, 2);
                e.Graphics.DrawRectangle(pen, controlRect);

                // V? background semi-transparent
                using var brush = new SolidBrush(Color.FromArgb(50, Color.Red));
                e.Graphics.FillRectangle(brush, controlRect);

                // V? text hi?n th? kích th??c
                var sizeText = $"{_selectedRoi.Width} x {_selectedRoi.Height}";
                using var font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.Yellow);
                e.Graphics.DrawString(sizeText, font, textBrush, controlRect.X, controlRect.Y - 20);
            }
        }

        /// <summary>
        /// Convert control coordinates sang image coordinates
        /// </summary>
        private Point ConvertToImageCoordinates(Point controlPoint)
        {
            var pictureBox = this.Controls.Find("pictureBoxFrame", true).FirstOrDefault() as PictureBox;
            if (pictureBox?.Image == null)
                return controlPoint;

            var image = pictureBox.Image;
            var control = pictureBox;

            // Tính t? l? scale
            float scaleX = (float)image.Width / control.ClientSize.Width;
            float scaleY = (float)image.Height / control.ClientSize.Height;
            float scale = Math.Max(scaleX, scaleY);

            // Tính offset (center image)
            var scaledWidth = image.Width / scale;
            var scaledHeight = image.Height / scale;
            var offsetX = (control.ClientSize.Width - scaledWidth) / 2;
            var offsetY = (control.ClientSize.Height - scaledHeight) / 2;

            // Convert
            var imageX = (int)((controlPoint.X - offsetX) * scale);
            var imageY = (int)((controlPoint.Y - offsetY) * scale);

            // Clamp
            imageX = Math.Max(0, Math.Min(imageX, image.Width));
            imageY = Math.Max(0, Math.Min(imageY, image.Height));

            return new Point(imageX, imageY);
        }

        /// <summary>
        /// Convert image coordinates sang control coordinates
        /// </summary>
        private Rectangle ConvertToControlCoordinates(Rectangle imageRect, PictureBox pictureBox)
        {
            if (pictureBox.Image == null)
                return imageRect;

            var image = pictureBox.Image;

            // Tính t? l? scale
            float scaleX = (float)image.Width / pictureBox.ClientSize.Width;
            float scaleY = (float)image.Height / pictureBox.ClientSize.Height;
            float scale = Math.Max(scaleX, scaleY);

            // Tính offset
            var scaledWidth = image.Width / scale;
            var scaledHeight = image.Height / scale;
            var offsetX = (pictureBox.ClientSize.Width - scaledWidth) / 2;
            var offsetY = (pictureBox.ClientSize.Height - scaledHeight) / 2;

            // Convert
            var x = (int)(imageRect.X / scale + offsetX);
            var y = (int)(imageRect.Y / scale + offsetY);
            var width = (int)(imageRect.Width / scale);
            var height = (int)(imageRect.Height / scale);

            return new Rectangle(x, y, width, height);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // Validate ROI
            if (this.DialogResult == DialogResult.OK)
            {
                if (_selectedRoi.Width < 10 || _selectedRoi.Height < 10)
                {
                    MessageBox.Show("Please select a valid subtitle area!", "Invalid Selection", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            }
        }
    }
}
