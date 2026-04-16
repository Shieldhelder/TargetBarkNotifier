using Dalamud.Bindings.ImGui;
using System;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private void DrawGeneralSettings()
    {
        ImGui.Text("基础设置");

        DrawCheckbox("启用插件", plugin.Configuration.Enabled, value => plugin.Configuration.Enabled = value);

        ImGui.SameLine();
        DrawCheckbox("启用TTS播报", plugin.Configuration.EnableTts, value => plugin.Configuration.EnableTts = value);
        ImGui.TextDisabled("提示：TTS为系统播报，独立于游戏内音量设置；即使游戏静音也会播报。 ");

        var cooldown = plugin.Configuration.RepeatCooldownSeconds;
        ImGui.Text("重复触发冷却(秒)");
        if (ImGui.SliderInt("##repeatCooldown", ref cooldown, 0, 120))
        {
            plugin.Configuration.RepeatCooldownSeconds = cooldown;
            SaveConfig();
        }

        ImGui.Text("推送方式（可同时开启）");
        DrawCheckbox("Bark 推送", plugin.Configuration.EnableBarkPush, value => plugin.Configuration.EnableBarkPush = value);
        ImGui.SameLine();
        DrawCheckbox("NotifyMe 推送", plugin.Configuration.EnableNotifyMePush, value => plugin.Configuration.EnableNotifyMePush = value);
        ImGui.SameLine();
        DrawCheckbox("Server酱3 推送", plugin.Configuration.EnableServerChan3Push, value => plugin.Configuration.EnableServerChan3Push = value);

        var token = plugin.Configuration.BarkToken;
        ImGui.Text("Bark Token");
        ImGui.SetNextItemWidth(560f);
        DrawInputTextWithHint("##barkToken", "Bark Token", ref token, 256, value => plugin.Configuration.BarkToken = value);

        var uuid = plugin.Configuration.NotifyMeUuid;
        ImGui.Text("NotifyMe UUID");
        ImGui.SetNextItemWidth(560f);
        DrawInputTextWithHint("##notifyMeUuid", "NotifyMe UUID", ref uuid, 256, value => plugin.Configuration.NotifyMeUuid = value);

        var serverChan3Key = plugin.Configuration.ServerChan3Key;
        ImGui.Text("Server酱3 SendKey");
        ImGui.SetNextItemWidth(560f);
        DrawInputTextWithHint("##serverChan3Key", "Server酱3 SendKey (以sctp开头)", ref serverChan3Key, 256, value => plugin.Configuration.ServerChan3Key = value);

        ImGui.Separator();
        ImGui.Text("推送前缀设置");

        var prefix = plugin.Configuration.PushPrefix;
        ImGui.Text("前缀内容");
        ImGui.SetNextItemWidth(400f);
        DrawInputTextWithHint("##pushPrefix", "例如: [1号机]", ref prefix, 64, value => plugin.Configuration.PushPrefix = value);
        ImGui.TextDisabled("支持占位符: {name}, {server}(归属服务器), {currentserver}(当前服务器)");

        var prefixLocation = plugin.Configuration.PushPrefixLocation;
        ImGui.Text("前缀位置");
        ImGui.SetNextItemWidth(400f);
        ImGui.Combo("##prefixLocation", ref prefixLocation, "不添加\0标题\0内容\0标题和内容\0");
        if (ImGui.IsItemEdited())
        {
            plugin.Configuration.PushPrefixLocation = prefixLocation;
            SaveConfig();
        }

        if (ImGui.Button("发送测试推送"))
        {
            plugin.TriggerTestPush();
        }

    }
}
