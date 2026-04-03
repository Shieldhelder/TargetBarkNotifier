using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private void DrawNotificationRecords()
    {
        var records = plugin.GetNotificationRecordsSnapshot();

        ImGui.Text("最近通知记录（最多 30 条）");
        ImGui.SameLine();
        if (ImGui.Button("清空记录"))
        {
            plugin.ClearNotificationRecords();
            return;
        }

        ImGui.SameLine();
        ImGui.Checkbox("成功", ref showRecordSuccess);
        ImGui.SameLine();
        ImGui.Checkbox("失败", ref showRecordFailed);
        ImGui.SameLine();
        ImGui.Checkbox("忽略", ref showRecordIgnored);

        ImGui.Separator();

        var logHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 8f);
        if (!ImGui.BeginChild("##records_scroll", new Vector2(0f, logHeight), true))
        {
            ImGui.EndChild();
            return;
        }

        if (records.Count == 0)
        {
            ImGui.TextDisabled("暂无记录");
            ImGui.EndChild();
            return;
        }

        foreach (var item in records)
        {
            if (item.Ignored && !showRecordIgnored)
                continue;
            if (!item.Ignored && item.Success && !showRecordSuccess)
                continue;
            if (!item.Ignored && !item.Success && !showRecordFailed)
                continue;

            var color = item.Ignored
                ? new Vector4(1f, 0.8f, 0.2f, 1f)
                : item.Success
                    ? new Vector4(0.3f, 0.9f, 0.3f, 1f)
                    : new Vector4(1f, 0.35f, 0.35f, 1f);
            var status = item.Ignored ? "忽略" : item.Success ? "成功" : "失败";
            ImGui.TextColored(color, status);
            ImGui.SameLine();
            ImGui.Text($"{item.TimeLocal:HH:mm:ss} | 匹配: {item.MatchText}");
            ImGui.TextWrapped($"推送方式: {item.PushProvider}");
            ImGui.TextWrapped($"Token/UUID: {item.PushIdentity}");
            ImGui.TextWrapped($"标题: {item.Title}");
            ImGui.TextWrapped($"内容: {item.Content}");
            if (!string.IsNullOrWhiteSpace(item.Detail))
                ImGui.TextWrapped($"详情: {item.Detail}");
            ImGui.Separator();
        }

        ImGui.EndChild();
    }
}
