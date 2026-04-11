using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace TargetBarkNotifier;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private int selectedRuleIndex = -1;
    private string importExportPath = string.Empty;
    private string ioStatus = string.Empty;
    private bool showAddRulePopup = false;
    private string newRuleMatchText = string.Empty;
    private string newRuleTitle = "目标提醒通知";
    private string newRuleContent = "检测到目标提示：{message}";
    private bool showRulesDebugWindow;
    private string rulesDebugMatchInput = string.Empty;
    private string rulesDebugParseOutput = string.Empty;
    private string rulesDebugMessageInput = string.Empty;
    private string rulesDebugSenderInput = string.Empty;
    private string rulesDebugChannelInput = string.Empty;
    private string rulesDebugPreviewOutput = string.Empty;
    private bool showRecordSuccess = true;
    private bool showRecordFailed = true;
    private bool showRecordIgnored = true;
    private bool showOfflineScanWindow;
    private bool rulesFileDialogPending;
    private bool allowEditOfflineAddon;
    private readonly List<string> monitorLogs = new();
    private readonly object monitorLogLock = new();

    public MainWindow(Plugin plugin)
        : base("TargetBarkNotifier", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(860, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        importExportPath = Path.Combine(plugin.PluginInterface.GetPluginConfigDirectory(), "rules.json");
        
        plugin.MonitorClient.OnLog += msg =>
        {
            lock (monitorLogLock)
            {
                monitorLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (monitorLogs.Count > 50)
                    monitorLogs.RemoveAt(0);
            }
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawFileDialogs();

        if (ImGui.BeginTabBar("##tbn_tabs_v2"))
        {
            if (ImGui.BeginTabItem("说明"))
            {
                DrawMatchHelp();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("基础设置"))
            {
                DrawGeneralSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("规则设置"))
            {
                DrawRules();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("通知记录"))
            {
                DrawNotificationRecords();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("掉线监控"))
            {
                if (ImGui.BeginTabBar("##offline_tabs"))
                {
                    if (ImGui.BeginTabItem("掉线监控"))
                    {
                        DrawOfflineMonitorGame();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("在线监控"))
                    {
                        DrawOfflineMonitorOnline();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        if (showOfflineScanWindow)
        {
            DrawOfflineScanWindow();
        }
    }

    private void DrawOfflineScanWindow()
    {
        if (!showOfflineScanWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(900, 640), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("掉线监控扫描结果", ref showOfflineScanWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        var statusInfo = plugin.GetOfflineNodeSnapshotInfo();
        if (!string.IsNullOrWhiteSpace(statusInfo.Info))
        {
            ImGui.TextDisabled(statusInfo.Info);
            ImGui.Separator();
        }

        ImGui.Text("扫描选项");
        if (ImGui.Button("扫描 Addon"))
        {
            plugin.RequestAddonScan();
        }

        ImGui.SameLine();
        if (ImGui.Button("扫描 Node"))
        {
            plugin.RequestOfflineNodeScan();
        }

        ImGui.Separator();
        ImGui.Text("可见 Addon 列表");
        var addonList = plugin.GetVisibleAddonSnapshot();
        var addonHeight = Math.Max(160f, ImGui.GetContentRegionAvail().Y * 0.35f);
        if (ImGui.BeginChild("##offline_addon_list", new Vector2(0f, addonHeight), true))
        {
            foreach (var name in addonList)
            {
                ImGui.TextUnformatted(name);
            }
            ImGui.EndChild();
        }

        ImGui.Separator();
        ImGui.Text("Node 文本快照");
        var nodes = plugin.GetOfflineNodeSnapshot();
        var nodeHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 8f);
        if (ImGui.BeginChild("##offline_node_list", new Vector2(0f, nodeHeight), true))
        {
            foreach (var node in nodes)
            {
                ImGui.TextUnformatted($"{node.NodeId}: {node.Text}");
            }
            ImGui.EndChild();
        }

        ImGui.End();
    }

    private void DrawOfflineMonitorGame()
    {
        ImGui.Text("游戏掉线检测");
        DrawCheckbox("启用掉线监控", plugin.Configuration.EnableOfflineMonitor,
            value => plugin.Configuration.EnableOfflineMonitor = value);
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.CurrentCharacterWorldDisplay);

        var addonName = plugin.Configuration.OfflineAddonName;
        ImGui.Text("Addon 名称");
        ImGui.SetNextItemWidth(420f);
        ImGui.SameLine();
        if (ImGui.Checkbox("可编辑", ref allowEditOfflineAddon))
        {
            if (!allowEditOfflineAddon)
                addonName = plugin.Configuration.OfflineAddonName;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("掉线对话框为Dialogue");
        ImGui.SameLine();
        if (ImGui.Button("Debug"))
        {
            showOfflineScanWindow = true;
        }
        if (!allowEditOfflineAddon)
            ImGui.BeginDisabled();
        if (ImGui.InputTextWithHint("##offlineAddon", "Dialogue", ref addonName, 64))
        {
            plugin.Configuration.OfflineAddonName = addonName;
            SaveConfig();
        }
        if (!allowEditOfflineAddon)
            ImGui.EndDisabled();

        var cooldown = plugin.Configuration.OfflineCooldownSeconds;
        ImGui.Text("触发冷却(秒)");
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("##offlineCooldown", ref cooldown))
        {
            plugin.Configuration.OfflineCooldownSeconds = Math.Clamp(cooldown, 0, 600);
            SaveConfig();
        }

        var alertLimit = plugin.Configuration.OfflineAlertLimit;
        ImGui.Text("警告次数上限");
        ImGui.SetNextItemWidth(120f);
        if (ImGui.InputInt("##offlineAlertLimit", ref alertLimit))
        {
            plugin.Configuration.OfflineAlertLimit = Math.Clamp(alertLimit, 1, 10);
            SaveConfig();
        }

        var title = plugin.Configuration.OfflinePushTitle;
        ImGui.Text("推送标题");
        ImGui.SetNextItemWidth(420f);
        if (ImGui.InputTextWithHint("##offlineTitle", "掉线监控", ref title, 128))
        {
            plugin.Configuration.OfflinePushTitle = title;
            SaveConfig();
        }

        var content = plugin.Configuration.OfflinePushContent;
        ImGui.Text("推送内容");
        ImGui.SetNextItemWidth(560f);
        if (ImGui.InputTextWithHint("##offlineContent", "断开连接", ref content, 256))
        {
            plugin.Configuration.OfflinePushContent = content;
            SaveConfig();
        }

        var status = plugin.GetOfflineNodeSnapshotStatus();
        if (status.Count > 0)
        {
            var age = DateTime.UtcNow - status.TimeUtc;
            ImGui.TextDisabled($"Node快照: {status.Count} 条, {Math.Max(0, (int)age.TotalSeconds)} 秒前");
        }
        else
        {
            ImGui.TextDisabled("Node快照: 0 条");
        }

        ImGui.Text("匹配关键词（任意一条命中）");
        var height = Math.Max(140f, ImGui.GetContentRegionAvail().Y - 8f);
        if (ImGui.BeginChild("##offline_match_list", new Vector2(0f, height), true))
        {
            for (var i = 0; i < plugin.Configuration.OfflineMatchTexts.Count; i++)
            {
                ImGui.PushID(i);
                var text = plugin.Configuration.OfflineMatchTexts[i];
                ImGui.SetNextItemWidth(520f);
                if (ImGui.InputText("##offlineMatch", ref text, 256))
                {
                    plugin.Configuration.OfflineMatchTexts[i] = text;
                    SaveConfig();
                }
                ImGui.SameLine();
                if (ImGui.Button("删除"))
                {
                    plugin.Configuration.OfflineMatchTexts.RemoveAt(i);
                    SaveConfig();
                    ImGui.PopID();
                    break;
                }
                ImGui.PopID();
            }

            if (ImGui.Button("新增关键词"))
            {
                plugin.Configuration.OfflineMatchTexts.Add(string.Empty);
                SaveConfig();
            }

            ImGui.SameLine();
            if (ImGui.Button("添加默认关键词"))
            {
                AddOfflineDefaultKeywords();
            }

            ImGui.EndChild();
        }
    }

    private void DrawOfflineMonitorOnline()
    {
        ImGui.Text("在线监控服务");

        var enableMonitor = plugin.Configuration.EnableMonitor;
        
        var newValue = enableMonitor;
        DrawCheckbox("启用监控客户端", enableMonitor, value => newValue = value);
        
        if (newValue != plugin.Configuration.EnableMonitor)
        {
            plugin.Configuration.EnableMonitor = newValue;
            SaveConfig();
            if (newValue)
            {
                _ = plugin.StartMonitorClient();
            }
            else
            {
                _ = plugin.StopMonitorClient();
            }
        }

        var connectionFailed = !plugin.IsMonitorConnected && enableMonitor && !plugin.Configuration.EnableMonitor;
        
        ImGui.SameLine();
        var statusText = plugin.IsMonitorConnected ? "已连接" : (connectionFailed ? "连接失败" : "未连接");
        ImGui.TextDisabled(statusText);
        ImGui.TextDisabled("连接到本地 TBNMonitor 服务，检测在线状态，离线时发送推送通知。");

        var monitorHost = plugin.Configuration.MonitorHost;
        ImGui.Text("监控主机");
        ImGui.SetNextItemWidth(200f);
        if (DrawInputTextWithHint("##monitorHost", "127.0.0.1", ref monitorHost, 64, value => plugin.Configuration.MonitorHost = value))
        {
            SaveConfig();
        }

        var monitorPort = plugin.Configuration.MonitorPort;
        ImGui.Text("监控端口");
        ImGui.SetNextItemWidth(100f);
        if (ImGui.InputInt("##monitorPort", ref monitorPort))
        {
            plugin.Configuration.MonitorPort = monitorPort;
            SaveConfig();
        }

        var monitorToken = plugin.Configuration.MonitorToken;
        ImGui.Text("客户端标识");
        ImGui.SetNextItemWidth(200f);
        if (DrawInputTextWithHint("##monitorToken", "例如: 1号机", ref monitorToken, 64, value => plugin.Configuration.MonitorToken = value))
        {
            SaveConfig();
        }

        ImGui.Separator();
        ImGui.Text("日志");
        ImGui.SameLine();
        if (ImGui.Button("复制"))
        {
            lock (monitorLogLock)
            {
                var text = string.Join("\n", monitorLogs);
                ImGui.SetClipboardText(text);
            }
        }
        var logHeight = Math.Max(100f, ImGui.GetContentRegionAvail().Y - 8f);
        if (ImGui.BeginChild("##monitor_logs", new Vector2(0f, logHeight), true))
        {
            lock (monitorLogLock)
            {
                foreach (var logMsg in monitorLogs)
                {
                    ImGui.TextUnformatted(logMsg);
                }
            }
            ImGui.SetScrollHereY();
            ImGui.EndChild();
        }
    }
}
