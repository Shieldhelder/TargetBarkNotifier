using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace TargetBarkNotifier;

public sealed partial class MainWindow
{
    private void DrawRules()
    {
        ImGui.TextDisabled("匹配标靶：[channel]Sender:Message. 支持 | (OR) 与 + (AND)，例如 123+456|队长点名");
        ImGui.TextDisabled("排除消息支持 | 分隔，命中任一排除词将不触发");
        ImGui.TextDisabled("匹配关键词支持占位符：{name}、{server}");
        ImGui.TextDisabled("推送内容可自定义，支持占位符：{channel}、{sender}、{message}、{name}、{server}、{currentserver}");

        if (ImGui.Button("新增规则"))
        {
            showAddRulePopup = true;
            ImGui.OpenPopup("新增规则设置");
        }

        ImGui.SameLine();
        if (ImGui.Button("新增示例"))
        {
            plugin.Configuration.Rules.Add(new MatchRule
            {
                Enabled = true,
                MatchText = "有人正在以你为目标",
                ExcludeText = string.Empty,
                TerritoryId = string.Empty,
                MessageTitle = "目标提醒通知",
                MessageContent = "检测到目标提示：{message}"
            });
            SaveConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("删除选中") && selectedRuleIndex >= 0 && selectedRuleIndex < plugin.Configuration.Rules.Count)
        {
            plugin.Configuration.Rules.RemoveAt(selectedRuleIndex);
            selectedRuleIndex = Math.Min(selectedRuleIndex, plugin.Configuration.Rules.Count - 1);
            SaveConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("Debug"))
        {
            showRulesDebugWindow = true;
        }

        DrawAddRulePopup();

        if (ImGui.Button("导出规则"))
        {
            if (!rulesFileDialogPending)
            {
                rulesFileDialogPending = true;
                ioStatus = "正在打开导出文件选择框...";
                StartSaveJsonFileDialogAsync("导出规则", importExportPath, "rules.json", picked =>
                {
                    rulesFileDialogPending = false;
                    if (string.IsNullOrWhiteSpace(picked))
                    {
                        ioStatus = "已取消导出操作";
                    }
                    else
                    {
                        importExportPath = picked;
                        ExportRules();
                    }
                });
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("导入规则"))
        {
            if (!rulesFileDialogPending)
            {
                rulesFileDialogPending = true;
                ioStatus = "正在打开导入文件选择框...";
                StartOpenJsonFileDialogAsync("导入规则", importExportPath, picked =>
                {
                    rulesFileDialogPending = false;
                    if (string.IsNullOrWhiteSpace(picked))
                    {
                        ioStatus = "已取消导入操作";
                    }
                    else
                    {
                        importExportPath = picked;
                        ImportRules();
                    }
                });
            }
        }
        if (!string.IsNullOrWhiteSpace(ioStatus))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(ioStatus);
        }

        ImGui.Separator();
        var rulesHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 8f);
        if (ImGui.BeginChild("##rules_scroll", new Vector2(0f, rulesHeight), true))
        {
            if (ImGui.BeginTable("##rules_table", 3,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("匹配", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("标题", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("内容", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                for (var i = 0; i < plugin.Configuration.Rules.Count; i++)
                {
                    var rule = plugin.Configuration.Rules[i];
                    ImGui.PushID(i);

                    var matchDisplay = $"{(rule.Enabled ? "" : "[规则关] ")}{rule.MatchText}";

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    var selected = selectedRuleIndex == i;
                    if (ImGui.Selectable(matchDisplay, selected, ImGuiSelectableFlags.AllowItemOverlap))
                    {
                        selectedRuleIndex = i;
                    }

                    if (ImGui.BeginPopupContextItem("##rule_context"))
                    {
                        var enabled = rule.Enabled;
                        if (ImGui.Checkbox("启用规则", ref enabled))
                        {
                            rule.Enabled = enabled;
                            SaveConfig();
                        }

                        var matchText = rule.MatchText;
                        ImGui.Text("匹配的消息（支持 | 或 +）");
                        ImGui.SetNextItemWidth(480f);
                        if (ImGui.InputText("##matchText", ref matchText, 512))
                        {
                            rule.MatchText = matchText;
                            SaveConfig();
                        }

                        var territoryId = rule.TerritoryId;
                        ImGui.Text("区域 ID（留空=全部区域）");
                        ImGui.SetNextItemWidth(240f);
                        if (ImGui.InputText("##territoryId", ref territoryId, 16))
                        {
                            rule.TerritoryId = territoryId.Trim();
                            SaveConfig();
                        }

                        var excludeText = rule.ExcludeText;
                        ImGui.Text("排除的消息（支持 | 分隔）");
                        ImGui.SetNextItemWidth(480f);
                        if (ImGui.InputText("##excludeText", ref excludeText, 512))
                        {
                            rule.ExcludeText = excludeText;
                            SaveConfig();
                        }

                        var messageTitle = rule.MessageTitle;
                        ImGui.Text("消息标题");
                        ImGui.SetNextItemWidth(480f);
                        if (ImGui.InputText("##messageTitle", ref messageTitle, 256))
                        {
                            rule.MessageTitle = messageTitle;
                            SaveConfig();
                        }
                        ImGui.TextDisabled("支持占位符: {channel}, {sender}, {message}, {name}, {server}, {currentserver}");

                        var messageContent = rule.MessageContent;
                        ImGui.Text("消息内容");
                        ImGui.SetNextItemWidth(480f);
                        if (ImGui.InputText("##messageContent", ref messageContent, 512))
                        {
                            rule.MessageContent = messageContent;
                            SaveConfig();
                        }
                        ImGui.TextDisabled("支持占位符: {channel}, {sender}, {message}, {name}, {server}, {currentserver}");

                        ImGui.Separator();
                        if (ImGui.Button("删除此规则"))
                        {
                            plugin.Configuration.Rules.RemoveAt(i);
                            selectedRuleIndex = Math.Min(selectedRuleIndex, plugin.Configuration.Rules.Count - 1);
                            SaveConfig();
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.TableSetColumnIndex(1);
                    ImGui.TextUnformatted(rule.MessageTitle ?? string.Empty);

                    ImGui.TableSetColumnIndex(2);
                    ImGui.TextUnformatted(rule.MessageContent ?? string.Empty);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }
        ImGui.EndChild();

        DrawRulesDebugWindow();
    }

    private void DrawAddRulePopup()
    {
        if (!showAddRulePopup)
            return;

        if (ImGui.BeginPopup("新增规则设置"))
        {
            ImGui.Text("请先填写规则配置，再确认新增。");

            ImGui.Text("匹配的消息（支持 | 或 +）");
            ImGui.SetNextItemWidth(420f);
            ImGui.InputText("##newRuleMatch", ref newRuleMatchText, 512);

            ImGui.Text("消息标题");
            ImGui.SetNextItemWidth(420f);
            ImGui.InputText("##newRuleTitle", ref newRuleTitle, 256);
            ImGui.TextDisabled("支持占位符: {channel}, {sender}, {message}, {name}, {server}, {currentserver}");

            ImGui.Text("消息内容");
            ImGui.SetNextItemWidth(420f);
            ImGui.InputText("##newRuleContent", ref newRuleContent, 512);
            ImGui.TextDisabled("支持占位符: {channel}, {sender}, {message}, {name}, {server}, {currentserver}");

            if (ImGui.Button("确认新增"))
            {
                plugin.Configuration.Rules.Add(new MatchRule
                {
                    Enabled = true,
                    MatchText = newRuleMatchText ?? string.Empty,
                    ExcludeText = string.Empty,
                    TerritoryId = string.Empty,
                    MessageTitle = string.IsNullOrWhiteSpace(newRuleTitle) ? "目标提醒通知" : newRuleTitle.Trim(),
                    MessageContent = newRuleContent ?? string.Empty
                });
                SaveConfig();

                selectedRuleIndex = plugin.Configuration.Rules.Count - 1;
                showAddRulePopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                showAddRulePopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void ExportRules()
    {
        try
        {
            var path = (importExportPath ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                ioStatus = "导出失败：路径为空";
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(plugin.Configuration.Rules, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
            ioStatus = $"已导出 {plugin.Configuration.Rules.Count} 条规则至 {path}";
        }
        catch (Exception ex)
        {
            ioStatus = $"导出失败: {ex.Message}";
        }
    }

    private void ImportRules()
    {
        try
        {
            var path = (importExportPath ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                ioStatus = "导入失败：路径为空";
                return;
            }

            if (!File.Exists(path))
            {
                ioStatus = $"导入失败：文件不存在 {path}";
                return;
            }

            var json = File.ReadAllText(path);
            var rules = JsonSerializer.Deserialize<List<MatchRule>>(json);
            if (rules is null)
            {
                ioStatus = "导入失败：文件内容为空或格式错误";
                return;
            }

            plugin.Configuration.Rules = rules;
            SaveConfig();
            selectedRuleIndex = plugin.Configuration.Rules.Count > 0 ? 0 : -1;
            ioStatus = $"已导入 {plugin.Configuration.Rules.Count} 条规则自 {path}";
        }
        catch (Exception ex)
        {
            ioStatus = $"导入失败: {ex.Message}";
        }
    }

    private void DrawRulesDebugWindow()
    {
        if (!showRulesDebugWindow)
            return;

        ImGui.SetNextWindowSize(new Vector2(900, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("规则设置 Debug", ref showRulesDebugWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.Text($"当前 MapID: {plugin.CurrentTerritoryId}");
        ImGui.Text($"当前角色与服务器: {plugin.CurrentCharacterWorldDisplay}");

        ImGui.Separator();
        ImGui.Text("规则调试（匹配表达式拆分）");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##rulesDebugMatchInput", "例如: 123+456|队长点名", ref rulesDebugMatchInput, 512);
        if (ImGui.Button("拆分规则"))
        {
            var groups = Plugin.ParseRuleMatchExpression(rulesDebugMatchInput);
            if (groups.Count == 0)
            {
                rulesDebugParseOutput = "无有效条件";
            }
            else
            {
                var sb = new StringBuilder();
                for (var i = 0; i < groups.Count; i++)
                {
                    sb.Append($"OR组{i + 1}: ");
                    sb.Append(string.Join(" AND ", groups[i]));
                    if (i < groups.Count - 1)
                        sb.Append('\n');
                }
                rulesDebugParseOutput = sb.ToString();
            }
        }
        if (!string.IsNullOrWhiteSpace(rulesDebugParseOutput))
            ImGui.TextWrapped(rulesDebugParseOutput);

        ImGui.Separator();
        ImGui.Text("推送调试（输入一条消息，预览会触发的规则及推送内容）");
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##rulesDebugChannel", "频道，如 tellincoming", ref rulesDebugChannelInput, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint("##rulesDebugSender", "发送者", ref rulesDebugSenderInput, 128);
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextMultiline("##rulesDebugMessage", ref rulesDebugMessageInput, 2048, new Vector2(-1f, 90f));

        if (ImGui.Button("预览推送"))
        {
            var previews = plugin.BuildRuleDebugPreviews(rulesDebugChannelInput, rulesDebugSenderInput, rulesDebugMessageInput);
            if (previews.Count == 0)
            {
                rulesDebugPreviewOutput = "未命中任何启用规则（或区域不匹配）";
            }
            else
            {
                var sb = new StringBuilder();
                for (var i = 0; i < previews.Count; i++)
                {
                    var item = previews[i];
                    sb.Append($"[{i + 1}] 规则: {item.MatchText}\n");
                    sb.Append($"标题: {item.Title}\n");
                    sb.Append($"内容: {item.Content}");
                    if (i < previews.Count - 1)
                        sb.Append("\n----------------\n");
                }

                rulesDebugPreviewOutput = sb.ToString();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("复制预览") && !string.IsNullOrWhiteSpace(rulesDebugPreviewOutput))
        {
            ImGui.SetClipboardText(rulesDebugPreviewOutput);
        }

        var height = Math.Max(180f, ImGui.GetContentRegionAvail().Y - 8f);
        if (ImGui.BeginChild("##rulesDebugPreview", new Vector2(0f, height), true))
        {
            if (string.IsNullOrWhiteSpace(rulesDebugPreviewOutput))
                ImGui.TextDisabled("暂无预览结果");
            else
                ImGui.TextUnformatted(rulesDebugPreviewOutput);
            ImGui.EndChild();
        }

        ImGui.End();
    }
}
