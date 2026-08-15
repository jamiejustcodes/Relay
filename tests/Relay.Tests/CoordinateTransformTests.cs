using FluentAssertions;
using Relay.Core.Models;
using Xunit;

namespace Relay.Tests;

public class CoordinateTransformTests
{
    [Theory]
    [InlineData(100, 100, 200, 200, 1.0, 100, 100, 200, 200)]
    [InlineData(100, 100, 200, 200, 1.25, 125, 125, 250, 250)]
    [InlineData(100, 100, 200, 200, 1.5, 150, 150, 300, 300)]
    [InlineData(50, 80, 400, 300, 2.0, 100, 160, 800, 600)]
    public void PhysicalCoordinates_ShouldScaleCorrectlyWithDpi(
        double dipX, double dipY, double dipW, double dipH, double dpiScale,
        int expectedX, int expectedY, int expectedW, int expectedH)
    {
        int physicalX = (int)Math.Round(dipX * dpiScale);
        int physicalY = (int)Math.Round(dipY * dpiScale);
        int physicalW = (int)Math.Round(dipW * dpiScale);
        int physicalH = (int)Math.Round(dipH * dpiScale);

        physicalX.Should().Be(expectedX);
        physicalY.Should().Be(expectedY);
        physicalW.Should().Be(expectedW);
        physicalH.Should().Be(expectedH);
    }

    [Fact]
    public void CaptureRegion_IsEmpty_ShouldBeTrue_WhenDimensionsAreZero()
    {
        var region = new CaptureRegion
        {
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            ImageBytes = Array.Empty<byte>()
        };

        region.IsEmpty.Should().BeTrue();
    }
}
