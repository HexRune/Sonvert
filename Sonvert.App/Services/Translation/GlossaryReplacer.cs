using System.Collections.Generic;
using System.Linq;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 术语替换：翻译前直接把配置的 SourceTerm 换成 TargetTerm，让 MT 模型
/// 把它当成"句子里已经是目标语言的一部分"去处理——实测确认 OPUS-MT
/// 能正确原样保留这类嵌入英文，不需要占位符那套"替换->翻译->再换回"
/// 的三段式，直接替换完丢给模型就行。
/// </summary>
public static class GlossaryReplacer
{
    public static string Replace(string text, List<GlossaryEntry> glossary)
    {
        var result = text;

        // 按术语长度从长到短替换，避免短术语是长术语子串时
        // （比如同时配置了"iPhone"和"iPhone17"），被提前替换掉一部分
        // 导致长术语匹配不上。
        foreach (var entry in glossary.OrderByDescending(e => e.SourceTerm.Length))
        {
            result = result.Replace(entry.SourceTerm, entry.TargetTerm);
        }

        return result;
    }
}