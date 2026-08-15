using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ScreenLens.Core.Interfaces;
using ScreenLens.Core.Models;

namespace ScreenLens.Infrastructure.ScreenCapture;

public class Win32ScreenCaptureService : IScreenCaptureService
{
    private const int MaxVisionDimension = 1568; // Gemini optimal vision dimension
    private const int ThumbnailMaxDimension = 280;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();

        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));

                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    double dpiScale = 1.0;
                    try
                    {
                        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
                        {
                            dpiScale = dpiX / 96.0;
                        }
                    }
                    catch
                    {
                        dpiScale = 1.0;
                    }

                    bool isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;

                    displays.Add(new DisplayInfo(
                        DeviceName: mi.szDevice,
                        Left: mi.rcMonitor.Left,
                        Top: mi.rcMonitor.Top,
                        Width: mi.rcMonitor.Width,
                        Height: mi.rcMonitor.Height,
                        DpiScale: dpiScale,
                        IsPrimary: isPrimary
                    ));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Fallback below
        }

        if (displays.Count == 0)
        {
            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;

            displays.Add(new DisplayInfo("Primary", left, top, width, height, 1.0, true));
        }

        return displays;
    }

    public Task<CaptureRegion> CaptureVirtualScreenAsync(CancellationToken ct = default)
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        return CaptureRegionAsync(left, top, width, height, 1.0, ct);
    }

    public Task<CaptureRegion> CaptureRegionAsync(int x, int y, int width, int height, double dpiScale = 1.0, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (width <= 0) width = 100;
            if (height <= 0) height = 100;

            try
            {
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                byte[] imageBytes = ProcessImageForAi(bitmap);
                byte[] thumbnailBytes = CreateThumbnail(bitmap);

                return new CaptureRegion
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    DpiScale = dpiScale,
                    ImageBytes = imageBytes,
                    ThumbnailBytes = thumbnailBytes,
                    CapturedAt = DateTime.UtcNow
                };
            }
            catch
            {
                return new CaptureRegion
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    DpiScale = dpiScale,
                    ImageBytes = Array.Empty<byte>(),
                    CapturedAt = DateTime.UtcNow
                };
            }
        }, ct);
    }

    private static byte[] ProcessImageForAi(Bitmap bitmap)
    {
        int originalWidth = bitmap.Width;
        int originalHeight = bitmap.Height;

        int targetWidth = originalWidth;
        int targetHeight = originalHeight;

        if (originalWidth > MaxVisionDimension || originalHeight > MaxVisionDimension)
        {
            double ratio = Math.Min((double)MaxVisionDimension / originalWidth, (double)MaxVisionDimension / originalHeight);
            targetWidth = Math.Max(1, (int)(originalWidth * ratio));
            targetHeight = Math.Max(1, (int)(originalHeight * ratio));
        }

        using var outputStream = new MemoryStream();

        if (targetWidth == originalWidth && targetHeight == originalHeight)
        {
            SaveAsJpeg(bitmap, outputStream, 88L);
        }
        else
        {
            using var resized = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
            }
            SaveAsJpeg(resized, outputStream, 88L);
        }

        return outputStream.ToArray();
    }

    private static byte[] CreateThumbnail(Bitmap bitmap)
    {
        int originalWidth = bitmap.Width;
        int originalHeight = bitmap.Height;

        double ratio = Math.Min((double)ThumbnailMaxDimension / originalWidth, (double)ThumbnailMaxDimension / originalHeight);
        int targetWidth = Math.Max(1, (int)(originalWidth * ratio));
        int targetHeight = Math.Max(1, (int)(originalHeight * ratio));

        using var thumb = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(thumb))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
        }

        using var ms = new MemoryStream();
        SaveAsJpeg(thumb, ms, 75L);
        return ms.ToArray();
    }

    private static void SaveAsJpeg(Bitmap bmp, Stream targetStream, long quality = 88L)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => e.MimeType == "image/jpeg")
                      ?? ImageCodecInfo.GetImageEncoders().First();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bmp.Save(targetStream, encoder, encoderParams);
    }
}
