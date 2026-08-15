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

        // Toggle state
        bool toggleResult = startupService.SetStartup(true, startMinimized: true);
        Assert.True(toggleResult);
        Assert.True(startupService.IsStartupEnabled());

        // Revert to disabled or initial state
        bool disableResult = startupService.SetStartup(initialState, startMinimized: true);
        Assert.True(disableResult);
        Assert.Equal(initialState, startupService.IsStartupEnabled());
    }
}
