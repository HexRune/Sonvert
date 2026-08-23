using System.Collections.Generic;
using System.Threading.Tasks;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Characters;

/// <summary>解析出的合成用参考素材——音频路径 + 对应文本，两者是一对，
/// 缺一不可，所以合并成一个返回值，避免调用方分两次查询导致数据不一致。</summary>
public class ResolvedTtsClip
{
    public required string AudioPath { get; init; }
    public required string PromptText { get; init; }
}

public interface ICharacterRepository
{
    Task<List<Character>> GetAllAsync();
    Task<Character?> GetByIdAsync(int id);

    Task<Character> CreateAsync(string name);
    Task DeleteAsync(int id);

    /// <summary>保存/覆盖某个角色某种情绪的录音。promptText 传录音对应的文字——
    /// 录音向导场景传固定脚本文字（可能已被用户编辑过），导入已有音频的场景
    /// 传用户手动填写的文字。</summary>
    Task SaveEmotionClipAsync(int characterId, string emotion, byte[] audioData, string promptText);

    /// <summary>按情绪查找参考音频+文本，查不到该情绪就退回同一角色的 NEUTRAL；
    /// 如果连 NEUTRAL 都没有，返回 null（调用方决定怎么处理，比如禁止在没配置
    /// NEUTRAL 的情况下开始翻译）。</summary>
    Task<ResolvedTtsClip?> ResolveClipAsync(int characterId, string emotion);
}