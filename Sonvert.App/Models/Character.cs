using System;
using System.Collections.Generic;
using System.Linq;

namespace Sonvert.App.Models;

public class Character
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<CharacterEmotionClip> EmotionClips { get; set; } = new();

    /// <summary>阿里云声音复刻返回的 voice_id（比如
    /// "qwen-audio-3.0-tts-flash-xxx-yyyyyyyy"）。声音复刻是一次性动作
    /// （把参考音频传给阿里云的注册接口，换回一个长期有效、可以反复
    /// 使用的音色 ID），不是每次合成都重新做，所以存在角色身上，跟
    /// EmotionClips 的参考音频文件是并列的两条"音色数据"——本地引擎
    /// （GPT-SoVITS/IndexTTS/Qwen3-TTS）用 EmotionClips 里的音频文件，
    /// 阿里云这个云端引擎用这个 voice_id。目前这个字段需要手动填入
    /// （在阿里云那边注册好之后把返回的 voice_id 粘贴过来），还没有做
    /// 应用内一键注册的功能——那需要先解决"本地参考音频怎么变成阿里云
    /// 接口要求的公网可访问 URL"这个问题（比如接入 OSS 上传），目前手动
    /// 走这一步，把 URL 通过其他方式准备好、跑一次注册脚本、把结果填
    /// 进这里即可。</summary>
    public string? AliyunVoiceId { get; set; }

    /// <summary>这个角色是不是还完全不能用于合成——中文 NEUTRAL 和英文
    /// NEUTRAL 一个都没录的话，ICharacterRepository.ResolveClipAsync
    /// 对这个角色永远返回 null，选中这个角色开始翻译会直接报错。
    /// 角色列表用这个属性显示一个警告标，提醒用户"这个角色还不能真的
    /// 拿去用"，不需要真的点进去试一次才发现问题。</summary>
    public bool HasNoUsableVoice => !EmotionClips.Any(c => c.Emotion == "NEUTRAL");
}