using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace Relay.Tests;

public class IconGeneratorTest
{
    [Fact]
    public void GenerateRelayIcon()
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var bitmaps = new List<Bitmap>();

        foreach (int size in sizes)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float margin = Math.Max(1.0f, size * 0.06f);
                var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);

                // Sleek indigo-purple gradient background
                using (var brush = new LinearGradientBrush(
                    rect,
                    Color.FromArgb(255, 99, 102, 241), // #6366F1
                    Color.FromArgb(255, 67, 56, 202),  // #4338CA
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillEllipse(brush, rect);
                }

                // Subtle outer border highlight
                using (var pen = new Pen(Color.FromArgb(160, 199, 210, 254), Math.Max(1.0f, size * 0.04f)))
                {
                    g.DrawEllipse(pen, rect);
                }

                // Sparkle ✦ in the center
                float cx = size / 2.0f;
                float cy = size / 2.0f;
                float rOut = size * 0.34f;
                float rIn = size * 0.09f;

                PointF[] pts =
                [
                    new PointF(cx, cy - rOut),
                    new PointF(cx + rIn, cy - rIn),
                    new PointF(cx + rOut, cy),
                    new PointF(cx + rIn, cy + rIn),
                    new PointF(cx, cy + rOut),
                    new PointF(cx - rIn, cy + rIn),
                    new PointF(cx - rOut, cy),
                    new PointF(cx - rIn, cy - rIn)
                ];

                // Glow
                using (var glowBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                {
                    g.FillPolygon(glowBrush, pts);
                }

                // Sparkle body
                using (var whiteBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                {
                    g.FillPolygon(whiteBrush, pts);
                }
            }
            bitmaps.Add(bmp);
        }

        string assetsDir = Path.GetFullPath(@"..\..\..\..\..\src\Relay.UI\Assets");
        if (!Directory.Exists(assetsDir))
        {
            Directory.CreateDirectory(assetsDir);
        }
        string icoPath = Path.Combine(assetsDir, "relay.ico");

        using (var fs = File.Create(icoPath))
        using (var bw = new BinaryWriter(fs))
        {
            // ICO Header
            bw.Write((ushort)0); // Reserved
            bw.Write((ushort)1); // Type 1 = Icon
            bw.Write((ushort)bitmaps.Count); // Count

            var pngStreams = new List<MemoryStream>();
            foreach (var bmp in bitmaps)
            {
                var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                pngStreams.Add(ms);
            }

            int offset = 6 + (bitmaps.Count * 16);

            for (int i = 0; i < bitmaps.Count; i++)
            {
                var bmp = bitmaps[i];
                var ms = pngStreams[i];
                byte w = bmp.Width >= 256 ? (byte)0 : (byte)bmp.Width;
                byte h = bmp.Height >= 256 ? (byte)0 : (byte)bmp.Height;

                bw.Write(w);
                bw.Write(h);
                bw.Write((byte)0); // Palette count
                bw.Write((byte)0); // Reserved
                bw.Write((ushort)1); // Color planes
                bw.Write((ushort)32); // Bits per pixel
                bw.Write((uint)ms.Length); // Size of image data
                bw.Write((uint)offset); // Offset of image data

                offset += (int)ms.Length;
            }

            for (int i = 0; i < pngStreams.Count; i++)
            {
                var ms = pngStreams[i];
                bw.Write(ms.ToArray());
                ms.Dispose();
                bitmaps[i].Dispose();
            }

            bw.Flush();
        }

        Assert.True(File.Exists(icoPath));
        Assert.True(new FileInfo(icoPath).Length > 1000);
    }
}
