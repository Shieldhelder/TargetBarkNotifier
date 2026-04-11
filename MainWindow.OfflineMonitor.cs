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

        ImGui.Separator();
        ImGui.Text("在线监控服务");

        DrawCheckbox("启用监控客户端", plugin.Configuration.EnableMonitor, value =>
        {
            plugin.Configuration.EnableMonitor = value;
            _ = plugin.StartMonitorClient();
        });
        ImGui.SameLine();
        ImGui.TextDisabled(plugin.IsMonitorConnected ? "已连接" : "未连接");
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
