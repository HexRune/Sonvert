using System;
using System.Collections.Generic;
using System.IO;

namespace Sonvert.App.Settings;

/// <summary>
/// 程序设置。跟"历史记录"这类用户数据不同，这些是纯配置项，
/// 整体读写一份 JSON 文件即可，不需要用数据库。角色/情绪录音相关的数据
/// 已经挪到 SQLite 数据库里（见 Data/AppDbContext.cs），这里只保留
/// "当前选中哪个角色"这个引用（ActiveCharacterId）。
/// </summary>
public class AppSettings
{
    /// <summary>历史记录自动清理的保留天数——null 或 0 表示不开启这个功能，
    /// 永久保留所有历史记录。程序启动时由 HistoryRetentionCleaner 读取
    /// 这个值，删掉超期的记录。</summary>
    public int? HistoryRetentionDays { get; set; } = null;

    // ---- SenseVoiceService ----
    public int SenseVoicePort { get; set; } = 8878;

    public string ModelPrecision { get; set; } = "fp32";

    public string SenseVoiceExecutablePath { get; set; } = DefaultDevExecutablePath();
    public string SenseVoiceArguments { get; set; } = "main.py";
    public string SenseVoiceWorkingDirectory { get; set; } = DefaultDevWorkingDirectory();

    public string VadModelPath { get; set; } = string.Empty;
    public int InputDeviceIndex { get; set; } = -1;
    
    /// <summary>语音识别的语言模式："auto"（自动检测，可能偶发把中文误判成
    /// 日语/粤语等）、"zh"（强制按中文解码）、"en"（强制按英文解码）。
    /// 直播场景下主播整场基本只用一种语言，建议强制指定，避免 auto 模式
    /// 偶发误判。</summary>
    public string RecognitionLanguage { get; set; } = "auto";

    private static string DefaultDevWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.SenseVoiceService"));

    private static string DefaultDevExecutablePath() =>
        Path.Combine(DefaultDevWorkingDirectory(), @"env\Scripts\python.exe");

    // ---- MTService ----
    /// <summary>
    /// 目标语言——识别出的语言如果不是这个值，就翻译成这个值；
    /// 识别出的语言如果正好是这个值，翻译成另一种（目前只支持 zh<->en
    /// 这一对，以后要支持更多语言时，这里的判断逻辑要跟着扩展）。
    /// </summary>
    public string TargetLanguage { get; set; } = "en";
    public int MTPort { get; set; } = 8879;
    public string MTExecutablePath { get; set; } = DefaultDevMTExecutablePath();
    public string MTArguments { get; set; } = "main.py";
    public string MTWorkingDirectory { get; set; } = DefaultDevMTWorkingDirectory();

    public string TranslationProvider { get; set; } = "local";
    public string TranslationApiEndpoint { get; set; } = string.Empty;
    public string TranslationApiKey { get; set; } = string.Empty;
    public string TranslationApiModel { get; set; } = string.Empty;

    private static string DefaultDevMTWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.MTService"));

    private static string DefaultDevMTExecutablePath() =>
        Path.Combine(DefaultDevMTWorkingDirectory(), @"env\Scripts\python.exe");

    // ---- TTSService ----
    public int TTSPort { get; set; } = 9880;
    public string TTSExecutablePath { get; set; } = string.Empty;
    public string TTSArguments { get; set; } =
        "api_v2.py -a 127.0.0.1 -p 9880 -c GPT_SoVITS/configs/tts_infer.yaml";
    public string TTSWorkingDirectory { get; set; } = string.Empty;
    public string TTSReferenceAudioLanguage { get; set; } = "zh";

    public string TTSProvider { get; set; } = "local";
    public string TTSApiEndpoint { get; set; } = string.Empty;
    public string TTSApiKey { get; set; } = string.Empty;
    public string TTSApiModel { get; set; } = string.Empty;

    // ---- 角色（声音克隆）----

    /// <summary>当前选中的角色 Id。null 表示还没选任何角色（首次使用、
    /// 还没建过角色），这种情况下不允许开始翻译，UI 层要给出提示。</summary>
    public int? ActiveCharacterId { get; set; }

    /// <summary>
    /// 【迁移专用】旧版本里全局的情绪参考音频配置。程序启动时如果发现
    /// 这里有值、且数据库里还没有任何角色，会自动创建一个"默认角色"
    /// 并把这份配置迁移过去，迁移完成后 LegacyTtsReferenceAudioMigrated
    /// 会被设为 true，不会重复迁移。迁移逻辑稳定运行几个版本之后，
    /// 这个字段和下面的 TtsReferenceClip 类可以整个删除。
    /// </summary>
    public Dictionary<string, TtsReferenceClip> TTSReferenceAudioByEmotion { get; set; } = new();

    public bool LegacyTtsReferenceAudioMigrated { get; set; } = false;

    public class TtsReferenceClip
    {
        public string AudioPath { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
    }
}