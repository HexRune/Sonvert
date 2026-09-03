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

    /// <summary>给"还没有开始翻译"这条提示的显示条件用。不能只看
    /// IsRunning——StartCommand（[RelayCommand] 生成的 AsyncRelayCommand）
    /// 自带一个 IsRunning 属性，在 StartAsync 方法执行期间为 true；而
    /// 我们自己这个 LiveTranslationViewModel.IsRunning 要等识别会话、
    /// 翻译服务、语音合成全部加载启动完才会变 true，中间有一段"点了
    /// 开始翻译、页面已经跳转过来了，但服务还在加载"的空窗期。
    /// 如果提示只看 this.IsRunning，这段空窗期里会误显示"还没有开始
    /// 翻译"，看起来像点击没反应。IsIdle 把两个条件都考虑进去，只有
    /// 真正"没在运行、也没在启动中"才是 true。</summary>
    public bool IsIdle => !IsRunning && !StartCommand.IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
    }

    /// <summary>最近一批麦克风采样的电平值 [0,1]，来自
    /// IRecognitionSessionService.LevelChanged。顶部横幅栏的电平指示
    /// 用的就是这个属性驱动的分段格子（见下面 LevelSegments），
    /// 不是滚动历史队列，所以不需要担心"连续静音时 PropertyChanged
    /// 被去重跳过"这个问题——格子亮不亮是这个值的纯函数，静音时该灭的
    /// 格子已经是灭的，跳过几次没有实际变化的通知不影响最终显示效果。</summary>
    [ObservableProperty]
    private double _audioLevel;

    // ---- 电平指示：一排固定数量的 LED 格子，位置固定配色 ----
    // 跟首页麦克风旁边的迷你电平表（HomeViewModel 里的 IsMicSegmentNOn）
    // 是同一个设计思路，但格数从 5 格/10 格进一步加到了 LevelSegmentCount
    // 这么多——格数一多，再给每一格单独写一个 IsLevelSegmentNOn 具名属性
    // 就太啰嗦了（几十个属性、XAML 里几十行重复的 Rectangle），所以改成
    // 一个 ObservableCollection<IBrush>，每一项就是"这一格现在应该填什么
    // 颜色"——没点亮时是 IdleBrush（暗灰），点亮时按这一格所在的区域
    // （绿/黄/红）填对应颜色。XAML 那边用 ItemsControl 遍历这个集合，
    // 每一项直接绑 Fill="{Binding}"（绑定路径是空的，因为集合里每一项
    // 本身就是要显示的 Brush，不是一个还要再取子属性的对象），不需要为了
    // "多加几个格子"就去改 XAML 或者 ViewModel 里的具名属性列表——改
    // LevelSegmentCount 这一个常量就行，格子数量和阈值分布都是根据它
    // 自动算出来的。
    private const int LevelSegmentCount = 200;

    // 三个区域的分界比例：前 60% 是绿色（安全音量），中间 20% 黄色
    // （音量偏大），最后 20% 红色（接近爆音）——比例上跟之前 10 格版本
    // （6:2:2）保持一致，只是格数按这个比例重新分配。
    private const double GreenZoneRatio = 0.60;
    private const double AmberZoneRatio = 0.20;
    // 剩下的 1 - GreenZoneRatio - AmberZoneRatio 是红色区，不用再单独定义。

    // 颜色跟 Styles/Colors.axaml 里对应的资源数值必须保持一致——这里
    // 不直接从 XAML 资源字典查找的原因，跟 MainViewModel 里那三个颜色
    // 常量的注释是同一个（避免从 ViewModel 反向依赖 XAML 资源、以及
    // 项目命名空间和 Avalonia Application 入口类同名 "App" 可能引起的
    // 引用歧义）。
    private static readonly Avalonia.Media.IBrush IdleBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2A2A"));
    private static readonly Avalonia.Media.IBrush GreenBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4ADE80"));
    private static readonly Avalonia.Media.IBrush AmberBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F0A030"));
    private static readonly Avalonia.Media.IBrush RedBrush =
        new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E64545"));

    public ObservableCollection<Avalonia.Media.IBrush> LevelSegments { get; } = new();

    /// <summary>按 LevelSegmentCount 生成每一格的阈值和"点亮时该显示
    /// 什么颜色"，在构造函数里调一次，把 LevelSegments 填满初始的熄灭色。
    /// 阈值是均匀分布在 (0, 0.95] 区间——留到 0.95 而不是 1.0，是保证
    /// 音量到达可实现的最大值时最后一格也真的能点亮（如果切到刚好等于
    /// 1.0，考虑到浮点数误差，可能永远点不亮最后一格）。</summary>
    private void InitializeLevelSegments()
    {
        for (var i = 0; i < LevelSegmentCount; i++)
        {
            LevelSegments.Add(IdleBrush);
        }
    }

    /// <summary>AudioLevel 变化时刷新每一格的显示颜色。用索引赋值
    /// （LevelSegments[i] = ...）而不是 Clear()+重新 Add()：索引赋值只会
    /// 通知"这一项被替换了"，ItemsControl 只重绘对应的那一个格子；
    /// Clear+Add 会让整个列表先清空再重建，等于每次电平变化都把 N 个
    /// 格子全部重新创建一遍可视元素，没必要的开销。</summary>
    private void RefreshLevelSegments()
    {
        var greenCount = (int)(LevelSegmentCount * GreenZoneRatio);
        var amberCount = (int)(LevelSegmentCount * AmberZoneRatio);

        for (var i = 0; i < LevelSegmentCount; i++)
        {
            var threshold = (i + 1) * 0.95 / LevelSegmentCount;
            var isActive = AudioLevel > threshold;

            if (!isActive)
            {
                LevelSegments[i] = IdleBrush;
                continue;
            }

            LevelSegments[i] = i < greenCount ? GreenBrush
                : i < greenCount + amberCount ? AmberBrush
                : RedBrush;
        }
    }

    partial void OnAudioLevelChanged(double value)
    {
        RefreshLevelSegments();
    }

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
        _recognitionSession.LevelChanged += OnLevelChanged;

        InitializeLevelSegments();

        // StartCommand.IsRunning（AsyncRelayCommand 自带的、"这个命令
        // 当前是否正在执行"的属性）变化时，让 IsIdle 也跟着刷新一次
        // 通知——不这么做的话，点击开始翻译后 IsIdle 只会在 this.IsRunning
        // 变化（也就是全部服务加载完）时才重新计算，界面上"还没有开始
        // 翻译"那条提示会一直显示到加载完成才消失，跟本来想解决的问题
        // 是同一个空窗期。
        StartCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        };
    }

    /// <summary>LevelChanged 跟 ResultReceived 一样是在后台采集线程上
    /// 触发的，直接赋值给 [ObservableProperty] 会导致绑定通知从非 UI
    /// 线程发出，Avalonia 的 UI 更新必须切回 UI 线程，用法上跟
    /// OnResultReceived 完全一致。</summary>
    private void OnLevelChanged(object? sender, double level)
    {
        Dispatcher.UIThread.Post(() => AudioLevel = level);
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
        AudioLevel = 0; // 停止后电平表清零，不留着停止前最后一帧的数值
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
                AsrLatencyMs = e.AsrLatencyMs,
            };
            Results.Insert(0, item);

            var playbackSlot = _playbackQueue.Enqueue();

            // e.AudioSamples/e.SampleRate 是这句话对应的原始波形，
            // 一路传下去用于历史记录落盘——识别完成的这一刻就已经拿到了，
            // 不依赖后面翻译/合成的结果。
            _ = TranslateAndSpeakAsync(
                item, e.Text, e.Language, e.Emotion, e.Event,
                e.AudioSamples, e.SampleRate, e.AsrLatencyMs, playbackSlot);
        });
    }

    private async Task TranslateAndSpeakAsync(
        RecognitionResultItem item, string text, string? language, string? emotion, string? eventTag,
        float[] audioSamples, int sampleRate, int asrLatencyMs, PlaybackSlot playbackSlot)
    {
        var configuredTarget = _settingsService.Current.TargetLanguage;
        var activeCharacterId = _settingsService.Current.ActiveCharacterId;

        // 这两个是最终要存进历史记录的值，随着下面的流程逐步填充——
        // 默认都是"没有"，只有真正翻译/合成成功了才会被赋值。
        string? translatedTextForHistory = null;
        byte[]? translatedAudioForHistory = null;

        // 翻译/合成各自的耗时——null 表示这一步压根没跑（不需要翻译、
        // TTS 播放关闭），跟 HistoryEntry 里对应字段的语义完全一致。
        int? translationLatencyMs = null;
        int? ttsLatencyMs = null;

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
            // 留 null，如实反映"这句没有经过翻译"这个事实。translationLatencyMs
            // 同理留 null，不是"耗时 0ms"，是这一步根本没有发生。
            textToSpeak = text;
            item.TranslatedText = text;
        }
        else
        {
            var translationStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
                // 失败也要记这段耗时——"翻译调用卡了多久才失败"本身是有
                // 诊断价值的信息，不应该因为失败就丢弃这段耗时数据。
                translationLatencyMs = (int)translationStopwatch.ElapsedMilliseconds;
                item.TranslationLatencyMs = translationLatencyMs;
                await SaveHistoryAsync(); // 翻译失败也要记一笔，只是没有译文/合成音频
                return;
            }
            finally
            {
                translationStopwatch.Stop();
            }

            translationLatencyMs = (int)translationStopwatch.ElapsedMilliseconds;
            item.TranslationLatencyMs = translationLatencyMs;
        }

        if (_settingsService.Current.EnableTtsPlayback)
        {
            var ttsStopwatch = System.Diagnostics.Stopwatch.StartNew();
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
            finally
            {
                // 不管成功失败都记这段耗时，理由跟上面翻译失败那里一样。
                ttsStopwatch.Stop();
                ttsLatencyMs = (int)ttsStopwatch.ElapsedMilliseconds;
                item.TtsLatencyMs = ttsLatencyMs;
            }
        }
        else
        {
            // 关闭了 TTS 播放——这句话的播放位置直接标记为"跳过"，
            // 不去调用合成，历史记录里这句自然也就没有合成音频，
            // ttsLatencyMs 也保持 null（这一步压根没跑，不是耗时 0）。
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
                translatedAudioWav: translatedAudioForHistory,
                asrLatencyMs: asrLatencyMs,
                translationLatencyMs: translationLatencyMs,
                ttsLatencyMs: ttsLatencyMs);
        }
    }
}