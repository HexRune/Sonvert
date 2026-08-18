using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sonvert.App.Settings;

public interface ISettingsService
{
    /// <summary>
    /// 当前生效的设置。程序启动时已经从磁盘加载好，其他服务直接读这个属性
    /// 就能拿到最新值（比如 SenseVoiceService 启动子进程前读 SenseVoicePort），
    /// 不需要每次都手动调用加载方法。
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// 把 Current 当前的值写回磁盘。设置界面改完之后调用这个方法保存。
    /// 调用方式：先改 settingsService.Current 上的属性，再调用 SaveAsync()。
    /// </summary>
    Task SaveAsync();
}

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;

    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        // 存在用户的 AppData\Roaming 目录下，不跟程序安装目录混在一起——
        // 这样即使用户重新安装/更新程序（覆盖安装目录），设置也不会丢。
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sonvert");
        Directory.CreateDirectory(appDataDir);
        _settingsFilePath = Path.Combine(appDataDir, "settings.json");

        Current = LoadFromDisk();
    }

    private AppSettings LoadFromDisk()
    {
        if (!File.Exists(_settingsFilePath))
        {
            // 第一次运行，文件还不存在，用默认值，并立刻落盘一份，
            // 这样下次能在磁盘上直接看到这个文件，方便用户手动查看/编辑。
            var defaults = new AppSettings();
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            // 反序列化失败（文件损坏/手动改错了格式）时返回 null，
            // 这里做兜底，不让程序直接崩在启动阶段。
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // settings.json 内容不是合法 JSON，同样兜底成默认值，
            // 不覆盖原文件——保留现场方便用户或者以后调试时查看到底哪里写坏了。
            return new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, // 缩进格式化，方便用户需要时手动打开文件查看/编辑
    };
}
