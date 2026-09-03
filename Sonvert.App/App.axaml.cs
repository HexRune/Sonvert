using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonvert.App.Data;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Services.Dialogs;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.SenseVoice;
using Sonvert.App.Services.Subtitle;
using Sonvert.App.Services.Translation;
using Sonvert.App.Services.Tts;
using Sonvert.App.Settings;
using Sonvert.App.ViewModels;
using Sonvert.App.Views;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sonvert.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // 改成 async void——这是应用生命周期的"最后一环"，没有任何调用方
    // 在等它的返回值，属于可以接受使用 async void 的例外场景（正常情况下
    // 应该优先用 async Task，这里是因为 Avalonia 框架本身定义的这个方法
    // 签名不能改，只能用 async void 来支持里面的 await）。
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            using (var db = new AppDbContext())
            {
                // ===== 重要：这次改动把数据库初始化从 EnsureCreated() 换成
                // 了正式的 Migrate()，原因和处理办法写在 MigrateDatabase 里，
                // 这是本次改动里唯一有真实风险、建议你重点自己核实一遍的
                // 地方（我这边没有 dotnet SDK，没法实际跑起来验证 SQLite
                // 这几条语句）。=====
                MigrateDatabase(db);
            }

            var migrator = Services.GetRequiredService<LegacySettingsMigrator>();
            migrator.RunAsync().GetAwaiter().GetResult(); // 同步等它跑完，不用 await
            var historyCleaner = Services.GetRequiredService<Sonvert.App.Services.History.HistoryRetentionCleaner>();
            historyCleaner.RunAsync().GetAwaiter().GetResult(); // 跟 migrator 一样同步等它跑完，原因见之前的窗口显示问题修复

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // 默认的 OnLastWindowClose 会等"所有窗口都关闭"才触发 ShutdownRequested——
            // 但字幕窗口是独立于主窗口存在的顶层窗口，它不会自己关闭，只能靠
            // ShutdownRequested 里的清理逻辑去关它，这就形成了死结：字幕窗口要等
            // ShutdownRequested 触发才能关，ShutdownRequested 又要等所有窗口
            // （包括字幕窗口）先关闭才会触发。改成 OnMainWindowClose，只要主窗口
            // 一关就触发关闭流程，不管这时候还有没有别的窗口（比如字幕窗口）开着。
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var isExiting = false;
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (isExiting)
                {
                    return;
                }

                e.Cancel = true;
                isExiting = true;

                await CleanupAsync();

                desktop.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISenseVoiceService, SenseVoiceService>();
        services.AddSingleton<IRecognitionSessionService, RecognitionSessionService>();

        services.AddSingleton<LiveTranslationViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<LocalTranslationService>();
        services.AddSingleton<ApiTranslationService>();
        services.AddSingleton<AzureTranslationService>();
        services.AddSingleton<ITranslationService, TranslationRouter>();

        services.AddSingleton<LocalTtsService>();
        services.AddSingleton<ApiTtsService>();
        services.AddSingleton<AzureTtsService>();
        services.AddSingleton<ITtsService, TtsRouter>();

        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<LegacySettingsMigrator>();

        services.AddSingleton<IAudioPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IAudioRecordingService, NAudioRecordingService>();
        services.AddSingleton<VoiceCloningViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<IPlaybackQueueService, PlaybackQueueService>();

        services.AddSingleton<Sonvert.App.Services.History.IHistoryRepository,
        Sonvert.App.Services.History.HistoryRepository>();
        services.AddSingleton<Sonvert.App.Services.History.HistoryRetentionCleaner>();

        services.AddSingleton<IGlossaryRepository, GlossaryRepository>();

        services.AddSingleton<HistoryViewModel>();

        services.AddSingleton<SubtitleWindowViewModel>();
        services.AddSingleton<ISubtitleWindowService, SubtitleWindowService>();
        services.AddSingleton<IDialogService, DialogService>();
    }

    private async Task CleanupAsync()
    {
        await SafeCleanupStepAsync("字幕窗口", () =>
        {
            Services.GetRequiredService<ISubtitleWindowService>().Hide();
            return Task.CompletedTask;
        });

        await SafeCleanupStepAsync("识别会话", async () =>
        {
            await Services.GetRequiredService<IRecognitionSessionService>().DisposeAsync();
        });

        await SafeCleanupStepAsync("SenseVoice 服务", async () =>
        {
            await Services.GetRequiredService<ISenseVoiceService>().StopAsync();
        });

        await SafeCleanupStepAsync("翻译服务", async () =>
        {
            await Services.GetRequiredService<ITranslationService>().StopAsync();
        });

        await SafeCleanupStepAsync("TTS 服务", async () =>
        {
            await Services.GetRequiredService<ITtsService>().StopAsync();
        });

        var playbackQueue = Services.GetRequiredService<IPlaybackQueueService>();
        await ((IAsyncDisposable)playbackQueue).DisposeAsync();
    }

    private static async Task SafeCleanupStepAsync(string stepName, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            // 任何一步清理失败，都不应该阻止程序继续走完剩下的清理步骤、
            // 最终正常退出——最坏情况就是这一步没清理干净（比如某个子进程
            // 没能优雅停掉），但至少不会导致整个程序卡死需要强制结束。
            System.Diagnostics.Debug.WriteLine($"[退出清理] {stepName} 清理失败: {ex}");
        }
    }

    /// <summary>
    /// 历史遗留问题：这个项目早期一直用 db.Database.EnsureCreated() 建表，
    /// 后来虽然规范地加上了 EF Core Migrations（Migrations 文件夹下那几个
    /// 文件），但这里一直没有真正切换成 db.Database.Migrate()——两者不能
    /// 混用：EnsureCreated 不会记录"迁移历史"（__EFMigrationsHistory
    /// 这张表），如果直接把这一行换成 Migrate()，EF Core 会以为一个迁移
    /// 都没跑过，尝试把 InitialBaseline 之后那两个"建表"的迁移
    /// （AddHistoryEntries/AddGlossaryEntries）重新执行一遍，而这些表其实
    /// 已经在磁盘上真实存在了（是 EnsureCreated 建的），会直接报"表已存在"
    /// 的错误，导致老用户升级后直接崩溃打不开。
    ///
    /// 这次要给 CharacterEmotionClips 表加 Language 这一列——这是真正的
    /// "修改已有表结构"，EnsureCreated 完全处理不了这种情况（它只在数据库
    /// 文件不存在时才会创建，已经存在的库不会被同步任何结构变化），所以
    /// 这次必须切到 Migrate()，顺便把"一直没切换"这个历史包袱一并还清。
    ///
    /// 处理办法：如果数据库文件已经有 Characters 表、但还没有迁移历史表，
    /// 说明这是一个老的、由 EnsureCreated 建出来的库——先把"在 EnsureCreated
    /// 时代就已经生效"的那三个迁移（InitialBaseline/AddHistoryEntries/
    /// AddGlossaryEntries）直接标记成"已应用"（不实际执行它们的 Up()，
    /// 因为对应的表结构本来就已经存在了），再调用 Migrate()，这样 Migrate()
    /// 就只会真正执行这次新增的 AddLanguageToCharacterEmotionClips。
    /// 全新安装（数据库文件还不存在）不受这段特殊处理影响，Migrate() 会
    /// 按正常顺序把所有迁移从头跑一遍。
    ///
    /// 这部分因为要手写原始 SQL 判断表是否存在、手动插入迁移历史记录，
    /// 是本次改动里唯一没法在当前环境编译验证的地方，强烈建议你在 VS 里
    /// 用一份现有的、已经录过角色数据的 sonvert.db 副本实际跑一遍这个
    /// 升级路径，确认没有崩溃、角色数据还在，再拿真实数据库去跑。
    /// </summary>
    private static void MigrateDatabase(AppDbContext db)
    {
        const string legacyProductVersion = "10.0.11"; // 跟其余 Migration 文件里 HasAnnotation("ProductVersion", ...) 的值保持一致

        if (db.Database.GetAppliedMigrations().Any())
        {
            // 迁移历史表存在且有记录，说明已经在正常走 Migrate() 这条路了
            // （比如全新安装，或者已经完成过一次这里的迁移），直接往下走
            // 正常的 Migrate() 就行，不需要特殊处理。
            db.Database.Migrate();
            return;
        }

        // 迁移历史是空的——可能是真·全新安装，也可能是老的 EnsureCreated
        // 库。用一个从项目一开始就有的表名（Characters）判断磁盘上是不是
        // 已经有真实数据，从而区分这两种情况。
        var connection = db.Database.GetDbConnection();
        connection.Open();
        try
        {
            bool charactersTableExists;
            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Characters'";
                charactersTableExists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
            }

            if (!charactersTableExists)
            {
                // 真·全新安装，交给下面的 Migrate() 从头跑所有迁移，
                // 行为和一直都在用 Migrate() 没有区别。
                return;
            }

            // 老的 EnsureCreated 库：手动建好迁移历史表（如果还没有的话），
            // 把 EnsureCreated 时代就已经生效的三个迁移标记成"已应用"。
            using (var createHistoryTableCommand = connection.CreateCommand())
            {
                createHistoryTableCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                        "ProductVersion" TEXT NOT NULL
                    );
                    """;
                createHistoryTableCommand.ExecuteNonQuery();
            }

            string[] legacyMigrationIds =
            [
                "20260823031438_InitialBaseline",
                "20260823031943_AddHistoryEntries",
                "20260824015742_AddGlossaryEntries",
            ];

            foreach (var migrationId in legacyMigrationIds)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText =
                    "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ($id, $version)";

                var idParam = insertCommand.CreateParameter();
                idParam.ParameterName = "$id";
                idParam.Value = migrationId;
                insertCommand.Parameters.Add(idParam);

                var versionParam = insertCommand.CreateParameter();
                versionParam.ParameterName = "$version";
                versionParam.Value = legacyProductVersion;
                insertCommand.Parameters.Add(versionParam);

                insertCommand.ExecuteNonQuery();
            }
        }
        finally
        {
            connection.Close();
        }

        // 迁移历史补齐之后，Migrate() 就只会执行这次真正新增的那个迁移了。
        db.Database.Migrate();
    }
}