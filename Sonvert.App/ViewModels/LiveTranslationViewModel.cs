using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.Translation;
using Sonvert.App.Services.Tts;

namespace Sonvert.App.ViewModels;

/// <summary>
/// "实时翻译"页面的 ViewModel。完整链路：识别 -> 翻译 -> 合成 -> 播放，
/// 四步都已经接上。UI 还是最简版本（原文+译文堆叠展示），美化留到
/// 所有逻辑跑通之后统一做。
/// </summary>
public partial class LiveTranslationViewModel : ViewModelBase
{
    private readonly IRecognitionSessionService _recognitionSession;
    private readonly ITranslationService _translationService;
    private readonly ITtsService _ttsService;
    private readonly IAudioPlaybackService _audioPlayback;

    public ObservableCollection<RecognitionResultItem> Results { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public LiveTranslationViewModel(
        IRecognitionSessionService recognitionSession,
        ITranslationService translationService,
        ITtsService ttsService,
        IAudioPlaybackService audioPlayback)
    {
        _recognitionSession = recognitionSession;
        _translationService = translationService;
        _ttsService = ttsService;
        _audioPlayback = audioPlayback;
        _recognitionSession.ResultReceived += OnResultReceived;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        ErrorMessage = null;
        try
        {
            // 翻译、合成两个子进程先并行拉起，减少总等待时间；
            // 识别那边内部包含 SenseVoice 的启动+加载，跟这两个并行。
            var loadTranslationTask = _translationService.StartAsync();
            var loadTtsTask = _ttsService.StartAsync();
            await _recognitionSession.StartAsync();
            await loadTranslationTask;
            await loadTtsTask;

            IsRunning = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _recognitionSession.StopAsync();
        await _translationService.StopAsync();
        await _ttsService.StopAsync();
        IsRunning = false;
    }

    private void OnResultReceived(object? sender, RecognitionResultEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var item = new RecognitionResultItem
            {
                Text = e.Text,
                Emotion = e.Emotion,
                Event = e.Event,
            };
            Results.Insert(0, item);

            // 原文先展示出来，译文+配音异步补上，不阻塞识别结果的展示速度。
            _ = TranslateAndSpeakAsync(item, e.Text, e.Language, e.Emotion);
        });
    }

    private async Task TranslateAndSpeakAsync(
        RecognitionResultItem item, string text, string? language, string? emotion)
    {
        // 目前只支持 zh<->en 这一对方向，其他语言先跳过，不报错。
        var (source, target) = language switch
        {
            "zh" => ("zh", "en"),
            "en" => ("en", "zh"),
            _ => ((string?)null, (string?)null),
        };

        if (source is null)
        {
            return;
        }

        string translatedText;
        try
        {
            var translationResult = await _translationService.TranslateAsync(text, source, target!);
            translatedText = translationResult.TranslatedText;
            item.TranslatedText = translatedText;
        }
        catch (Exception ex)
        {
            item.TranslatedText = $"[翻译失败: {ex.Message}]";
            return; // 翻译都失败了，没有译文可念，不继续往下走合成
        }

        try
        {
            // emotion 为 null 时交给 TtsRouter/LocalTtsService 内部退回 NEUTRAL 处理，
            // 这里不用重复判断。
            var ttsResult = await _ttsService.SynthesizeAsync(translatedText, target!, emotion ?? "NEUTRAL");
            await _audioPlayback.PlayAsync(ttsResult.AudioData);
        }
        catch (Exception ex)
        {
            // 合成/播放失败不影响已经显示出来的译文，只是这一句没声音——
            // 先用 Debug 输出方便联调阶段看到失败原因，不打断整体流程。
            System.Diagnostics.Debug.WriteLine($"[TTS] 合成或播放失败: {ex.Message}");
        }
    }
}