namespace Sonvert.App.Models;

/// <summary>
/// 用户自定义的术语翻译表。SourceTerm 是识别到的原文里的词/短语，
/// TargetTerm 是想要在译文里固定出现的内容——实现方式不是"翻译后
/// 再替换译文"，而是"翻译前就把 SourceTerm 替换成 TargetTerm，
/// 让 MT 模型直接把它当成一段已经是目标语言的文字原样保留"。
/// 这个方式比生成随机占位符更可靠，因为模型训练数据里本来就有大量
/// "中文句子夹杂英文专有名词"这种真实语料，处理这类混合输入是它
/// 见过的模式，不是新东西。
/// </summary>
public class GlossaryEntry
{
    public int Id { get; set; }
    public required string SourceTerm { get; set; }
    public required string TargetTerm { get; set; }
}