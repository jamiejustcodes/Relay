using Microsoft.EntityFrameworkCore;
using Relay.Core.Interfaces;
using Relay.Core.Models;

namespace Relay.Infrastructure.Data;

public class SqliteHistoryRepository : IHistoryRepository
{
    private bool _initialized;

    private async Task EnsureDatabaseAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        using var db = new RelayDbContext();
        await db.Database.EnsureCreatedAsync(ct);
        _initialized = true;
    }

    public async Task<IReadOnlyList<HistoryItem>> GetHistoryAsync(int limit = 50, string? searchFilter = null, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var db = new RelayDbContext();

        var query = db.HistoryItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            string pattern = searchFilter.Trim();
            query = query.Where(h =>
                EF.Functions.Like(h.Title, $"%{pattern}%") ||
                EF.Functions.Like(h.Summary, $"%{pattern}%") ||
                EF.Functions.Like(h.ApplicationName, $"%{pattern}%") ||
                EF.Functions.Like(h.UserQuestion, $"%{pattern}%")
            );
        }

        var entities = await query
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<HistoryItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var db = new RelayDbContext();

        var entity = await db.HistoryItems.FindAsync(new object[] { id }, ct);
        return entity != null ? MapToDomain(entity) : null;
    }

    public async Task SaveHistoryItemAsync(HistoryItem item, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var db = new RelayDbContext();

        var existing = await db.HistoryItems.FindAsync(new object[] { item.Id }, ct);
        if (existing != null)
        {
            existing.Title = item.Title;
            existing.Summary = item.Summary;
            existing.MarkdownResponse = item.MarkdownResponse;
            existing.IsFavorite = item.IsFavorite;
            existing.ThumbnailBase64 = item.ThumbnailBase64;
            existing.Intent = (int)item.Intent;
        }
        else
        {
            var entity = new HistoryItemEntity
            {
                Id = item.Id,
                Timestamp = item.Timestamp,
                ApplicationName = item.ApplicationName,
                WindowTitle = item.WindowTitle,
                Intent = (int)item.Intent,
                UserQuestion = item.UserQuestion,
                Title = item.Title,
                Summary = item.Summary,
                MarkdownResponse = item.MarkdownResponse,
                ThumbnailBase64 = item.ThumbnailBase64,
                IsFavorite = item.IsFavorite,
                ImageWidth = item.ImageWidth,
                ImageHeight = item.ImageHeight
            };
            db.HistoryItems.Add(entity);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteHistoryItemAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var db = new RelayDbContext();

        var entity = await db.HistoryItems.FindAsync(new object[] { id }, ct);
        if (entity != null)
        {
            db.HistoryItems.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ClearAllHistoryAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);
        using var db = new RelayDbContext();

        db.HistoryItems.RemoveRange(db.HistoryItems);
        await db.SaveChangesAsync(ct);
    }

    private static HistoryItem MapToDomain(HistoryItemEntity entity)
    {
        return new HistoryItem
        {
            Id = entity.Id,
            Timestamp = entity.Timestamp,
            ApplicationName = entity.ApplicationName,
            WindowTitle = entity.WindowTitle,
            Intent = (IntentType)entity.Intent,
            UserQuestion = entity.UserQuestion,
            Title = entity.Title,
            Summary = entity.Summary,
            MarkdownResponse = entity.MarkdownResponse,
            ThumbnailBase64 = entity.ThumbnailBase64,
            IsFavorite = entity.IsFavorite,
            ImageWidth = entity.ImageWidth,
            ImageHeight = entity.ImageHeight
        };
    }
}
