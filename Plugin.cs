using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
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
    [PluginService] public IObjectTable ObjectTable { get; private set; } = null!;

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
    private readonly object addonListLock = new();
    private readonly List<string> visibleAddons = [];
    private DateTime lastAddonScanUtc = DateTime.MinValue;
    private string lastKnownCharacterName = string.Empty;
    private string lastKnownServerName = string.Empty;
    private string lastKnownCharacterSource = string.Empty;
    private bool hasKnownCharacterWorld;
    private readonly WindowSystem windowSystem = new("TargetBarkNotifier");
    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IChatGui chatGui,
        IPluginLog log,
        IFramework framework,
        IClientState clientState)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        ChatGui = chatGui;
        Log = log;
        Framework = framework;
        ClientState = clientState;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);
        EnsureDefaultRuleIfNeeded();

        ttsService = new TtsService(Log);
        pushService = new PushService(Log, ChatGui, Configuration, AddNotificationRecord, ApplyCharacterPlaceholders);

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/tbn 打开主窗口\n/tbn on 启用插件\n/tbn off 禁用插件\n/tbn test 发送测试推送\n/tbn status 查看当前状态"
        });
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += OpenUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenUi;

        Log.Information("TargetBarkNotifier loaded.");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        UpdateCharacterWorldSnapshot();

        if (!Configuration.Enabled || !Configuration.EnableOfflineMonitor)
            return;

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

    public string CurrentCharacterWorldDisplay
    {
        get
        {
            var latest = TryBuildCurrentCharacterWorld();
            if (latest.HasValue)
            {
                lastKnownCharacterName = latest.Value.Name;
                lastKnownServerName = latest.Value.Server;
                lastKnownCharacterSource = latest.Value.Source;
                hasKnownCharacterWorld = true;
                return $"{latest.Value.Name}@{latest.Value.Server} [{latest.Value.Source}]";
            }

            if (hasKnownCharacterWorld)
                return $"{lastKnownCharacterName}@{lastKnownServerName} [{lastKnownCharacterSource}]";

            return "未获取到当前角色名及服务器";
        }
    }

    private void UpdateCharacterWorldSnapshot()
    {
        var latest = TryBuildCurrentCharacterWorld();
        if (!latest.HasValue)
            return;

        lastKnownCharacterName = latest.Value.Name;
        lastKnownServerName = latest.Value.Server;
        lastKnownCharacterSource = latest.Value.Source;
        hasKnownCharacterWorld = true;
    }

    private (string Name, string Server, string Source)? TryBuildCurrentCharacterWorld()
    {
        foreach (var candidate in EnumeratePlayerCandidates())
        {
            var player = candidate.Player;
            var name = TryReadTextValue(player, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var world = TryReadWorldName(player);
            if (string.IsNullOrWhiteSpace(world))
                continue;

            return (name, world, candidate.Source);
        }

        return null;
    }

    private IEnumerable<(object Player, string Source)> EnumeratePlayerCandidates()
    {
        object? objectTablePlayer = null;
        try
        {
            objectTablePlayer = ObjectTable?.LocalPlayer;
        }
        catch
        {
        }

        if (objectTablePlayer != null)
            yield return (objectTablePlayer, "ObjectTable");

        object? clientStatePlayer = null;
        try
        {
            clientStatePlayer = TryReadNestedProperty(ClientState, "LocalPlayer");
        }
        catch
        {
        }

        if (clientStatePlayer != null)
            yield return (clientStatePlayer, "ClientState");
    }

    private string ApplyCharacterPlaceholders(string template)
    {
        var result = template ?? string.Empty;
        if (result.Length == 0)
            return result;

        var name = hasKnownCharacterWorld ? lastKnownCharacterName : string.Empty;
        var server = hasKnownCharacterWorld ? lastKnownServerName : string.Empty;

        result = result.Replace("{name}", name, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{server}", server, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    public string ApplyRulePlaceholders(string template, string message, string sender, string channel)
    {
        var resolved = ApplyTemplate(template ?? string.Empty, message ?? string.Empty, sender ?? string.Empty, channel ?? string.Empty);
        return ApplyCharacterPlaceholders(resolved);
    }

    private static string? TryReadTextValue(object source, string propertyName)
    {
        try
        {
            var prop = source.GetType().GetProperty(propertyName);
            if (prop == null)
                return null;

            var value = prop.GetValue(source);
            if (value == null)
                return null;

            var textValueProp = value.GetType().GetProperty("TextValue");
            if (textValueProp != null)
            {
                var textValue = textValueProp.GetValue(value)?.ToString();
                if (!string.IsNullOrWhiteSpace(textValue))
                    return textValue;
            }

            var text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadWorldName(object player)
    {
        var current = TryReadWorldNameFromProperty(player, "CurrentWorld");
        if (!string.IsNullOrWhiteSpace(current))
            return current;

        return TryReadWorldNameFromProperty(player, "HomeWorld");
    }

    private static string? TryReadWorldNameFromProperty(object source, string propertyName)
    {
        try
        {
            var prop = source.GetType().GetProperty(propertyName);
            if (prop == null)
                return null;

            var worldRef = prop.GetValue(source);
            if (worldRef == null)
                return null;

            var worldObj = TryReadNestedProperty(worldRef, "Value") ?? TryReadNestedProperty(worldRef, "ValueNullable");
            if (worldObj == null)
                return null;

            var nameObj = TryReadNestedProperty(worldObj, "Name");
            if (nameObj == null)
                return null;

            var textValue = TryReadNestedProperty(nameObj, "TextValue")?.ToString();
            if (!string.IsNullOrWhiteSpace(textValue))
                return textValue;

            var raw = nameObj.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch
        {
            return null;
        }
    }

    private static object? TryReadNestedProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType().GetProperty(propertyName)?.GetValue(source);
        }
        catch
        {
            return null;
        }
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
                ChatGui.Print("[TBN] 用法: /tbn(打开主窗口) | /tbn on | /tbn off | /tbn test | /tbn status");
                break;
        }
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!Configuration.Enabled)
            return;

        var text = message.TextValue;
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var rule in Configuration.Rules)
        {
            if (!rule.Enabled)
                continue;
            if (string.IsNullOrWhiteSpace(rule.MatchText))
                continue;
            var senderName = sender.TextValue?.Trim() ?? string.Empty;
            var channelName = type.ToString();
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
            if (!IsRuleMatched(rule.MatchText, source))
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
            return id;

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
