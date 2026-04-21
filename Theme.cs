using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TunnelVision
{
    // Central palette + helpers for a cohesive Win11-style look that matches
    // the app icon (#75F09A mint green).
    public static class Theme
    {
        // Brand (icon)
        public static readonly Color Accent = Color.FromArgb(117, 240, 154);     // #75F09A
        public static readonly Color AccentHover = Color.FromArgb(140, 248, 175);
        public static readonly Color AccentDark = Color.FromArgb(60, 200, 120);

        // Dark palette (Fluent-ish)
        public static class Dark
        {
            public static readonly Color Background = Color.FromArgb(32, 32, 32);   // chrome
            public static readonly Color Surface = Color.FromArgb(43, 43, 43);      // cards / menu
            public static readonly Color SurfaceHover = Color.FromArgb(55, 55, 55);
            public static readonly Color SurfacePressed = Color.FromArgb(65, 65, 65);
            public static readonly Color Border = Color.FromArgb(64, 64, 64);
            public static readonly Color BorderSubtle = Color.FromArgb(50, 50, 50);
            public static readonly Color TextPrimary = Color.FromArgb(240, 240, 240);
            public static readonly Color TextSecondary = Color.FromArgb(170, 170, 170);
            public static readonly Color Separator = Color.FromArgb(56, 56, 56);
        }

        // Light palette
        public static class Light
        {
            public static readonly Color Background = Color.FromArgb(243, 243, 243);
            public static readonly Color Surface = Color.White;
            public static readonly Color SurfaceHover = Color.FromArgb(240, 240, 240);
            public static readonly Color SurfacePressed = Color.FromArgb(230, 230, 230);
            public static readonly Color Border = Color.FromArgb(225, 225, 225);
            public static readonly Color BorderSubtle = Color.FromArgb(235, 235, 235);
            public static readonly Color TextPrimary = Color.FromArgb(30, 30, 30);
            public static readonly Color TextSecondary = Color.FromArgb(100, 100, 100);
            public static readonly Color Separator = Color.FromArgb(230, 230, 230);
        }

        public static bool IsSystemDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? v = key.GetValue("AppsUseLightTheme");
                    if (v != null) return (int)v == 0;
                }
            }
            catch { }
            return false;
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var arc = new Rectangle(bounds.Location, new Size(d, d));
            var path = new GraphicsPath();

            if (radius <= 0)
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
    }

    // Fluent-style renderer for ContextMenuStrip.
    public class FluentMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public FluentMenuRenderer(bool dark) : base(new FluentColorTable(dark))
        {
            _dark = dark;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bg = _dark ? Theme.Dark.Surface : Theme.Light.Surface;
            var border = _dark ? Theme.Dark.Border : Theme.Light.Border;
            var rect = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);

            using var path = Theme.RoundedRect(rect, 8);
            using var bgBrush = new SolidBrush(bg);
            using var pen = new Pen(border, 1f);
            g.FillPath(bgBrush, path);
            g.DrawPath(pen, path);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* drawn in background */ }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Hide the default image margin vertical strip for a cleaner look.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var item = e.Item;
            if (!item.Selected && !item.Pressed) return;

            var bg = item.Pressed
                ? (_dark ? Theme.Dark.SurfacePressed : Theme.Light.SurfacePressed)
                : (_dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover);

            var rect = new Rectangle(4, 1, item.Width - 8, item.Height - 2);
            using var path = Theme.RoundedRect(rect, 5);
            using var b = new SolidBrush(bg);
            g.FillPath(b, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            var color = _dark ? Theme.Dark.Separator : Theme.Light.Separator;
            using var pen = new Pen(color, 1f);
            int y = e.Item.Height / 2;
            g.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var fg = _dark ? Theme.Dark.TextPrimary : Theme.Light.TextPrimary;
            e.TextColor = e.Item.Enabled ? fg : (_dark ? Theme.Dark.TextSecondary : Theme.Light.TextSecondary);
            base.OnRenderItemText(e);
        }
    }

    internal class FluentColorTable : ProfessionalColorTable
    {
        private readonly bool _dark;
        public FluentColorTable(bool dark) { _dark = dark; UseSystemColors = false; }

        public override Color ToolStripDropDownBackground => _dark ? Theme.Dark.Surface : Theme.Light.Surface;
        public override Color ImageMarginGradientBegin => _dark ? Theme.Dark.Surface : Theme.Light.Surface;
        public override Color ImageMarginGradientMiddle => _dark ? Theme.Dark.Surface : Theme.Light.Surface;
        public override Color ImageMarginGradientEnd => _dark ? Theme.Dark.Surface : Theme.Light.Surface;
        public override Color MenuBorder => _dark ? Theme.Dark.Border : Theme.Light.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => _dark ? Theme.Dark.SurfaceHover : Theme.Light.SurfaceHover;
        public override Color MenuItemPressedGradientBegin => _dark ? Theme.Dark.SurfacePressed : Theme.Light.SurfacePressed;
        public override Color MenuItemPressedGradientEnd => _dark ? Theme.Dark.SurfacePressed : Theme.Light.SurfacePressed;
        public override Color SeparatorDark => _dark ? Theme.Dark.Separator : Theme.Light.Separator;
        public override Color SeparatorLight => _dark ? Theme.Dark.Separator : Theme.Light.Separator;
    }
}
