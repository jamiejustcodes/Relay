using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ScreenLens.Core.Interfaces;

namespace ScreenLens.Infrastructure.Ocr;

public class WindowsMediaOcrService : IOcrService
{
    private OcrEngine? _ocrEngine;
    private bool _initialized;

    private void EnsureEngine()
    {
        if (_initialized) return;

        try
        {
            _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages() 
                         ?? (OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("en-US")) 
                             ? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US")) 
                             : null);
        }
        catch
        {
            _ocrEngine = null;
        }
        finally
        {
            _initialized = true;
        }
    }

    public async Task<string> RecognizeTextAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return string.Empty;

        EnsureEngine();

        if (_ocrEngine == null)
            return string.Empty;

        try
        {
            using var memStream = new MemoryStream(imageBytes);
            using var randomAccessStream = new InMemoryRandomAccessStream();
            await randomAccessStream.WriteAsync(imageBytes.AsBuffer());
            randomAccessStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            return ocrResult.Text ?? string.Empty;
        }
        catch
        {
            // Non-critical local OCR fallback
            return string.Empty;
        }
    }
}
