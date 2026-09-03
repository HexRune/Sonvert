using System.Collections.Generic;

namespace Sonvert.App.Services.Characters;

/// <summary>
/// 录音向导展示的内置固定文本，按"语言+情绪"两个维度分类。用户照着这段
/// 文字念，念完保存时这段文字会作为 PromptText 的默认值——用户可以在
/// 保存前修改，这里存的只是"建议初始值"，不是强制不可改的。
///
/// 情绪集合覆盖 SenseVoice 能识别的全部 7 种情绪（NEUTRAL/HAPPY/SAD/
/// ANGRY/FEARFUL/DISGUSTED/SURPRISED），EMO_UNKNOWN 不算在内——那个
/// 是"识别不出情绪"的兜底标签，不是一个可以专门去录的情绪。
///
/// 英文脚本不是中文脚本的直译，是重新写的自然英文直播话术——同样的
/// "带货主播"语气，但不逐字对应中文那几句，直译出来的英文会显得很别扭
/// （比如"家人们"这种称呼直译成英文没有对应的自然说法）。
/// </summary>
public static class EmotionScripts
{
    public static readonly Dictionary<string, string> ChineseDefaults = new()
    {
        ["NEUTRAL"] = "今天天气真不错，阳光特别好，我想着待会儿出去散散步，顺便买杯咖啡回来慢慢喝。",
        ["HAPPY"] = "终于熬到星期五了，这一周可真是把我累得够呛，周末必须好好睡个懒觉，谁也别叫我。",
        ["SAD"] = "刚在抽屉里翻到了以前的老照片，看着看着心里就有点不是滋味，时间过得真快啊。",
        ["ANGRY"] = "说好了下午三点钟见面，这都快一个小时了，连个人影都没见着，连消息也不回一个。",
        ["FEARFUL"] = "外面天都黑透了，这条路上路灯又暗，我一个人走着说实话心里还真有点发毛。",
        ["DISGUSTED"] = "今天点的这碗面咸得要命，吃了几口实在是咽不下去了，感觉这钱花得太冤枉了。",
        ["SURPRISED"] = "不会吧，都过去这么多年了，你居然还能把这事儿记得这么清楚，我早就忘干净了。",
    };

    public static readonly Dictionary<string, string> EnglishDefaults = new()
    {
        ["NEUTRAL"] = "The weather is really nice today, the sun is shining, so I think I'll go for a walk and grab a coffee to enjoy slowly.",
        ["HAPPY"] = "I'm so glad it's finally Friday, this week has absolutely worn me out, I am definitely sleeping in this weekend.",
        ["SAD"] = "I just found some old photos in the drawer and looking at them made me feel a bit emotional, time really does fly.",
        ["ANGRY"] = "We agreed to meet at three in the afternoon and it's been almost an hour, still no sign of them and no reply.",
        ["FEARFUL"] = "It's completely dark outside and the streetlights are dim, honestly I feel a little uneasy walking here by myself.",
        ["DISGUSTED"] = "The noodles I ordered today are way too salty, I could barely eat a few bites, it just feels like such a waste of money.",
        ["SURPRISED"] = "No way, after all these years, you still remember that so clearly? I had completely forgotten about it.",
    };

    /// <summary>按语言取对应的默认脚本字典——录音向导切换语言标签页时用
    /// 这个方法，不用在调用的地方写 if/else 判断该用哪个字典。</summary>
    public static Dictionary<string, string> GetDefaults(string language) =>
        language == "en" ? EnglishDefaults : ChineseDefaults;
}
