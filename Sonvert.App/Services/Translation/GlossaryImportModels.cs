using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 词典导入用的 JSON 结构——字段名用 "name"/"entries"/"source"/"target"
/// 这种简单小写单词，不是照抄 GlossaryDictionary/GlossaryEntry 的 C#
/// 属性名（PascalCase），因为这份 JSON 是给"人手写"或者"从别处导出"的
/// 场景准备的，格式要尽量直白，不需要用户知道这个项目内部的类型命名
/// 习惯。示例：
/// {
///   "name": "英雄联盟",
///   "entries": [
///     { "source": "ADC", "target": "ADC" },
///     { "source": "打野", "target": "Jungler" }
///   ]
/// }
/// "name" 是可选的——如果用户导入时已经在界面上填了词典名字，或者只是
/// 想往一份"没有名字、纯词条数组"的文件里塞条目，也能正常工作，见
/// GlossaryRepository.ImportFromJsonAsync 里 nameOverride 参数的处理。
/// </summary>
public class GlossaryImportFile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("entries")]
    public List<GlossaryImportEntry> Entries { get; set; } = new();
}

public class GlossaryImportEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;
}
