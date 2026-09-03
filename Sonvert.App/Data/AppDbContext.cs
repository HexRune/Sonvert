using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Models;

namespace Sonvert.App.Data;

/// <summary>
/// 整个应用唯一的数据落盘方式：SQLite 数据库存结构化的元数据（文字、
/// 时间戳、各种关联 Id），大文件（音频）单独存在磁盘文件夹里，数据库
/// 只存指向这些文件的相对路径——这是"角色"和"历史记录"两块功能共用的
/// 存储模式，不是各自发明了一套。选相对路径而不是绝对路径，是为了让
/// 整个 AppData\Sonvert 目录可以被用户整体搬家/备份而不失效。
///
/// 数据库结构变化通过 EF Core Migrations 管理（Migrations 文件夹），
/// App.axaml.cs 里调用 db.Database.Migrate() 应用——从这次给
/// CharacterEmotionClips 加 Language 列开始，正式改成 Migrate()（之前
/// 一直是 EnsureCreated()，虽然 Migrations 文件夹早就有了，但那行代码
/// 没跟着切换，历史细节和处理办法见 App.axaml.cs 的 MigrateDatabase 方法）。
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterEmotionClip> CharacterEmotionClips => Set<CharacterEmotionClip>();
    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();
    public DbSet<GlossaryEntry> GlossaryEntries => Set<GlossaryEntry>();

    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sonvert");

    public static string CharactersAudioRoot { get; } = Path.Combine(AppDataRoot, "Characters");

    /// <summary>历史记录音频的根目录，下面按日期分文件夹存放
    /// （History/2026-08-21/、History/2026-08-22/ ...）。</summary>
    public static string HistoryRoot { get; } = Path.Combine(AppDataRoot, "History");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(AppDataRoot);
        var dbPath = Path.Combine(AppDataRoot, "sonvert.db");
        options.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>()
            .HasMany(c => c.EmotionClips)
            .WithOne(clip => clip.Character)
            .HasForeignKey(clip => clip.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // 历史记录经常按"某一天"这个条件查询/删除，给 Timestamp 加个索引，
        // 数据量大了之后这类按时间范围过滤的查询不会随记录数线性变慢。
        modelBuilder.Entity<HistoryEntry>()
            .HasIndex(e => e.Timestamp);
    }
}