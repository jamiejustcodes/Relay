namespace Relay.Core.Models;

/// <summary>
/// Detailed display monitor info.
/// </summary>
public record DisplayInfo(
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    double DpiScale,
    bool IsPrimary
);

/// <summary>
/// Represents a captured rectangular screen region.
/// </summary>
public class CaptureRegion
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DpiScale { get; init; } = 1.0;
    public byte[] ImageBytes { get; init; } = Array.Empty<byte>();
    public byte[]? ThumbnailBytes { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    public bool IsEmpty => Width <= 0 || Height <= 0 || ImageBytes.Length == 0;
}
