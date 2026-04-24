using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TunnelVision
{
    // Fast "pretty enough" blur for the tunnel-vision backdrop panels.
    //
    // Strategy: downsample by 4x (the visual isn't critical — it's going behind
    // a tinted overlay), box-blur the small bitmap, then upscale with bilinear
    // interpolation. The Graphics.DrawImage smoothing does most of the final
    // softening for free. Three passes of box blur approximate a Gaussian.
    //
    // At 1920×1080 input this runs in a couple of milliseconds on modern HW —
    // fast enough to refresh the panel bitmaps every ~500 ms without breaking
    // a sweat.
    public static class ImageBlur
    {
        public static Bitmap FastBlur(Bitmap source, int downsample = 4, int boxRadius = 4, int passes = 3)
        {
            int smallW = Math.Max(1, source.Width / downsample);
            int smallH = Math.Max(1, source.Height / downsample);

            // Downscale with bilinear to smear out aliasing
            using var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, smallW, smallH),
                    new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }

            // Multi-pass box blur on the small bitmap (separable H then V each pass)
            for (int i = 0; i < passes; i++)
            {
                BoxBlurInPlace(small, boxRadius);
            }

            // Upscale back to original size
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(small, new Rectangle(0, 0, result.Width, result.Height),
                    new Rectangle(0, 0, smallW, smallH), GraphicsUnit.Pixel);
            }
            return result;
        }

        // Separable box blur, runs horizontally then vertically.
        // Uses a sliding-window sum so cost is O(pixels) regardless of radius.
        private static void BoxBlurInPlace(Bitmap bmp, int radius)
        {
            if (radius < 1) return;
            int w = bmp.Width, h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            int[] pixels = new int[w * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int[] temp = new int[w * h];
            BlurRows(pixels, temp, w, h, radius);
            BlurCols(temp, pixels, w, h, radius);

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
        }

        private static void BlurRows(int[] src, int[] dst, int w, int h, int r)
        {
            for (int y = 0; y < h; y++)
            {
                int rowStart = y * w;
                long sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                // Prime the window with the leading 'r' pixels
                for (int x = 0; x < Math.Min(r, w); x++)
                {
                    int p = src[rowStart + x];
                    sumR += (p >> 16) & 0xFF;
                    sumG += (p >> 8) & 0xFF;
                    sumB += p & 0xFF;
                    count++;
                }

                for (int x = 0; x < w; x++)
                {
                    // Slide in the new right edge
                    int xr = x + r;
                    if (xr < w)
                    {
                        int p = src[rowStart + xr];
                        sumR += (p >> 16) & 0xFF;
                        sumG += (p >> 8) & 0xFF;
                        sumB += p & 0xFF;
                        count++;
                    }
                    // Slide out the left edge
                    int xl = x - r - 1;
                    if (xl >= 0)
                    {
                        int p = src[rowStart + xl];
                        sumR -= (p >> 16) & 0xFF;
                        sumG -= (p >> 8) & 0xFF;
                        sumB -= p & 0xFF;
                        count--;
                    }
                    int avgR = (int)(sumR / count);
                    int avgG = (int)(sumG / count);
                    int avgB = (int)(sumB / count);
                    dst[rowStart + x] = unchecked((int)0xFF000000) | (avgR << 16) | (avgG << 8) | avgB;
                }
            }
        }

        private static void BlurCols(int[] src, int[] dst, int w, int h, int r)
        {
            for (int x = 0; x < w; x++)
            {
                long sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                for (int y = 0; y < Math.Min(r, h); y++)
                {
                    int p = src[y * w + x];
                    sumR += (p >> 16) & 0xFF;
                    sumG += (p >> 8) & 0xFF;
                    sumB += p & 0xFF;
                    count++;
                }

                for (int y = 0; y < h; y++)
                {
                    int yr = y + r;
                    if (yr < h)
                    {
                        int p = src[yr * w + x];
                        sumR += (p >> 16) & 0xFF;
                        sumG += (p >> 8) & 0xFF;
                        sumB += p & 0xFF;
                        count++;
                    }
                    int yl = y - r - 1;
                    if (yl >= 0)
                    {
                        int p = src[yl * w + x];
                        sumR -= (p >> 16) & 0xFF;
                        sumG -= (p >> 8) & 0xFF;
                        sumB -= p & 0xFF;
                        count--;
                    }
                    int avgR = (int)(sumR / count);
                    int avgG = (int)(sumG / count);
                    int avgB = (int)(sumB / count);
                    dst[y * w + x] = unchecked((int)0xFF000000) | (avgR << 16) | (avgG << 8) | avgB;
                }
            }
        }
    }
}
