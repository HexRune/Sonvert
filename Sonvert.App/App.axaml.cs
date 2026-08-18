using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.SenseVoice;
using Sonvert.App.Settings;
using Sonvert.App.ViewModels;
using Sonvert.App.Views;

namespace Sonvert.App;

public partial class App : Application
{
    // 整个应用生命周期内只有这一个 ServiceProvider，挂在 App 实例上，
    // 方便以后如果要弹新窗口/对话框，也能从这里拿到 DI 容器解析依赖，
    // 不需要每个地方各自维护一份。
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // 程序退出前必须优雅地停掉 SenseVoiceService 子进程（调用 /shutdown
            // + 等待/强制 Kill），否则会变成孤儿进程留在后台——但 Avalonia 的
            // 退出流程本身不是 async 的，ShutdownRequested 事件也不支持直接
            // await。这里用"先取消这次关闭、异步做完清理、再真正触发关闭"
            // 的套路：第一次收到关闭请求时 Cancel=true 挡住它，清理做完后
            // 再调用 desktop.Shutdown() 真正关闭，这次不再拦截（靠 _isExiting
            // 这个标志位区分是不是第二次进来）。
            var isExiting = false;
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (isExiting)
                {
                    return; // 第二次进来，是我们自己触发的真正关闭，放行
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
        // 全部注册成单例——这个桌面应用只有一个主窗口、一路识别会话，
        // 这些服务本来就应该在整个程序生命周期里只存在一份，不需要
        // 每次注入都创建新实例（尤其 SenseVoiceService 持有子进程和
        // HttpClient，绝对不能被创建出多份）。
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISenseVoiceService, SenseVoiceService>();
        services.AddSingleton<IRecognitionSessionService, RecognitionSessionService>();

        services.AddSingleton<LiveTranslationViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    private async Task CleanupAsync()
    {
        // 顺序：先停识别会话（麦克风采集、VAD、后台处理任务），
        // 再停 SenseVoiceService 子进程——反过来的话，识别会话的后台
        // 任务可能还在等一个正在进行的 RecognizeAsync 调用，这时候
        // 子进程被先关掉，那次调用会直接报错（虽然 RecognitionSessionService
        // 内部对识别失败有 try/catch 兜底，不会崩，但顺序对了更干净，
        // 不会产生本可以避免的报错日志）。
        var recognitionSession = Services.GetRequiredService<IRecognitionSessionService>();
        await recognitionSession.DisposeAsync();

        var senseVoiceService = Services.GetRequiredService<ISenseVoiceService>();
        await senseVoiceService.StopAsync();
    }
}
