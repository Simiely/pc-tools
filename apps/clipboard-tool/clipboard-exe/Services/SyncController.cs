// Services/SyncController.cs - WebDAV 同步编排控制器（M5c 抽离：把同步状态/自动同步从 MainWindow 下沉到服务层）
// 职责：持有同步配置 + 定时自动同步 tick（仅调用 SyncEngine/WebDavSync，不碰 UI——对话框由 MainWindow 打开）。
// 对齐审计建议：UI 层只编排，业务/编排逻辑下沉服务层（Separation of Concerns）。
using System;
using System.Threading.Tasks;
using ClipboardExe.Controls;
using ClipboardExe.Models;

namespace ClipboardExe.Services;

public sealed class SyncController
{
    private readonly Storage _storage;
    private readonly FileStore _fileStore;
    private readonly string _dataDir;

    private SyncConfig? _config;

    /// <summary>当前同步配置（null = 未配置）。配置变更（保存/同步成功/定时刷新）后触发 ConfigChanged，
    /// 由订阅方（MainWindow：刷新顶栏账号徽章）响应。服务层自身不碰 UI，仅发通知。</summary>
    public SyncConfig? Config
    {
        get => _config;
        private set
        {
            if (ReferenceEquals(_config, value)) return;
            _config = value;
            ConfigChanged?.Invoke();
        }
    }

    /// <summary>配置变化通知（切账号/首次绑定/同步后重载均触发）。UI 订阅方须在 UI 线程响应（触发点都在 UI 上下文）。</summary>
    public event Action? ConfigChanged;

    public SyncController(Storage storage, FileStore fileStore, string dataDir)
    {
        _storage = storage;
        _fileStore = fileStore;
        _dataDir = dataDir;
        Config = WebDavSync.LoadConfig(dataDir);
    }

    /// <summary>配置目录（供对话框把配置持久化到同一 webdav.json）。</summary>
    public string DataDir => _dataDir;

    /// <summary>刷新配置（保存/同步成功后调用，或自动同步收尾时从磁盘重载）。</summary>
    public void SetConfig(SyncConfig? cfg) => Config = cfg;

    /// <summary>定时自动同步 tick（对齐 runAutoSync：1 分钟轮询，到点才跑；失败静默——引擎已记 lastSyncError）。</summary>
    public async Task Tick()
    {
        if (Config == null || !Config.AutoSync || !SyncEngine.IsDue(Config)) return;
        try
        {
            var r = await Task.Run(() => SyncEngine.RunSync(_storage, _fileStore, Config, _dataDir));
            if (r.Ok) ToastService.Flash("已自动同步");
        }
        catch { /* 引擎内部已吞异常并返回 Error，这里不重复处理 */ }
        finally { Config = WebDavSync.LoadConfig(_dataDir); } // 刷新（含 lastSyncAt / lastSyncError）
    }

    /// <summary>立即同步（工具条「同步」按钮 / 数据管理「立即同步」调用）：用传入配置跑 RunSync；
    /// 成功后重载磁盘配置（引擎已在收尾把 lastSyncAt/密码写回 webdav.json，重载最准）。</summary>
    public async Task<SyncResult> RunNow(SyncConfig cfg)
    {
        var r = await Task.Run(() => SyncEngine.RunSync(_storage, _fileStore, cfg, _dataDir));
        Config = WebDavSync.LoadConfig(_dataDir); // 同步后引擎已把配置（含密码）写盘，重载最准
        return r;
    }
}
