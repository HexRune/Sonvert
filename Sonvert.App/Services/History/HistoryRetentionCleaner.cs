using System;
using System.Linq;
using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.History;

/// <summary>
/// 程序启动时跑一次：如果用户配置了历史记录保留天数，就把超过这个天数
/// 的历史记录（连同音频文件）删掉。跟 LegacySettingsMigrator 是同一种
/// "启动时跑一次性维护任务"的模式，都在 App.axaml.cs 里同步调用一次。
/// </summary>
public class HistoryRetentionCleaner
{
    private readonly ISettingsService _settingsService;
    private readonly IHistoryRepository _historyRepository;

    public HistoryRetentionCleaner(
        ISettingsService settingsService, IHistoryRepository historyRepository)
    {
        _settingsService = settingsService;
        _historyRepository = historyRepository;
    }

    public async Task RunAsync()
    {
        var retentionDays = _settingsService.Current.HistoryRetentionDays;

        // null 或 <= 0 表示用户没开启这个功能，永久保留，什么都不做。
        if (retentionDays is not > 0)
        {
            return;
        }

        var cutoffDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays.Value));
        var allDates = await _historyRepository.GetDistinctDatesAsync();

        foreach (var date in allDates.Where(d => d < cutoffDate))
        {
            await _historyRepository.DeleteDateAsync(date);
        }
    }
}