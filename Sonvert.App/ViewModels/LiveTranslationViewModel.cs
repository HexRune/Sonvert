using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.History;
using Sonvert.App.Services.Recognition;
using Sonvert.App.Services.Translation;
using Sonvert.App.Services.Tts;
using Sonvert.App.Settings;

namespace Sonvert.App.ViewModels;

/// <summary>
/// "实时翻译"页面的 ViewModel，是整条同传链路的编排中枢：
///
///   识别(SenseVoice) -> 翻译(MT) -> 语音合成(GPT-SoVITS) -> 播放(NAudio)
///                                                        -> 历史记录落盘
///
/// 播放顺序保证：识别到一句话的那一刻，就立刻在 IPlaybackQueueService
/// 里占一个播放位置（见 OnResultReceived），而不是等翻译+合成都做完了
/// 才去排队——这样播放顺序永远等于说话顺序，不受各自处理耗时波动的影响，
/// 也不会出现两句话同时在播的情况（队列本身是串行消费的）。
///
/// 历史记录保存：不管这句话最终有没有成功翻译/合成语音，只要识别到了
/// 原文，就会落一条历史记录——TranslatedText/合成音频这两项是"有就存，
/// 没有就留空"，不会因为翻译或合成失败就整条记录都不保存（识别到的
/// 原文本身依然是有价值的记录）。
/// </summary>
public partial class LiveTranslationViewModel : ViewModelBase
{
    private readonly IRecognitionSessionService _recognitionSession;
    private readonly ITranslationService _translationService;
    private readonly ITtsService _ttsService;
    private readonly IPlaybackQueueService _playbackQueue;
    private readonly IHistoryRepository _historyRepository; // 新增：历史记录落盘
    private readonly ISettingsService _settingsService;

    public ObservableCollection<RecognitionResultItem> Results { get; } = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public LiveTranslationViewModel(
        IRecognitionSessionService recognitionSession,
        ITranslationService translationService,
        ITtsService ttsService,
        IPlaybackQueueService playbackQueue,
        IHistoryRepository historyRepository, // 新增参数
        ISettingsService settingsService)
    {
        _recognitionSession = recognitionSession;
        _translationService = translationService;
        _ttsService = ttsService;
        _playbackQueue = playbackQueue;
        _historyRepository = historyRepository;
        _settingsService = settingsService;

        _recognitionSession.ResultReceived += OnResultReceived;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        ErrorMessage = null;
        try
        {
            var settings = _settingsService.Current;
            // 翻译服务启动完成后紧接着调一次模型预加载——跟 TTS 那边的
            var loadTranslationTask = settings.TranslationProvider == "api"
            ? Task.CompletedTask
            : _translationService.StartAsync().ContinueWith(async _ =>
            {
                await _translationService.LoadModelAsync();
            }).Unwrap();

            // TTS 启动完成后紧接着做一次参考音频预热——链式接在 StartAsync()
            // 后面，而不是跟它并行，因为预热依赖 GPT-SoVITS 进程已经就绪
            // （子进程还没启动完就调 /set_refer_audio 只会请求失败）。
            // 这整个链条依然是跟识别会话、翻译服务的启动并行进行的，
            // 不会额外拖慢"开始翻译"按钮的响应速度。
            var shouldStartTts = settings.EnableTtsPlayback && settings.TTSProvider != "api";

            var loadTtsTask = shouldStartTts
            ? _ttsService.StartAsync().ContinueWith(async _ =>
            {
                var activeCharacterId = _settingsService.Current.ActiveCharacterId;
                if (activeCharacterId is { } characterId)
                {
                    await _ttsService.PrewarmReferenceAudioAsync(characterId);
                }
            }).Unwrap()
            : Task.CompletedTask;

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

            var playbackSlot = _playbackQueue.Enqueue();

            // e.AudioSamples/e.SampleRate 是这句话对应的原始波形，
            // 一路传下去用于历史记录落盘——识别完成的这一刻就已经拿到了，
            // 不依赖后面翻译/合成的结果。
            _ = TranslateAndSpeakAsync(
                item, e.Text, e.Language, e.Emotion, e.Event,
                e.AudioSamples, e.SampleRate, playbackSlot);
        });
    }

    private async Task TranslateAndSpeakAsync(
        RecognitionResultItem item, string text, string? language, string? emotion, string? eventTag,
        float[] audioSamples, int sampleRate, PlaybackSlot playbackSlot)
    {
        var configuredTarget = _settingsService.Current.TargetLanguage;
        var activeCharacterId = _settingsService.Current.ActiveCharacterId;

        // 这两个是最终要存进历史记录的值，随着下面的流程逐步填充——
        // 默认都是"没有"，只有真正翻译/合成成功了才会被赋值。
        string? translatedTextForHistory = null;
        byte[]? translatedAudioForHistory = null;

        if (language != "zh" && language != "en")
        {
            playbackSlot.Complete(null);
            await SaveHistoryAsync(); // 不支持的语言也要记一笔——原文本身是有价值的
            return;
        }

        string textToSpeak;

        if (language == configuredTarget)
        {
            // 已经是目标语言，不用翻译。这句"没有译文"是设计如此，
            // 不是漏翻了——item.TranslatedText 界面上依然显示原文
            // （方便用户看着顺眼），但历史记录里 TranslatedText 故意
            // 留 null，如实反映"这句没有经过翻译"这个事实。
            textToSpeak = text;
            item.TranslatedText = text;
        }
        else
        {
            try
            {
                var translationResult = await _translationService.TranslateAsync(text, language, configuredTarget);
                textToSpeak = translationResult.TranslatedText;
                item.TranslatedText = textToSpeak;
                translatedTextForHistory = textToSpeak; // 真正翻译成功了，记下来
            }
            catch (Exception ex)
            {
                item.TranslatedText = $"[翻译失败: {ex.Message}]";
                playbackSlot.Complete(null);
                await SaveHistoryAsync(); // 翻译失败也要记一笔，只是没有译文/合成音频
                return;
            }
        }

        if (_settingsService.Current.EnableTtsPlayback)
        {
            try
            {
                var ttsResult = await _ttsService.SynthesizeAsync(textToSpeak, configuredTarget, emotion ?? "NEUTRAL");
                playbackSlot.Complete(ttsResult.AudioData);
                translatedAudioForHistory = ttsResult.AudioData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TTS] 合成失败: {ex.Message}");
                playbackSlot.Complete(null);
            }
        }
        else
        {
            // 关闭了 TTS 播放——这句话的播放位置直接标记为"跳过"，
            // 不去调用合成，历史记录里这句自然也就没有合成音频。
            playbackSlot.Complete(null);
        }

        await SaveHistoryAsync();
        return;

        // 本地函数：把当前已经收集到的信息（不管翻译/合成成不成功）
        // 落一条历史记录。定义成本地函数而不是提前把参数一个个传出去，
        // 是因为它需要访问这个方法里好几个局部变量（translatedTextForHistory
        // 等），用本地函数能直接捕获外层变量，不用再单独设计一份参数列表。
        async Task SaveHistoryAsync()
        {
            var sourceAudioWav = WavEncoder.EncodeFloatSamplesToWav(audioSamples, sampleRate);

            await _historyRepository.AddAsync(
                timestamp: DateTime.Now,
                sourceText: text,
                translatedText: translatedTextForHistory,
                emotion: emotion,
                eventTag: eventTag,
                characterId: activeCharacterId,
                targetLanguage: configuredTarget,
                sourceAudioWav: sourceAudioWav,
                translatedAudioWav: translatedAudioForHistory);
        }
    }
}