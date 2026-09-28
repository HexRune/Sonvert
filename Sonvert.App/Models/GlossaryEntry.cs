namespace Sonvert.App.Models;

/// <summary>
/// 用户自定义的术语翻译表。SourceTerm 是识别到的原文里的词/短语，
/// TargetTerm 是想要在译文里固定出现的内容——实现方式不是"翻译后
/// 再替换译文"，而是"翻译前就把 SourceTerm 替换成 TargetTerm，
/// 让 MT 模型直接把它当成一段已经是目标语言的文字原样保留"。
/// 这个方式比生成随机占位符更可靠，因为模型训练数据里本来就有大量
/// "中文句子夹杂英文专有名词"这种真实语料，处理这类混合输入是它
/// 见过的模式，不是新东西。
///
/// DictionaryId：每条术语现在必须归属一个 GlossaryDictionary——
/// 术语表从"一个扁平全局列表"升级成"多个可以分别开关的词典"之后，
/// 这是唯一实质加的字段，替换逻辑本身（GlossaryReplacer.Replace）
/// 完全没变，它拿到手的还是一份扁平的 List&lt;GlossaryEntry&gt;，
/// "哪些词条参与"这一层过滤是在 GlossaryRepository 查询的时候按
/// Dictionary.IsEnabled 做的，不是这个类或者替换逻辑要关心的事。
/// </summary>
public class GlossaryEntry
{
    public int Id { get; set; }
    public required string SourceTerm { get; set; }
    public required string TargetTerm { get; set; }

    public int DictionaryId { get; set; }
    public GlossaryDictionary? Dictionary { get; set; }
}
