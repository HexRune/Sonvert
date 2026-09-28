using System;
using System.Collections.Generic;

namespace Sonvert.App.Models;

/// <summary>
/// 一个词典——术语表功能从"一个扁平的全局列表"升级成"多个词典各自
/// 管理"之后新加的分组单位。典型用法：一个词典对应一款游戏或者一个
/// 场景（"英雄联盟"、"通用缩写"），互不干扰，用户在"词典管理"页面
/// 建好各个场景的词典，实际翻译时在首页"选择词典"下拉框里挑一个当次
/// 要用的（单选，不是多选叠加）——选中哪个记在
/// AppSettings.ActiveGlossaryDictionaryId 里，不是这个类自己的字段
/// 在管。IsEnabled 字段是设计迭代中留下的历史遗留，见它自己的注释。
///
/// Entries 是这个词典下的所有词条，一对多，级联删除（删词典的时候，
/// 它名下的词条没有单独存在的意义，一起删掉，不留孤儿数据）——具体的
/// 级联配置在 AppDbContext.OnModelCreating 里，跟 Character/
/// CharacterEmotionClip 那对关系是完全一样的处理方式。
/// </summary>
public class GlossaryDictionary
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>历史遗留字段——最初的设计是"全局开关+每个词典自己的
    /// 开关"这种多选模式，用户反馈之后简化成了首页"选择词典"下拉框
    /// 单选一个（不使用/词典A/词典B...），选中哪个由
    /// AppSettings.ActiveGlossaryDictionaryId 决定，不再靠这个字段过滤。
    /// 保留这个属性（而不是删掉）是因为它已经是数据库里的一列，删掉
    /// 属性但不删列会导致 EF Core 插入新词典时漏给这一列赋值，
    /// SQLite 那边这一列又是 NOT NULL，会直接报错——保留字段、只是不再
    /// 被任何查询/界面用到，是眼下最省事也最不容易出错的处理方式，
    /// 真要彻底去掉得再写一次迁移删除这一列，目前没这个必要。</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<GlossaryEntry> Entries { get; set; } = new();
}
