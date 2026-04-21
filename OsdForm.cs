using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace TunnelVision
{
    // Lightweight on-screen display showing current intensity as a pill with progress bar.
    // Auto-dismisses after a short delay with fade-out.
    //
    // Brand colors: mint green accent (#75F09A) matching the app icon.
    // Rendering: solid form + Region-based rounded shape — no TransparencyKey,
    //   so no magenta/pink bleeding on anti-aliased edges.
    public class OsdForm : Form
    {
        private readonly Timer _hideTimer;
        private readonly Timer _fadeTimer;
        private int _percent;
        private string _label = "";

        // Brand palette (derived from app icon #75F09A)
        private static readonly Color BrandGreen = Color.FromArgb(117, 240, 154);
        private static readonly Color BrandGreenDark = Color.FromArgb(60, 200, 120);
        private static readonly Color Bg = Color.FromArgb(24, 26, 28);
        private static readonly Color BgBorder = Color.FromArgb(60, 64, 68);

        public OsdForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Bg;
            this.DoubleBuffered = true;
            this.Size = new Size(260, 84);
            this.Opacity = 0;

            // No manual GDI Region here — Region clipping is pixel-perfect (no AA)
            // and produces jagged corners. We rely on the DWM rounded-corner
            // attribute (applied in OnHandleCreated) so the compositor does the
            // rounding with proper anti-aliasing.

            _fadeTimer = new Timer { Interval = 20 };
            _fadeTimer.Tick += (s, e) =>
            {
                if (this.Opacity <= 0.05)
                {
                    this.Opacity = 0;
                    _fadeTimer.Stop();
                    this.Hide();
                }
                else
                {
                    this.Opacity = Math.Max(0, this.Opacity - 0.08);
                }
            };

            _hideTimer = new Timer { Interval = 1200 };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                _fadeTimer.Start();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x20;        // WS_EX_TRANSPARENT (click-through)
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Ask Windows 11 for native rounded corners in addition to our Region —
            // gives the shadow/outline a matching curve.
            NativeMethods.TryApplyRoundedCorners(this.Handle, small: false);
        }

        public void ShowIntensity(int percent, string label)
        {
            _percent = Math.Max(0, Math.Min(100, percent));
            _label = label ?? "";

            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            int x = screen.Left + (screen.Width - this.Width) / 2;
            int y = screen.Bottom - this.Height - 80;
            this.Location = new Point(x, y);

            _fadeTimer.Stop();
            _hideTimer.Stop();

            this.Opacity = 0.96;
            if (!this.Visible)
            {
                this.Show();
            }
            this.Invalidate();
            _hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var bounds = new Rectangle(0, 0, this.Width, this.Height);

            // Solid background painted over the whole rounded Region — no transparency trick needed.
            using (var bg = new SolidBrush(Bg))
            {
                g.FillRectangle(bg, bounds);
            }

            // Thin border inside the rounded shape (radius matches DWM's DWMWCP_ROUND ≈ 8px
            // so the painted stroke aligns with the window's anti-aliased outer edge).
            using (var pen = new Pen(BgBorder, 1f))
            using (var path = RoundedRect(new Rectangle(0, 0, this.Width - 1, this.Height - 1), 8))
            {
                g.DrawPath(pen, path);
            }

            // Label (top-left)
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var fore = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.DrawString(_label, font, fore, new PointF(20, 14));
            }

            // Percent value (top-right) in brand green
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var fore = new SolidBrush(BrandGreen))
            {
                string txt = _percent + "%";
                var sz = g.MeasureString(txt, font);
                g.DrawString(txt, font, fore, new PointF(this.Width - sz.Width - 20, 14));
            }

            // Progress track
            var track = new Rectangle(20, 50, this.Width - 40, 10);
            using (var path = RoundedRect(track, 5))
            using (var trackBrush = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
            {
                g.FillPath(trackBrush, path);
            }

            // Progress fill — brand green gradient
            int fillWidth = (int)(track.Width * (_percent / 100.0));
            if (fillWidth > 2)
            {
                var fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                using (var path = RoundedRect(fill, 5))
                using (var fillBrush = new LinearGradientBrush(fill, BrandGreenDark, BrandGreen, 0f))
                {
                    g.FillPath(fillBrush, path);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var arc = new Rectangle(bounds.Location, new Size(d, d));
            var path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideTimer?.Dispose();
                _fadeTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
