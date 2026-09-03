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

    /// <summary>这个角色是不是还完全不能用于合成——中文 NEUTRAL 和英文
    /// NEUTRAL 一个都没录的话，ICharacterRepository.ResolveClipAsync
    /// 对这个角色永远返回 null，选中这个角色开始翻译会直接报错。
    /// 角色列表用这个属性显示一个警告标，提醒用户"这个角色还不能真的
    /// 拿去用"，不需要真的点进去试一次才发现问题。</summary>
    public bool HasNoUsableVoice => !EmotionClips.Any(c => c.Emotion == "NEUTRAL");
}