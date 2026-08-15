using System.IO;
using Microsoft.EntityFrameworkCore;
using Relay.Core.Models;

namespace Relay.Infrastructure.Data;

public class HistoryItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ApplicationName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public int Intent { get; set; }
    public string UserQuestion { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string MarkdownResponse { get; set; } = string.Empty;
    public string? ThumbnailBase64 { get; set; }
    public bool IsFavorite { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
}

public class SettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class RelayDbContext : DbContext
{
    public DbSet<HistoryItemEntity> HistoryItems => Set<HistoryItemEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    public static string DatabasePath
    {
        get
        {
            var relayPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Relay", "relay.db");
            var legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenLens", "screenlens.db");

            if (!File.Exists(relayPath) && File.Exists(legacyPath))
            {
                try
                {
                    var relayDir = Path.GetDirectoryName(relayPath)!;
                    if (!Directory.Exists(relayDir)) Directory.CreateDirectory(relayDir);
                    File.Copy(legacyPath, relayPath, overwrite: false);
                }
                catch { }
            }

            return relayPath;
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HistoryItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Intent);
        });

        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
        });
    }
}
