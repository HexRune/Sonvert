using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sonvert.App.Data;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.SenseVoice;
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

        services.AddSingleton<HistoryViewModel>();
    }

    private async Task CleanupAsync()
    {
        var recognitionSession = Services.GetRequiredService<IRecognitionSessionService>();
        await recognitionSession.DisposeAsync();

        var senseVoiceService = Services.GetRequiredService<ISenseVoiceService>();
        await senseVoiceService.StopAsync();

        var translationService = Services.GetRequiredService<ITranslationService>();
        await translationService.StopAsync();

        var ttsService = Services.GetRequiredService<ITtsService>();
        await ttsService.StopAsync();

        var playbackQueue = Services.GetRequiredService<IPlaybackQueueService>();
        await ((IAsyncDisposable)playbackQueue).DisposeAsync();
    }
}