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

    public MainWindow(Plugin plugin)
        : base("TargetBarkNotifier", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(860, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        importExportPath = Path.Combine(plugin.PluginInterface.GetPluginConfigDirectory(), "rules.json");
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
                DrawOfflineMonitor();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }
}
