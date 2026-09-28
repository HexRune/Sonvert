using System.Collections.Generic;
using System.Threading.Tasks;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Translation;

public interface IGlossaryRepository
{
    /// <summary>翻译流程实际使用的入口——返回指定词典下的全部词条，
    /// 已经拍平成一份 List&lt;GlossaryEntry&gt;，可以直接原样传给
    /// GlossaryReplacer.Replace。dictionaryId 为 null 时（对应首页
    /// "选择词典"下拉框里的"不使用词典"）直接返回空列表，不查数据库——
    /// 调用方（各个 TranslationService）传的就是
    /// AppSettings.ActiveGlossaryDictionaryId，"要不要用词典、用哪个"
    /// 这个决定权在设置这一层，仓储层只负责"给了 Id 就查，没给就返回
    /// 空"，不掺和判断逻辑。</summary>
    Task<List<GlossaryEntry>> GetEntriesForDictionaryAsync(int? dictionaryId);

    /// <summary>管理界面用——列出所有词典，每个词典的 Entries 已经一并
    /// 加载好（Include），界面不需要再逐个词典单独查一次词条。</summary>
    Task<List<GlossaryDictionary>> GetDictionariesAsync();

    Task<GlossaryDictionary> AddDictionaryAsync(string name);

    /// <summary>删词典会级联删掉它名下的所有词条——级联规则配置在
    /// AppDbContext.OnModelCreating，这里不需要手动先删词条再删词典。
    /// 如果删掉的正好是当前"选择词典"下拉框选中的那一个，调用方
    /// （GlossaryViewModel）负责把 AppSettings.ActiveGlossaryDictionaryId
    /// 也一并清空，这里只管数据库这一侧。</summary>
    Task DeleteDictionaryAsync(int dictionaryId);

    Task AddEntryAsync(int dictionaryId, string sourceTerm, string targetTerm);

    Task DeleteEntryAsync(int entryId);

    /// <summary>从 JSON 文本导入一个新词典（连同它的全部词条）。
    /// nameOverride 优先于 JSON 里的 "name" 字段——界面上如果用户自己
    /// 填了词典名字，就不用 JSON 里那个（或者 JSON 里根本没有 name 字段
    /// 的情况）。返回新建的 GlossaryDictionary（Entries 已加载），
    /// 方便调用方直接展示，不用再查一次。</summary>
    Task<GlossaryDictionary> ImportFromJsonAsync(string json, string? nameOverride);
}
