using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TargetBarkNotifier;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/tbn";

    [PluginService] public IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public IPluginLog Log { get; private set; } = null!;
    [PluginService] public IGameGui GameGui { get; private set; } = null!;
    [PluginService] public IFramework Framework { get; private set; } = null!;
    [PluginService] public IClientState ClientState { get; private set; } = null!;

    public IDataManager DataManager { get; }

    public string Name => "Target Bark Notifier";
    public Configuration Configuration { get; }

    private readonly Dictionary<string, DateTime> lastTriggeredAt = new(StringComparer.Ordinal);
    private readonly List<NotificationRecord> notificationRecords = [];
    private readonly object recordsLock = new();
    private readonly TtsService ttsService;
    private readonly PushService pushService;
    private DateTime lastOfflineTriggerUtc = DateTime.MinValue;
    private int offlineAlertCount;
    private bool offlineAlertPaused;
    private readonly object offlineLock = new();
    private readonly List<OfflineNodeInfo> offlineNodes = [];
    private DateTime lastOfflineScanUtc = DateTime.MinValue;
    private int lastOfflineNodeCount = 0;
    private string lastOfflineScanInfo = "";
    private Vector3 idleLastPosition;
    private DateTime? idleLastMoveTime;
    private int idleStuckSeconds;
    private DateTime idleLastTriggerUtc = DateTime.MinValue;
    private float idleCurrentPosX, idleCurrentPosY, idleCurrentPosZ;
    private float idleLastPosX, idleLastPosY, idleLastPosZ;
    private float idleDeltaX, idleDeltaY, idleDeltaZ;
    private Vector3 euclideanLastPosition;
    private DateTime euclideanDiffMoveStart = DateTime.MinValue;
    private int euclideanDiffStuckSeconds;
    private DateTime euclideanDiffLastTriggerUtc = DateTime.MinValue;
    private float euclideanDistance;
    private string idlePlayerSource = string.Empty;
    private bool idlePlayerFound;
    
    private readonly object addonListLock = new();

    public void ResetIdleState()
    {
        idleLastMoveTime = null;
        idleStuckSeconds = 0;
        idleLastTriggerUtc = DateTime.MinValue;
        idleCurrentPosX = idleCurrentPosY = idleCurrentPosZ = 0;
        idleLastPosX = idleLastPosY = idleLastPosZ = 0;
        idleDeltaX = idleDeltaY = idleDeltaZ = 0;
        ResetEuclideanState();
    }

    public void ResetEuclideanState()
    {
        euclideanDiffMoveStart = DateTime.MinValue;
        euclideanDiffStuckSeconds = 0;
        euclideanDiffLastTriggerUtc = DateTime.MinValue;
        euclideanDistance = 0;
        euclideanLastPosition = Vector3.Zero;
    }
    private readonly List<string> visibleAddons = [];
    private DateTime lastAddonScanUtc = DateTime.MinValue;
    private string lastKnownCharacterName = string.Empty;
    private string lastKnownHomeWorldName = string.Empty;
    private string lastKnownCurrentWorldName = string.Empty;
    private bool hasKnownCharacterWorld;
    private readonly WindowSystem windowSystem = new("TargetBarkNotifier");
    private readonly MainWindow mainWindow;
    private readonly MonitorClient monitorClient;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IPluginLog log,
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ChatGui = chatGui;
        Log = log;
        Framework = framework;
        ClientState = clientState;
        DataManager = dataManager;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        EnsureDefaultRuleIfNeeded();

        ttsService = new TtsService(Log);
        pushService = new PushService(Log, ChatGui, Configuration, AddNotificationRecord, ApplyCharacterPlaceholders);
        monitorClient = new MonitorClient(Configuration);
        monitorClient.OnConnectionLost += async () =>
        {
            Configuration.EnableMonitor = false;
            await StopMonitorClient();
            Configuration.Save();
        };

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/tbn 打开主窗口\n/tbn on 启用插件\n/tbn off 禁用插件\n/tbn test 发送测试推送\n/tbn status 查看当前状态\n/tbn idlenotify on|off 开关静止检测"
        });
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenUi;

        Log.Information("TargetBarkNotifier loaded.");
        _ = monitorClient.StartAsync();
    }

    public bool IsMonitorConnected => monitorClient.IsConnected;
    public bool MonitorConnectionFailed => monitorClient.ConnectionFailed;
    public MonitorClient MonitorClient => monitorClient;

    public async Task StartMonitorClient()
    {
        await monitorClient.StartAsync();
    }

    public async Task StopMonitorClient()
    {
        await monitorClient.StopAsync();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        UpdateCharacterWorldSnapshot();

        if (!Configuration.Enabled)
            return;

        if (Configuration.EnableOfflineMonitor)
            UpdateOfflineMonitor();

        if (Configuration.EnableIdleDetect && Configuration.EnableComponentDiffDetect)
        {
            UpdateComponentDiffDetect();
        }

        if (Configuration.EnableIdleDetect && Configuration.EnableEuclideanDiffDetect)
        {
            UpdateEuclideanDiffDetect();
        }
    }

    private void UpdateOfflineMonitor()
    {
        if (offlineAlertPaused)
        {
            if (IsOfflineAddonMissing())
            {
                offlineAlertPaused = false;
                offlineAlertCount = 0;
            }
            return;
        }

        if (IsOfflineConditionMatched())
        {
            var cooldown = Math.Max(Configuration.OfflineCooldownSeconds, 0);
            var now = DateTime.UtcNow;
            if (cooldown > 0 && (now - lastOfflineTriggerUtc).TotalSeconds < cooldown)
                return;

            lastOfflineTriggerUtc = now;
            var titleTemplate = string.IsNullOrWhiteSpace(Configuration.OfflinePushTitle) ? "掉线监控" : Configuration.OfflinePushTitle;
            var contentTemplate = string.IsNullOrWhiteSpace(Configuration.OfflinePushContent) ? "断开连接" : Configuration.OfflinePushContent;
            var title = ApplyCharacterPlaceholders(titleTemplate);
            var content = ApplyCharacterPlaceholders(contentTemplate);
            _ = pushService.TriggerPushAsync(title, content, "OfflineMonitor", "OfflineMonitor");
            offlineAlertCount++;
            var alertLimit = Math.Clamp(Configuration.OfflineAlertLimit, 1, 10);
            if (offlineAlertCount >= alertLimit)
            {
                offlineAlertPaused = true;
            }
        }
    }

    public int IdleStuckSeconds => idleStuckSeconds;
    public float IdleCurrentPosX => idleCurrentPosX;
    public float IdleCurrentPosY => idleCurrentPosY;
    public float IdleCurrentPosZ => idleCurrentPosZ;
    public float IdleLastPosX => idleLastPosX;
    public float IdleLastPosY => idleLastPosY;
    public float IdleLastPosZ => idleLastPosZ;
    public float IdleDeltaX => idleDeltaX;
    public float IdleDeltaY => idleDeltaY;
    public float IdleDeltaZ => idleDeltaZ;
    public string IdlePlayerSource => idlePlayerSource;
    public bool IdlePlayerFound => idlePlayerFound;
    public float EuclideanDistance => euclideanDistance;
    public int EuclideanDiffStuckSeconds => euclideanDiffStuckSeconds;
    public float EuclideanLastPosX => euclideanLastPosition.X;
    public float EuclideanLastPosY => euclideanLastPosition.Y;
    public float EuclideanLastPosZ => euclideanLastPosition.Z;

    private unsafe bool EnsureIdlePlayerReady()
    {
        var lp = Control.GetLocalPlayer();
        if (lp == null)
        {
            idlePlayerFound = false;
            idlePlayerSource = "未获取到角色";
            idleLastMoveTime = null;
            idleStuckSeconds = 0;
            return false;
        }

        idlePlayerFound = true;
        idlePlayerSource = "CSGameObject";
        var pos = lp->Position;
        idleCurrentPosX = pos.X;
        idleCurrentPosY = pos.Y;
        idleCurrentPosZ = pos.Z;

        if (idleLastMoveTime == null)
        {
            idleLastPosition = pos;
            idleLastMoveTime = DateTime.UtcNow;
            idleLastPosX = pos.X;
            idleLastPosY = pos.Y;
            idleLastPosZ = pos.Z;
            idleDeltaX = 0;
            idleDeltaY = 0;
            idleDeltaZ = 0;
            euclideanLastPosition = pos;
        }

        return true;
    }

    private void UpdateComponentDiffDetect()
    {
        if (!EnsureIdlePlayerReady())
            return;

        idleDeltaX = Math.Abs(idleCurrentPosX - idleLastPosition.X);
        idleDeltaY = Math.Abs(idleCurrentPosY - idleLastPosition.Y);
        idleDeltaZ = Math.Abs(idleCurrentPosZ - idleLastPosition.Z);
        idleLastPosX = idleLastPosition.X;
        idleLastPosY = idleLastPosition.Y;
        idleLastPosZ = idleLastPosition.Z;
        var threshold = Configuration.ComponentDiffThreshold;

        if (idleDeltaX <= threshold && idleDeltaY <= threshold && idleDeltaZ <= threshold)
        {
            var stationaryTime = (DateTime.UtcNow - idleLastMoveTime!.Value).TotalSeconds;
            idleStuckSeconds = (int)stationaryTime;

            if (stationaryTime >= Configuration.ComponentDiffTimeoutSeconds)
            {
                var now = DateTime.UtcNow;
                var cooldown = Math.Max(Configuration.ComponentDiffTimeoutSeconds, 0);
                if (cooldown > 0 && (now - idleLastTriggerUtc).TotalSeconds < cooldown)
                    return;

                idleLastTriggerUtc = now;
                var titleTemplate = string.IsNullOrWhiteSpace(Configuration.ComponentDiffPushTitle) ? "分量静止检测" : Configuration.ComponentDiffPushTitle;
                var contentTemplate = string.IsNullOrWhiteSpace(Configuration.ComponentDiffPushContent) ? "角色已静止 {stuck} 秒！" : Configuration.ComponentDiffPushContent;
                var title = ApplyCharacterPlaceholders(titleTemplate);
                var content = ApplyCharacterPlaceholders(contentTemplate).Replace("{stuck}", idleStuckSeconds.ToString(), StringComparison.Ordinal);
                _ = pushService.TriggerPushAsync(title, content, "ComponentDiffDetect", "ComponentDiffDetect");
                idleLastMoveTime = DateTime.UtcNow;
            }
        }
        else
        {
            idleLastPosition = new Vector3(idleCurrentPosX, idleCurrentPosY, idleCurrentPosZ);
            idleLastMoveTime = DateTime.UtcNow;
            idleStuckSeconds = 0;
        }
    }

    private void UpdateEuclideanDiffDetect()
    {
        if (!EnsureIdlePlayerReady())
            return;

        var currentPos = new Vector3(idleCurrentPosX, idleCurrentPosY, idleCurrentPosZ);
        var deltaX = Math.Abs(currentPos.X - euclideanLastPosition.X);
        var deltaY = Math.Abs(currentPos.Y - euclideanLastPosition.Y);
        var deltaZ = Math.Abs(currentPos.Z - euclideanLastPosition.Z);

        euclideanDistance = (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);

        var threshold = Configuration.EuclideanDiffThreshold;

        if (euclideanDistance <= threshold)
        {
            if (euclideanDiffMoveStart == DateTime.MinValue)
            {
                euclideanDiffMoveStart = DateTime.UtcNow;
            }
            else
            {
                euclideanDiffStuckSeconds = (int)(DateTime.UtcNow - euclideanDiffMoveStart).TotalSeconds;

                if (euclideanDiffStuckSeconds >= Configuration.EuclideanDiffTimeoutSeconds)
                {
                    var now = DateTime.UtcNow;
                    var cooldown = Math.Max(Configuration.EuclideanDiffTimeoutSeconds, 0);
                    if (cooldown > 0 && (now - euclideanDiffLastTriggerUtc).TotalSeconds < cooldown)
                        return;

                    euclideanDiffLastTriggerUtc = now;
                    var titleTemplate = string.IsNullOrWhiteSpace(Configuration.EuclideanDiffPushTitle) ? "几何静止检测" : Configuration.EuclideanDiffPushTitle;
                    var contentTemplate = string.IsNullOrWhiteSpace(Configuration.EuclideanDiffPushContent) ? "角色已静止 {stuck} 秒！" : Configuration.EuclideanDiffPushContent;
                    var title = ApplyCharacterPlaceholders(titleTemplate);
                    var content = ApplyCharacterPlaceholders(contentTemplate).Replace("{stuck}", euclideanDiffStuckSeconds.ToString(), StringComparison.Ordinal);
                    _ = pushService.TriggerPushAsync(title, content, "EuclideanDiffDetect", "EuclideanDiffDetect");
                    euclideanDiffMoveStart = DateTime.UtcNow;
                }
            }
        }
        else
        {
            euclideanLastPosition = currentPos;
            euclideanDiffMoveStart = DateTime.MinValue;
            euclideanDiffStuckSeconds = 0;
        }
    }

    public string CurrentCharacterWorldDisplay
    {
        get
        {
            var latest = TryBuildCurrentCharacterWorld();
            if (latest.HasValue)
            {
                lastKnownCharacterName = latest.Value.Name;
                lastKnownHomeWorldName = latest.Value.HomeWorld;
                lastKnownCurrentWorldName = latest.Value.CurrentWorld;
                hasKnownCharacterWorld = true;
                return $"{latest.Value.Name}@{latest.Value.HomeWorld}，当前位于：{latest.Value.CurrentWorld}";
            }

            if (hasKnownCharacterWorld)
                return $"{lastKnownCharacterName}@{lastKnownHomeWorldName}，当前位于：{lastKnownCurrentWorldName}";

            return "未获取到当前角色名及服务器";
        }
    }

    private void UpdateCharacterWorldSnapshot()
    {
        var latest = TryBuildCurrentCharacterWorld();
        if (!latest.HasValue)
            return;

        lastKnownCharacterName = latest.Value.Name;
        lastKnownHomeWorldName = latest.Value.HomeWorld;
        lastKnownCurrentWorldName = latest.Value.CurrentWorld;
        hasKnownCharacterWorld = true;
    }

    private unsafe (string Name, string HomeWorld, string CurrentWorld)? TryBuildCurrentCharacterWorld()
    {
        var lp = Control.GetLocalPlayer();
        if (lp == null)
            return null;

        var nameSpan = lp->Name;
        var nullIdx = nameSpan.IndexOf((byte)0);
        if (nullIdx >= 0)
            nameSpan = nameSpan.Slice(0, nullIdx);
        var name = Encoding.UTF8.GetString(nameSpan);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var character = (Character*)lp;
        var homeWorld = ResolveWorldName(character->HomeWorld);
        var currentWorld = ResolveWorldName(character->CurrentWorld);
        if (string.IsNullOrWhiteSpace(currentWorld))
            currentWorld = homeWorld;
        return (name, homeWorld, currentWorld);
    }

    private string ResolveWorldName(ushort worldId)
    {
        if (worldId == 0)
            return string.Empty;

        try
        {
            var sheet = DataManager.GetExcelSheet<World>();
            if (sheet == null)
                return worldId.ToString();

            var row = sheet.GetRow(worldId);
            if (row.RowId == 0)
                return worldId.ToString();

            var name = row.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return worldId.ToString();
    }

    private string ApplyCharacterPlaceholders(string template)
    {
        var result = template ?? string.Empty;
        if (result.Length == 0)
            return result;

        var name = hasKnownCharacterWorld ? lastKnownCharacterName : string.Empty;
        var server = hasKnownCharacterWorld ? lastKnownHomeWorldName : string.Empty;
        var currentServer = hasKnownCharacterWorld ? lastKnownCurrentWorldName : string.Empty;

        result = result.Replace("{name}", name, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{server}", server, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{currentserver}", currentServer, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    public string ApplyRulePlaceholders(string template, string message, string sender, string channel)
    {
        var resolved = ApplyTemplate(template ?? string.Empty, message ?? string.Empty, sender ?? string.Empty, channel ?? string.Empty);
        return ApplyCharacterPlaceholders(resolved);
    }

    public void RequestAddonScan()
    {
        CaptureVisibleAddons();
    }

    public IReadOnlyList<string> GetVisibleAddonSnapshot()
    {
        lock (addonListLock)
        {
            return visibleAddons.ToArray();
        }
    }

    public DateTime GetVisibleAddonSnapshotTimeUtc()
    {
        return lastAddonScanUtc;
    }

    public void RequestOfflineNodeScan()
    {
        CaptureOfflineNodes();
    }

    public IReadOnlyList<OfflineNodeInfo> GetOfflineNodeSnapshot()
    {
        lock (offlineLock)
        {
            return offlineNodes.ToArray();
        }
    }

    public (DateTime TimeUtc, int Count) GetOfflineNodeSnapshotStatus()
    {
        lock (offlineLock)
        {
            return (lastOfflineScanUtc, lastOfflineNodeCount);
        }
    }

    public (DateTime TimeUtc, int Count, string Info) GetOfflineNodeSnapshotInfo()
    {
        lock (offlineLock)
        {
            return (lastOfflineScanUtc, lastOfflineNodeCount, lastOfflineScanInfo);
        }
    }

    private unsafe bool CaptureVisibleAddons()
    {
        try
        {
            var list = new List<string>();
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return false;

            var loaded = manager->AllLoadedUnitsList;
            for (var i = 0; i < loaded.Count; i++)
            {
                var unit = loaded.Entries[i].Value;
                if (unit == null)
                    continue;
                var name = unit->NameString;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                list.Add(name);
            }

            if (list.Count == 0)
            {
                var focused = manager->FocusedUnitsList;
                for (var i = 0; i < focused.Count; i++)
                {
                    var unit = focused.Entries[i].Value;
                    if (unit == null)
                        continue;
                    var name = unit->NameString;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    list.Add(name);
                }
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            lock (addonListLock)
            {
                visibleAddons.Clear();
                visibleAddons.AddRange(list);
                lastAddonScanUtc = DateTime.UtcNow;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Offline addon scan failed");
            return false;
        }
    }

    private static unsafe string TryReadText(AtkTextNode* textNode)
    {
        if (textNode == null)
            return string.Empty;

        try
        {
            return textNode->NodeText.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static unsafe AtkUnitBase* FindAddonByName(string addonName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
            return null;

        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return null;

        var loaded = manager->AllLoadedUnitsList;
        for (var i = 0; i < loaded.Count; i++)
        {
            var unit = loaded.Entries[i].Value;
            if (unit == null)
                continue;
            var name = unit->NameString;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (name.Equals(addonName, StringComparison.OrdinalIgnoreCase))
                return unit;
        }

        var focused = manager->FocusedUnitsList;
        for (var i = 0; i < focused.Count; i++)
        {
            var unit = focused.Entries[i].Value;
            if (unit == null)
                continue;
            var name = unit->NameString;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (name.Equals(addonName, StringComparison.OrdinalIgnoreCase))
                return unit;
        }

        return null;
    }

    private unsafe bool CaptureOfflineNodes()
    {
        try
        {
            var addonName = string.IsNullOrWhiteSpace(Configuration.OfflineAddonName) ? "Dialogue" : Configuration.OfflineAddonName.Trim();
            AtkUnitBase* addon = null;
            var info = string.Empty;

            if (GameGui != null)
            {
                var addonWrapper = GameGui.GetAddonByName(addonName, 1);
                if (addonWrapper != null && addonWrapper.Address != IntPtr.Zero)
                    addon = (AtkUnitBase*)addonWrapper.Address;
            }

            if (addon == null)
                addon = FindAddonByName(addonName);

            if (addon == null)
            {
                info = $"Addon 未找到: {addonName}";
                lock (offlineLock)
                {
                    offlineNodes.Clear();
                    lastOfflineScanUtc = DateTime.UtcNow;
                    lastOfflineNodeCount = 0;
                    lastOfflineScanInfo = info;
                }
                return false;
            }

            if (!addon->IsVisible)
            {
                info = $"Addon 不可见: {addonName}";
                lock (offlineLock)
                {
                    offlineNodes.Clear();
                    lastOfflineScanUtc = DateTime.UtcNow;
                    lastOfflineNodeCount = 0;
                    lastOfflineScanInfo = info;
                }
                return false;
            }

            var list = addon->UldManager.NodeList;
            var snapshot = new List<OfflineNodeInfo>();
            if (list != null)
            {
                for (var i = 0; i < addon->UldManager.NodeListCount; i++)
                {
                    var node = list[i];
                    if (node == null)
                        continue;
                    if (node->Type != NodeType.Text)
                        continue;

                    var textNode = node->GetAsAtkTextNode();
                    var text = TryReadText(textNode);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    snapshot.Add(new OfflineNodeInfo
                    {
                        NodeId = node->NodeId,
                        Text = text
                    });
                }
            }

            info = $"Addon: {addonName}, 节点={snapshot.Count}";
            lock (offlineLock)
            {
                offlineNodes.Clear();
                offlineNodes.AddRange(snapshot);
                lastOfflineScanUtc = DateTime.UtcNow;
                lastOfflineNodeCount = snapshot.Count;
                lastOfflineScanInfo = info;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Offline node scan failed");
            lock (offlineLock)
            {
                lastOfflineScanUtc = DateTime.UtcNow;
                lastOfflineNodeCount = 0;
                lastOfflineScanInfo = "扫描异常";
            }
            return false;
        }
    }

    private unsafe bool IsOfflineConditionMatched()
    {
        if (Configuration.OfflineMatchTexts == null || Configuration.OfflineMatchTexts.Count == 0)
            return false;

        if (!CaptureOfflineNodes())
            return false;

        var matchTexts = Configuration.OfflineMatchTexts;

        lock (offlineLock)
        {
            foreach (var node in offlineNodes)
            {
                if (string.IsNullOrWhiteSpace(node.Text))
                    continue;

                foreach (var match in matchTexts)
                {
                    if (string.IsNullOrWhiteSpace(match))
                        continue;
                    if (node.Text.Contains(match, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }

        return false;
    }

    private unsafe bool IsOfflineAddonMissing()
    {
        if (string.IsNullOrWhiteSpace(Configuration.OfflineAddonName))
            return true;

        var addon = FindAddonByName(Configuration.OfflineAddonName.Trim());
        return addon == null || !addon->IsVisible;
    }

    private void EnsureDefaultRuleIfNeeded()
    {
        if (!Configuration.HasCompletedFirstSetup || Configuration.Rules.Count == 0)
        {
            Configuration.Rules.Add(new MatchRule
            {
                Enabled = true,
                MatchText = "有人正在以你为目标",
                MessageTitle = "目标提醒通知",
                MessageContent = "检测到目标提示：{message}"
            });
            Configuration.HasCompletedFirstSetup = true;
            Configuration.Save();
        }
    }

    private void OnCommand(string command, string args)
    {
        var action = (args ?? string.Empty).Trim().ToLowerInvariant();
        if (action.Length == 0)
        {
            mainWindow.IsOpen = true;
            return;
        }

        if (action == "idlenotify on")
        {
            Configuration.EnableIdleDetect = true;
            Configuration.EnableComponentDiffDetect = true;
            Configuration.Save();
            ResetIdleState();
            ChatGui.Print("[TBN] 静止检测已启用。");
            return;
        }

        if (action == "idlenotify off")
        {
            Configuration.EnableIdleDetect = false;
            Configuration.Save();
            ResetIdleState();
            ChatGui.Print("[TBN] 静止检测已禁用。");
            return;
        }

        switch (action)
        {
            case "on":
                Configuration.Enabled = true;
                Configuration.Save();
                ChatGui.Print("[TBN] 已启用。");
                break;
            case "off":
                Configuration.Enabled = false;
                Configuration.Save();
                ChatGui.Print("[TBN] 已禁用。");
                break;
            case "test":
                TriggerTestPush();
                ChatGui.Print("[TBN] 已发送测试请求。");
                break;
            case "status":
                ChatGui.Print($"[TBN] 状态={(Configuration.Enabled ? "启用" : "禁用")}, 规则数量={Configuration.Rules.Count}");
                break;
            default:
                ChatGui.Print("[TBN] 用法: /tbn(打开主窗口) | /tbn on | /tbn off | /tbn test | /tbn status | /tbn idlenotify on|off");
                break;
        }
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        if (!Configuration.Enabled)
            return;

        var text = chatMessage.Message.TextValue;
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var rule in Configuration.Rules)
        {
            if (!rule.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(rule.MatchText))
                continue;
            var senderName = chatMessage.Sender.TextValue?.Trim() ?? string.Empty;
            var channelName = chatMessage.LogKind.ToString();
            var matchSource = $"[{channelName}]{senderName}:{text}";
            var resolvedMatchText = ApplyCharacterPlaceholders(rule.MatchText);
            if (!IsRuleMatched(resolvedMatchText, matchSource))
                continue;

            if (!IsTerritoryMatched(rule.TerritoryId))
            {
                AddNotificationRecord(new NotificationRecord
                {
                    TimeLocal = DateTime.Now,
                    Ignored = true,
                    Success = false,
                    PushProvider = "规则过滤",
                    PushIdentity = string.Empty,
                    MatchText = rule.MatchText,
                    Title = string.IsNullOrWhiteSpace(rule.MessageTitle) ? "目标提醒通知" : rule.MessageTitle,
                    Content = rule.MessageContent ?? string.Empty,
                    Detail = $"MapID不匹配: 当前={CurrentTerritoryId}, 规则={rule.TerritoryId} | 来源={matchSource}"
                });
                continue;
            }

            var resolvedExcludeText = ApplyCharacterPlaceholders(rule.ExcludeText);
            if (TryGetMatchedExcludeToken(resolvedExcludeText, out var matchedExcludeToken, matchSource))
            {
                AddNotificationRecord(new NotificationRecord
                {
                    TimeLocal = DateTime.Now,
                    Ignored = true,
                    Success = false,
                    PushProvider = "规则过滤",
                    PushIdentity = string.Empty,
                    MatchText = rule.MatchText,
                    Title = string.IsNullOrWhiteSpace(rule.MessageTitle) ? "目标提醒通知" : rule.MessageTitle,
                    Content = rule.MessageContent ?? string.Empty,
                    Detail = $"命中排除词: {matchedExcludeToken} | 来源={matchSource}"
                });
                continue;
            }
            if (InCooldown(rule))
                continue;

            var finalContent = BuildFinalContent(rule.MessageContent, text, senderName, channelName);
            var finalTitle = BuildFinalTitle(rule.MessageTitle, text, senderName, channelName);
            if (Configuration.EnableTts)
                ttsService.TrySpeak(text);
            _ = pushService.TriggerPushAsync(finalTitle, finalContent, rule.MatchText, matchSource);
        }

    }

    private bool InCooldown(MatchRule rule)
    {
        var seconds = Math.Max(Configuration.RepeatCooldownSeconds, 0);
        if (seconds == 0)
            return false;

        var key = rule.MatchText;

        var now = DateTime.UtcNow;
        if (!lastTriggeredAt.TryGetValue(key, out var last))
        {
            lastTriggeredAt[key] = now;
            return false;
        }

        if ((now - last).TotalSeconds < seconds)
            return true;

        lastTriggeredAt[key] = now;
        return false;
    }

    private static bool IsRuleMatched(string ruleMatchText, string incomingMessage)
    {
        var groups = ParseRuleMatchExpression(ruleMatchText);
        if (groups.Count == 0)
            return false;

        foreach (var andGroup in groups)
        {
            if (andGroup.Count == 0)
                continue;

            var allMatched = true;
            foreach (var token in andGroup)
            {
                if (!incomingMessage.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    allMatched = false;
                    break;
                }
            }

            if (allMatched)
                return true;
        }

        return false;
    }

    public static IReadOnlyList<IReadOnlyList<string>> ParseRuleMatchExpression(string expression)
    {
        var groups = new List<IReadOnlyList<string>>();
        var orParts = (expression ?? string.Empty).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in orParts)
        {
            var andParts = part.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (andParts.Length == 0)
                continue;
            groups.Add(andParts);
        }

        return groups;
    }

    private static bool TryGetMatchedExcludeToken(string excludeExpression, out string matchedToken, params string[] incomingTargets)
    {
        matchedToken = string.Empty;
        if (string.IsNullOrWhiteSpace(excludeExpression))
            return false;

        var parts = excludeExpression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            foreach (var target in incomingTargets)
            {
                if (!string.IsNullOrWhiteSpace(target) && target.Contains(part, StringComparison.OrdinalIgnoreCase))
                {
                    matchedToken = part;
                    return true;
                }
            }
        }

        return false;
    }

    public IReadOnlyList<RuleDebugPreview> BuildRuleDebugPreviews(string channel, string sender, string message)
    {
        var previews = new List<RuleDebugPreview>();
        var normalizedChannel = channel ?? string.Empty;
        var normalizedSender = sender ?? string.Empty;
        var normalizedMessage = message ?? string.Empty;
        var source = $"[{normalizedChannel}]{normalizedSender}:{normalizedMessage}";

        foreach (var rule in Configuration.Rules)
        {
            if (!rule.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(rule.MatchText))
                continue;
            if (!IsTerritoryMatched(rule.TerritoryId))
                continue;

            var resolvedMatchText = ApplyCharacterPlaceholders(rule.MatchText);
            if (!IsRuleMatched(resolvedMatchText, source))
                continue;

            var resolvedExcludeText = ApplyCharacterPlaceholders(rule.ExcludeText);
            if (TryGetMatchedExcludeToken(resolvedExcludeText, out _, source))
                continue;

            previews.Add(new RuleDebugPreview
            {
                MatchText = rule.MatchText,
                Title = BuildFinalTitle(rule.MessageTitle, normalizedMessage, normalizedSender, normalizedChannel),
                Content = BuildFinalContent(rule.MessageContent, normalizedMessage, normalizedSender, normalizedChannel)
            });
        }

        return previews;
    }

    private bool IsTerritoryMatched(string? territoryId)
    {
        if (string.IsNullOrWhiteSpace(territoryId))
            return true;

        var current = GetCurrentTerritoryId();
        var parts = territoryId.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true;

        foreach (var part in parts)
        {
            if (ushort.TryParse(part, out var parsedId) && parsedId == current)
                return true;
        }

        return false;
    }

    public ushort CurrentTerritoryId => GetCurrentTerritoryId();

    private unsafe ushort GetCurrentTerritoryId()
    {
        var id = ClientState?.TerritoryType ?? 0;
        if (id != 0)
            return (ushort)id;

        var gameMain = GameMain.Instance();
        if (gameMain != null)
            return (ushort)gameMain->CurrentTerritoryTypeId;

        return 0;
    }

    private string BuildFinalContent(string? template, string rawMessage, string sender, string channel)
    {
        var contentTemplate = template?.Trim() ?? string.Empty;
        if (contentTemplate.Length == 0)
            return ApplyCharacterPlaceholders(rawMessage);

        var resolved = ApplyTemplate(contentTemplate, rawMessage, sender, channel);
        return ApplyCharacterPlaceholders(resolved);
    }

    private string BuildFinalTitle(string? template, string rawMessage, string sender, string channel)
    {
        var titleTemplate = string.IsNullOrWhiteSpace(template) ? "目标提醒通知" : template.Trim();
        var resolved = ApplyTemplate(titleTemplate, rawMessage, sender, channel);
        return ApplyCharacterPlaceholders(resolved);
    }

    private static bool HasTemplateToken(string template)
    {
        return template.Contains("{message}", StringComparison.Ordinal) ||
               template.Contains("{sender}", StringComparison.Ordinal) ||
               template.Contains("{channel}", StringComparison.Ordinal);
    }

    private static string ApplyTemplate(string template, string message, string sender, string channel)
    {
        var result = template;
        if (result.Contains("{message}", StringComparison.Ordinal))
            result = result.Replace("{message}", message, StringComparison.Ordinal);
        if (result.Contains("{sender}", StringComparison.Ordinal))
            result = result.Replace("{sender}", sender, StringComparison.Ordinal);
        if (result.Contains("{channel}", StringComparison.Ordinal))
            result = result.Replace("{channel}", channel, StringComparison.Ordinal);
        return result;
    }


    private void AddNotificationRecord(NotificationRecord record)
    {
        lock (recordsLock)
        {
            notificationRecords.Insert(0, record);
            if (notificationRecords.Count > 30)
            {
                notificationRecords.RemoveRange(30, notificationRecords.Count - 30);
            }
        }
    }

    public IReadOnlyList<NotificationRecord> GetNotificationRecordsSnapshot()
    {
        lock (recordsLock)
        {
            return notificationRecords.ToArray();
        }
    }

    public void ClearNotificationRecords()
    {
        lock (recordsLock)
        {
            notificationRecords.Clear();
        }
    }


    private void DrawUi()
    {
        windowSystem.Draw();
    }

    private void OpenUi()
    {
        mainWindow.IsOpen = true;
    }

    public void TriggerTestPush()
    {
        if (Configuration.EnableTts)
            ttsService.TrySpeak("TBN测试消息");
        _ = pushService.TriggerPushAsync("目标提醒通知", "TBN测试消息", "ManualTest", "ManualTest");
    }

    public void PrintTestResultToChat(bool ok, string matchText)
    {
        ChatGui.Print(ok
            ? $"[TBN] 测试匹配成功: {matchText}"
            : $"[TBN] 测试未匹配: {matchText}");
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= OpenUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenUi;
        ttsService.Dispose();
        monitorClient.Dispose();
        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }

    public sealed class OfflineNodeInfo
    {
        public uint NodeId { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    public sealed class RuleDebugPreview
    {
        public string MatchText { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }
}
