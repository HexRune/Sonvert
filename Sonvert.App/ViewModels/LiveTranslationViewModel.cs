using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Recognition;

namespace Sonvert.App.ViewModels;

/// <summary>
/// "实时翻译"页面的 ViewModel。这一版只做"开始/停止 + 展示识别结果"，
/// 先验证 麦克风采集 -> VAD -> SenseVoice 识别 这条链路真的通，翻译和 TTS
/// 都还没接，Results 列表里先只看得到原文 + 情绪 + 事件，没有译文。
/// </summary>
public partial class LiveTranslationViewModel : ViewModelBase
{
    private readonly IRecognitionSessionService _recognitionSession;

    public ObservableCollection<RecognitionResultItem> Results { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public LiveTranslationViewModel(IRecognitionSessionService recognitionSession)
    {
        _recognitionSession = recognitionSession;
        _recognitionSession.ResultReceived += OnResultReceived;
    }

    // [RelayCommand] 是 CommunityToolkit.Mvvm 的 source generator：
    // 会自动生成一个 StartCommand 属性（IAsyncRelayCommand），界面上
    // Button.Command 绑到这个自动生成的属性上就行，不需要手写 ICommand。
    [RelayCommand]
    private async Task StartAsync()
    {
        ErrorMessage = null;
        try
        {
            await _recognitionSession.StartAsync();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            // 先简单展示异常信息，方便这一步联调时看到具体哪里失败
            // （比如 VAD 路径没配、SenseVoiceService 启动超时之类）。
            // 更友好的错误提示留到界面美化那一轮再做。
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _recognitionSession.StopAsync();
        IsRunning = false;
    }

    private void OnResultReceived(object? sender, RecognitionResultEventArgs e)
    {
        // ResultReceived 是在后台处理任务的线程上触发的，不是 UI 线程——
        // 必须用 Dispatcher.UIThread.Post 切回 UI 线程才能安全地改动
        // ObservableCollection，否则要么直接抛异常，要么界面不刷新。
        Dispatcher.UIThread.Post(() =>
        {
            Results.Insert(0, new RecognitionResultItem
            {
                Text = e.Text,
                Emotion = e.Emotion,
                Event = e.Event,
            });
        });
    }
}
