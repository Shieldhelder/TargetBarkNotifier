using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private void DrawOfflineMonitor()
    {
        ImGui.Text("掉线监控");
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
        ImGui.TextDisabled("掉线对话框的Addon为Dialogue");
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
        ImGui.TextDisabled("支持占位符: {name}, {server}");

        var content = plugin.Configuration.OfflinePushContent;
        ImGui.Text("推送内容");
        ImGui.SetNextItemWidth(560f);
        if (ImGui.InputTextWithHint("##offlineContent", "断开连接", ref content, 256))
        {
            plugin.Configuration.OfflinePushContent = content;
            SaveConfig();
        }
        ImGui.TextDisabled("支持占位符: {name}, {server}");

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

        DrawOfflineScanWindow();
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

    private void AddOfflineDefaultKeywords()
    {
        var defaults = new[]
        {
            "失去了与服务器的连接。",
            "已断开连接。",
            "与服务器的连接已中断。"
        };

        foreach (var text in defaults)
        {
            if (!plugin.Configuration.OfflineMatchTexts.Exists(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase)))
                plugin.Configuration.OfflineMatchTexts.Add(text);
        }

        SaveConfig();
    }
}
