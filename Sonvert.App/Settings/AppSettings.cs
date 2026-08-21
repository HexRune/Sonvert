using System;
using System.Collections.Generic;
using System.IO;

namespace Sonvert.App.Settings;

/// <summary>
/// 程序设置。跟"历史记录"这类用户数据不同，这些是纯配置项，
/// 整体读写一份 JSON 文件即可，不需要用数据库。
/// 新增设置项直接在这里加属性，记得给一个合理的默认值——
/// 这样老用户的 settings.json 里没有这个字段时，反序列化会自动
/// 用默认值填上，不会因为多了个新字段就读取失败。
/// </summary>
public class AppSettings
{
    /// <summary>
    /// SenseVoiceService（Python 子进程）监听的本地端口。
    /// 默认值跟 Python 端 config.py 里的 DEFAULT_CONFIG 保持一致。
    /// </summary>
    public int SenseVoicePort { get; set; } = 8878;

    /// <summary>
    /// 模型精度选择："int8" 或 "fp32"。
    /// 默认 fp32——已经实测过 int8 会明显削弱 event（事件分类）的准确性，
    /// 详见 Sonvert.SenseVoiceService/README.md，所以不把 int8 作为默认值，
    /// 只作为"资源紧张时"的可选项。
    /// </summary>
    public string ModelPrecision { get; set; } = "fp32";

    // ---- SenseVoiceService 子进程怎么启动，这三项是一个整体 ----
    // 之所以设计成"可执行文件 + 参数 + 工作目录"这种通用三元组，而不是绑定死
    // "Python 解释器路径 + 脚本路径"，是因为开发期（python.exe 加 main.py 参数）
    // 和打包后（PyInstaller 产出的独立 exe，不需要参数）这两种场景下，
    // 启动命令的"形状"本身不一样——用通用三元组能同时覆盖两种场景，
    // 不需要以后打包时再改代码，只需要改这几个设置值（或者由安装程序在
    // 安装时直接写好正确的 settings.json）。

    /// <summary>
    /// 要执行的程序。开发期是 venv 里的 python.exe 完整路径；
    /// 打包后应该指向安装目录下的 SenseVoiceService.exe。
    /// 默认值是开发期的相对路径占位，指向跟 Sonvert.App 同级的
    /// Sonvert.SenseVoiceService 项目——如果你的 solution 目录结构不一样，
    /// 需要在 settings.json 里手动改成实际路径。
    /// </summary>
    public string SenseVoiceExecutablePath { get; set; } = DefaultDevExecutablePath();

    /// <summary>
    /// 启动参数。开发期是 "main.py"（python.exe 需要这个参数才知道跑哪个脚本）；
    /// 打包后是独立 exe，不需要参数，这里应该设成空字符串。
    /// </summary>
    public string SenseVoiceArguments { get; set; } = "main.py";

    /// <summary>
    /// 进程的工作目录。这个很重要，不只是影响相对路径解析——
    /// Python 端 config.py 读取 service_config.json、model_manager.py 里
    /// resource_dir="models" 这个相对路径，都是相对于"进程启动时的工作目录"
    /// 来解析的，设错了会导致模型/配置文件找不到。
    /// </summary>
    public string SenseVoiceWorkingDirectory { get; set; } = DefaultDevWorkingDirectory();

    /// <summary>
    /// Silero VAD 模型文件（.onnx）的完整路径，喂给 sherpa-onnx 的
    /// VoiceActivityDetector 用来做语音断句。这个模型文件是旧项目里
    /// 已经在用的，直接复用，不需要重新选型。
    /// 没有一个能通用的默认路径（取决于你实际把模型文件放在哪），
    /// 空字符串表示"还没配置"，RecognitionSessionService 启动前会检查
    /// 这个值，没配置会直接报错提示，而不是默默用一个大概率不对的路径。
    /// </summary>
    public string VadModelPath { get; set; } = string.Empty;

    /// <summary>
    /// 麦克风输入设备索引。-1 表示"用系统默认设备"，对应旧代码里
    /// `settings.InputDeviceIndex >= 0 ? settings.InputDeviceIndex : 0` 这个
    /// 判断逻辑，这里直接把"默认"语义放在 -1，调用的地方不用再重复判断一次。
    /// </summary>
    public int InputDeviceIndex { get; set; } = -1;

    // 这两个默认值只是"开发期能直接跑起来"的占位值，用相对路径算出来，
    // 假设 Sonvert.App 编译输出目录和 Sonvert.SenseVoiceService 源码目录
    // 保持固定的相对关系（同一个 solution 下）。等真正打包分发时，
    // 应该由安装程序在安装完成后直接写一份新的 settings.json，
    // 把这两个值改成打包产物的实际绝对路径，不依赖这里算出来的默认值。
    private static string DefaultDevWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.SenseVoiceService"));

    private static string DefaultDevExecutablePath() =>
        Path.Combine(DefaultDevWorkingDirectory(), @"env\Scripts\python.exe");

    // ---- MTService（新的 Python 子进程，翻译用）----
    public int MTPort { get; set; } = 8879;

    public string MTExecutablePath { get; set; } = DefaultDevMTExecutablePath();

    public string MTArguments { get; set; } = "main.py";

    public string MTWorkingDirectory { get; set; } = DefaultDevMTWorkingDirectory();

    // ---- 翻译提供方选择：本地模型 or 第三方 API ----
    // "local" | "api"。目前 "api" 分支还没实现（ApiTranslationService 会直接
    // 抛异常），先占好设置位，等接入时不需要再改设置结构。
    public string TranslationProvider { get; set; } = "local";

    /// <summary>第三方翻译/大模型 API 的请求地址，预留，暂未使用。</summary>
    public string TranslationApiEndpoint { get; set; } = string.Empty;

    /// <summary>第三方 API 的密钥，预留，暂未使用。</summary>
    public string TranslationApiKey { get; set; } = string.Empty;

    /// <summary>第三方 API 用的模型名（比如 "gpt-4o-mini"、"qwen-mt-turbo"），预留，暂未使用。</summary>
    public string TranslationApiModel { get; set; } = string.Empty;

    private static string DefaultDevMTWorkingDirectory() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\Sonvert.MTService"));

    private static string DefaultDevMTExecutablePath() =>
        Path.Combine(DefaultDevMTWorkingDirectory(), @"env\Scripts\python.exe");

    // ---- TTSService（GPT-SoVITS 官方 api_v2.py，语音合成用）----

    /// <summary>
    /// GPT-SoVITS api_v2.py 监听的本地端口，默认值跟它自己的默认端口一致。
    /// </summary>
    public int TTSPort { get; set; } = 9880;

    /// <summary>
    /// 要执行的程序：开发期指向 GPT-SoVITS 整合包里的 runtime\python.exe。
    /// 跟 SenseVoice/MT 那两个不一样——这个不是我们自己的 venv，是官方
    /// 整合包自带的独立运行时，路径需要指到那个整合包的解压目录里。
    /// </summary>
    public string TTSExecutablePath { get; set; } = "D:/code/Sonvert/GPT-SoVITS-v3lora-20250228/runtime/python.exe";

    public string TTSArguments { get; set; } =
        "api_v2.py -a 127.0.0.1 -p 9880 -c GPT_SoVITS/configs/tts_infer.yaml";

    /// <summary>GPT-SoVITS 整合包的根目录（解压后的那个文件夹）。</summary>
    public string TTSWorkingDirectory { get; set; } = "D:/code/Sonvert/GPT-SoVITS-v3lora-20250228";

    /// <summary>
    /// 按情绪分类的参考音频。key 是情绪标签，直接复用 SenseVoice 输出的
    /// 那套（NEUTRAL/HAPPY/ANGRY/SAD 等）。NEUTRAL 这一项必须配置，
    /// 找不到对应情绪的参考音频时会退回用它兜底。
    /// </summary>
    public Dictionary<string, TtsReferenceClip> TTSReferenceAudioByEmotion { get; set; } = new()
    {
        ["NEUTRAL"] = new TtsReferenceClip(),
    };

    public class TtsReferenceClip
    {
        /// <summary>参考音频文件路径，必须是干净录音——不能是剪辑/转码过的素材，
        /// 之前实测过背景音乐/混响会导致合成内容错乱，参见 Sonvert.TTSService 排查记录。</summary>
        public string AudioPath { get; set; } = "D:\\code\\Sonvert\\ReferenceAudio.wav";

        /// <summary>参考音频里实际说的内容，逐字对应。</summary>
        public string PromptText { get; set; } = "来，家人们看过来！今天这款产品我敢说，你错过了一定会后悔。";
    }

    public string TTSReferenceAudioLanguage { get; set; } = "zh";

    // ---- TTS 提供方选择：本地模型 or 第三方 API，跟 MT 那边设计对称 ----
    public string TTSProvider { get; set; } = "local"; // "local" | "api"
    public string TTSApiEndpoint { get; set; } = string.Empty;
    public string TTSApiKey { get; set; } = string.Empty;
    public string TTSApiModel { get; set; } = string.Empty;
}