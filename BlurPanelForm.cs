using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TunnelVision
{
    // Panel that renders a slice of a shared, pre-blurred desktop snapshot.
    //
    // The shared bitmap is owned by OverlayForm — all four panels hold a
    // reference to the same bitmap and just draw a different source rectangle
    // out of it. This avoids per-frame Bitmap.Clone (which was ~5-15 ms each,
    // times four panels, times 60 fps = unusable during window drags).
    //
    // The UI thread is the only writer to the shared reference, so panels
    // always see a consistent pointer even when the capture loop swaps it in.
    public class BlurPanelForm : Form
    {
        private Bitmap? _sharedFullBitmap;
        private Rectangle _sourceRect;
        private Color _tint = Color.Black;
        private byte _tintAlpha = 120;

        public BlurPanelForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Black;
            this.DoubleBuffered = true;
            this.Visible = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20;        // WS_EX_TRANSPARENT — click-through
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryExcludeFromCapture(this.Handle);
        }

        // Updates the shared bitmap pointer. Called by OverlayForm on the UI
        // thread every time a new capture finishes. The panel does NOT take
        // ownership — OverlayForm disposes old bitmaps after all panels have
        // switched.
        public void SetSharedBitmap(Bitmap? bitmap)
        {
            _sharedFullBitmap = bitmap;
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.Invalidate(); } catch { }
            }
        }

        // Updates just the source rect without touching the shared bitmap.
        // Called on every panel move so the content shown always matches the
        // panel's current screen position, at 60 fps, without any bitmap copy.
        public void SetSourceRect(Rectangle sourceRect)
        {
            _sourceRect = sourceRect;
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.Invalidate(); } catch { }
            }
        }

        public void UpdateTint(Color tint, double darknessPct)
        {
            _tint = tint;
            const int MinTintAlpha = 40;
            const int MaxTintAlpha = 190;
            int range = MaxTintAlpha - MinTintAlpha;
            _tintAlpha = (byte)(MinTintAlpha + (int)Math.Round(darknessPct * range));
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try { this.Invalidate(); } catch { }
            }
        }

        public void SetRect(Rectangle rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                if (this.Visible) this.Visible = false;
                return;
            }
            if (this.Bounds != rect)
            {
                this.Bounds = rect;
            }
            if (!this.Visible) this.Visible = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress default BackColor fill — OnPaint covers every pixel.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = _sharedFullBitmap;

            if (bg != null && _sourceRect.Width > 0 && _sourceRect.Height > 0)
            {
                try
                {
                    // Bilinear gives a slight extra softening and is much cheaper
                    // than HighQualityBilinear during drags; the content is
                    // already blurred so extra smoothing is unnecessary.
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(bg, new Rectangle(0, 0, this.Width, this.Height),
                        _sourceRect, GraphicsUnit.Pixel);
                }
                catch
                {
                    using var fill = new SolidBrush(_tint);
                    g.FillRectangle(fill, this.ClientRectangle);
                }
            }
            else
            {
                using var fill = new SolidBrush(_tint);
                g.FillRectangle(fill, this.ClientRectangle);
            }

            using var tintBrush = new SolidBrush(Color.FromArgb(_tintAlpha, _tint));
            g.FillRectangle(tintBrush, this.ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            // We never own the shared bitmap; OverlayForm cleans it up.
            _sharedFullBitmap = null;
            base.Dispose(disposing);
        }
    }
}
