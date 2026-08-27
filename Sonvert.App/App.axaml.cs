using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sonvert.App.Data;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.SenseVoice;
using Sonvert.App.Services.Subtitle;
using Sonvert.App.Services.Translation;
using Sonvert.App.Services.Tts;
using Sonvert.App.Settings;
using Sonvert.App.ViewModels;
using Sonvert.App.Views;
using System;
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
                db.Database.EnsureCreated();
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
        services.AddSingleton<ITranslationService, TranslationRouter>();

        services.AddSingleton<LocalTtsService>();
        services.AddSingleton<ApiTtsService>();
        services.AddSingleton<ITtsService, TtsRouter>();

        services.AddSingleton<ICharacterRepository, CharacterRepository>();
        services.AddSingleton<LegacySettingsMigrator>();

        services.AddSingleton<IAudioPlaybackService, NAudioPlaybackService>();
        services.AddSingleton<IAudioRecordingService, NAudioRecordingService>();
        services.AddSingleton<VoiceCloningViewModel>();
        services.AddSingleton<IPlaybackQueueService, PlaybackQueueService>();

        services.AddSingleton<Sonvert.App.Services.History.IHistoryRepository,
        Sonvert.App.Services.History.HistoryRepository>();
        services.AddSingleton<Sonvert.App.Services.History.HistoryRetentionCleaner>();

        services.AddSingleton<IGlossaryRepository, GlossaryRepository>();

        services.AddSingleton<HistoryViewModel>();

        services.AddSingleton<SubtitleWindowViewModel>();
        services.AddSingleton<ISubtitleWindowService, SubtitleWindowService>();
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
}