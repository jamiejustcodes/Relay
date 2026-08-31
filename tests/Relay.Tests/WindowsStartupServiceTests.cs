using Relay.Infrastructure.Startup;
using Xunit;

namespace Relay.Tests;

public class WindowsStartupServiceTests
{
    [Fact]
    public void StartupService_CanCheckAndToggleStartupSafely()
    {
        var startupService = new WindowsStartupService();

        // Check initial state
        bool initialState = startupService.IsStartupEnabled();

        // Toggle state to enabled
        bool toggleResult = startupService.SetStartup(true, startMinimized: true);
        Assert.True(toggleResult);
        Assert.True(startupService.IsStartupEnabled());

        // Toggle state to disabled
        bool disableResult = startupService.SetStartup(false, startMinimized: true);
        Assert.True(disableResult);
        Assert.False(startupService.IsStartupEnabled());

        // Restore original state
        if (initialState)
        {
            startupService.SetStartup(true, startMinimized: true);
        }
    }
}
