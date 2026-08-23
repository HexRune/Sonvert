using System.Collections.Generic;

namespace Sonvert.App.Services.Characters;

/// <summary>
/// 录音向导展示的内置固定文本，按情绪分类。用户照着这段文字念，
/// 念完保存时这段文字会作为 PromptText 的默认值——用户可以在
/// 保存前修改，这里存的只是"建议初始值"，不是强制不可改的。
/// </summary>
public static class EmotionScripts
{
    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["NEUTRAL"] = "来，家人们看过来，今天这款产品的功能和参数我详细介绍一下。",
        ["HAPPY"] = "哇，这个也太好看了吧，家人们，这个价格真的是史低了！",
        ["ANGRY"] = "我真的不能忍，这个质量问题厂家必须给个说法！",
        ["SURPRISED"] = "啊？真的假的，这个价格我没看错吧，福利也太大了！",
    };
}