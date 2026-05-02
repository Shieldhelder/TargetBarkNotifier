using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private void DrawIdleDetect()
    {
    }

    private void DrawComponentDiffDetect()
    {
        ImGui.Text("分量差检测");
        ImGui.TextDisabled("检测角色坐标各分量差 (X-X₀, Y-Y₀, Z-Z₀) 是否低于阈值");

        DrawCheckbox("启用分量差检测", plugin.Configuration.EnableComponentDiffDetect,
            value => {
                plugin.Configuration.EnableComponentDiffDetect = value;
                if (value) plugin.ResetIdleState();
            });

        if (plugin.Configuration.EnableComponentDiffDetect)
        {
            ImGui.Spacing();
            var timeout = plugin.Configuration.ComponentDiffTimeoutSeconds;
            ImGui.Text("静止超时(秒)");
            ImGui.SetNextItemWidth(150f);
            if (ImGui.SliderInt("##compTimeout", ref timeout, 10, 600))
            {
                plugin.Configuration.ComponentDiffTimeoutSeconds = timeout;
                plugin.ResetIdleState();
                SaveConfig();
            }

            var threshold = plugin.Configuration.ComponentDiffThreshold;
            ImGui.Text("分量偏差阈值");
            ImGui.SetNextItemWidth(150f);
            if (ImGui.SliderFloat("##compThreshold", ref threshold, 0.1f, 5f))
            {
                plugin.Configuration.ComponentDiffThreshold = threshold;
                plugin.ResetIdleState();
                SaveConfig();
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "推送设置");

            var title = plugin.Configuration.ComponentDiffPushTitle;
            ImGui.Text("推送标题");
            ImGui.SetNextItemWidth(420f);
            if (ImGui.InputTextWithHint("##compPushTitle", "分量静止检测", ref title, 128))
            {
                plugin.Configuration.ComponentDiffPushTitle = title;
                SaveConfig();
            }

            var content = plugin.Configuration.ComponentDiffPushContent;
            ImGui.Text("推送内容");
            ImGui.SetNextItemWidth(560f);
            if (ImGui.InputTextWithHint("##compPushContent", "角色已静止 {stuck} 秒！", ref content, 256))
            {
                plugin.Configuration.ComponentDiffPushContent = content;
                SaveConfig();
            }
            ImGui.TextDisabled("支持占位符: {stuck}(静止秒数), {name}(角色名), {server}(归属服务器)");
        }
    }

    private void DrawEuclideanDiffDetect()
    {
        ImGui.Text("几何差检测");
        ImGui.TextDisabled("检测角色几何距离 √(ΔX²+ΔY²+ΔZ²) 是否低于阈值");

        DrawCheckbox("启用几何差检测", plugin.Configuration.EnableEuclideanDiffDetect,
            value => {
                plugin.Configuration.EnableEuclideanDiffDetect = value;
                if (value) plugin.ResetEuclideanState();
            });

        if (plugin.Configuration.EnableEuclideanDiffDetect)
        {
            ImGui.Spacing();
            var timeout = plugin.Configuration.EuclideanDiffTimeoutSeconds;
            ImGui.Text("静止超时(秒)");
            ImGui.SetNextItemWidth(150f);
            if (ImGui.SliderInt("##eucTimeout", ref timeout, 10, 600))
            {
                plugin.Configuration.EuclideanDiffTimeoutSeconds = timeout;
                plugin.ResetEuclideanState();
                SaveConfig();
            }

            var threshold = plugin.Configuration.EuclideanDiffThreshold;
            ImGui.Text("几何差阈值");
            ImGui.SetNextItemWidth(150f);
            if (ImGui.SliderFloat("##eucThreshold", ref threshold, 0.1f, 5f))
            {
                plugin.Configuration.EuclideanDiffThreshold = threshold;
                plugin.ResetEuclideanState();
                SaveConfig();
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "推送设置");

            var title = plugin.Configuration.EuclideanDiffPushTitle;
            ImGui.Text("推送标题");
            ImGui.SetNextItemWidth(420f);
            if (ImGui.InputTextWithHint("##eucPushTitle", "几何静止检测", ref title, 128))
            {
                plugin.Configuration.EuclideanDiffPushTitle = title;
                SaveConfig();
            }

            var content = plugin.Configuration.EuclideanDiffPushContent;
            ImGui.Text("推送内容");
            ImGui.SetNextItemWidth(560f);
            if (ImGui.InputTextWithHint("##eucPushContent", "角色已静止 {stuck} 秒！", ref content, 256))
            {
                plugin.Configuration.EuclideanDiffPushContent = content;
                SaveConfig();
            }
            ImGui.TextDisabled("支持占位符: {stuck}(静止秒数), {name}(角色名), {server}(归属服务器)");
        }
    }

    private void DrawIdleDetectDebugWindow()
    {
        if (!showIdleDetectDebugWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(550, 450), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("静止检测调试", ref showIdleDetectDebugWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.Text($"角色信息: {plugin.CurrentCharacterWorldDisplay}");
        ImGui.Text($"当前坐标: X={plugin.IdleCurrentPosX:F2}, Y={plugin.IdleCurrentPosY:F2}, Z={plugin.IdleCurrentPosZ:F2}");

        ImGui.Spacing();
        if (plugin.Configuration.EnableComponentDiffDetect)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "分量差检测");
            ImGui.Text($"基准坐标: X={plugin.IdleLastPosX:F2}, Y={plugin.IdleLastPosY:F2}, Z={plugin.IdleLastPosZ:F2}");
            ImGui.Text($"坐标偏移: ΔX={plugin.IdleDeltaX:F4}, ΔY={plugin.IdleDeltaY:F4}, ΔZ={plugin.IdleDeltaZ:F4}");
            ImGui.Text($"分量偏差阈值: {plugin.Configuration.ComponentDiffThreshold:F2}");
            ImGui.Text($"静止超时: {plugin.Configuration.ComponentDiffTimeoutSeconds}秒");
            ImGui.Text($"当前静止秒数: {plugin.IdleStuckSeconds}");
            ImGui.Spacing();
        }

        if (plugin.Configuration.EnableEuclideanDiffDetect)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "几何差检测");
            ImGui.Text($"基准坐标: X={plugin.EuclideanLastPosX:F2}, Y={plugin.EuclideanLastPosY:F2}, Z={plugin.EuclideanLastPosZ:F2}");
            ImGui.Text($"几何距离: {plugin.EuclideanDistance:F4}");
            ImGui.Text($"几何差阈值: {plugin.Configuration.EuclideanDiffThreshold:F2}");
            ImGui.Text($"静止超时: {plugin.Configuration.EuclideanDiffTimeoutSeconds}秒");
            ImGui.Text($"当前静止秒数: {plugin.EuclideanDiffStuckSeconds}");
            ImGui.Spacing();
        }

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), "控制");
        if (ImGui.Button("重置分量差"))
        {
            plugin.ResetIdleState();
        }
        ImGui.SameLine();
        if (ImGui.Button("重置几何差"))
        {
            plugin.ResetEuclideanState();
        }
        ImGui.SameLine();
        if (ImGui.Button("全部重置"))
        {
            plugin.ResetIdleState();
            plugin.ResetEuclideanState();
        }

        ImGui.End();
    }
}