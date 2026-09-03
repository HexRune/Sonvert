using System.Collections.Generic;
using System.Threading.Tasks;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Characters;

/// <summary>解析出的合成用参考素材——音频路径 + 对应文本 + 实际用的语言，
/// 三者是一体的，所以合并成一个返回值，避免调用方分两次查询导致数据
/// 不一致。Language 是"实际解析出来用的是哪种语言的参考音频"，不一定
/// 等于调用方传进去的目标语言——比如角色只录了英文，读中文时也会兜底
/// 用英文参考音频，这时候 Language 返回 "en"，调用方（LocalTtsService）
/// 拿这个值去告诉 GPT-SoVITS"这段参考音频的文字是什么语言"，不能沿用
/// 目标合成语言，两者这种情况下是不一致的。</summary>
public class ResolvedTtsClip
{
    public required string AudioPath { get; init; }
    public required string PromptText { get; init; }
    public required string Language { get; init; }
}

public interface ICharacterRepository
{
    Task<List<Character>> GetAllAsync();
    Task<Character?> GetByIdAsync(int id);

    Task<Character> CreateAsync(string name);
    Task DeleteAsync(int id);

    /// <summary>保存/覆盖某个角色"某种语言+某种情绪"的录音。promptText 传
    /// 录音对应的文字——录音向导场景传固定脚本文字（可能已被用户编辑过），
    /// 导入已有音频的场景传用户手动填写的文字。
    /// language 只能是 "zh" 或 "en"。</summary>
    Task SaveEmotionClipAsync(int characterId, string emotion, string language, byte[] audioData, string promptText);

    /// <summary>
    /// 按"目标语言+情绪"解析出该用哪条参考音频，两层兜底：
    ///
    /// 第一层——选语言：一个角色的中文 NEUTRAL 和英文 NEUTRAL 至少要有
    /// 一个存在，角色才"可用"（在录音向导里，没先录某个语言的 NEUTRAL，
    /// 那个语言下的其他情绪是锁住的，不可能出现"有情绪没NEUTRAL"这种
    /// 中间状态）。如果目标语言对应的 NEUTRAL 存在，就用目标语言；
    /// 如果不存在（角色只录了另一种语言），就整体改用角色唯一支持的
    /// 那种语言——不管合成任务要读的是中文还是英文，统统用这一种语言
    /// 的参考音频顶上。
    ///
    /// 第二层——选情绪：语言定下来之后，如果这个语言下正好录了目标情绪
    /// 的参考音频就直接用；没录就退回同一语言下的 NEUTRAL（一定存在，
    /// 因为第一层已经保证了）。
    ///
    /// 如果两种语言的 NEUTRAL 都不存在（角色还没法用），返回 null，
    /// 调用方决定怎么处理（比如禁止在没配置任何 NEUTRAL 的情况下开始
    /// 翻译）。</summary>
    Task<ResolvedTtsClip?> ResolveClipAsync(int characterId, string emotion, string targetLanguage);
}
