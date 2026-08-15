using FluentAssertions;
using Relay.Core.Models;
using Relay.Infrastructure.Data;
using Xunit;

namespace Relay.Tests;

public class HistoryRepositoryTests
{
    [Fact]
    public async Task SqliteHistoryRepository_SaveAndRetrieve_ShouldWork()
    {
        var repo = new SqliteHistoryRepository();

        var item = new HistoryItem
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            ApplicationName = "Visual Studio",
            WindowTitle = "App.xaml.cs",
            Intent = IntentType.Debug,
            UserQuestion = "Fix this error",
            Title = "CS0104 Ambiguous Reference",
            Summary = "The type is ambiguous between two imported namespaces.",
            MarkdownResponse = "To resolve this, specify the fully qualified namespace."
        };

        await repo.SaveHistoryItemAsync(item);

        var retrieved = await repo.GetByIdAsync(item.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("CS0104 Ambiguous Reference");
        retrieved.ApplicationName.Should().Be("Visual Studio");
        retrieved.Intent.Should().Be(IntentType.Debug);

        var searchResults = await repo.GetHistoryAsync(10, "Ambiguous");
        searchResults.Should().Contain(h => h.Id == item.Id);

        // Cleanup
        await repo.DeleteHistoryItemAsync(item.Id);
        var afterDelete = await repo.GetByIdAsync(item.Id);
        afterDelete.Should().BeNull();
    }
}
