using FluentAssertions;
using Relay.Infrastructure.Hotkeys;
using Relay.Infrastructure.ScreenCapture;
using Xunit;

namespace Relay.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void HotkeyParser_ControlSpace_ShouldReturnControlModifierAndSpaceKey()
    {
        var (modifiers, key) = HotkeyParser.Parse("Control", "Space");

        modifiers.Should().Be(NativeMethods.MOD_CONTROL);
        key.Should().Be(0x20); // VK_SPACE
    }

    [Fact]
    public void HotkeyParser_ControlAltS_ShouldReturnControlAndAltModifiers()
    {
        var (modifiers, key) = HotkeyParser.Parse("Control + Alt", "S");

        modifiers.Should().Be(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT);
        key.Should().Be(0x53); // 'S'
    }
}
