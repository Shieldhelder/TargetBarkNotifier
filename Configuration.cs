using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace TargetBarkNotifier;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool HasCompletedFirstSetup { get; set; } = false;

    public bool Enabled { get; set; } = true;
    public bool EnableTts { get; set; } = false;
    public int RepeatCooldownSeconds { get; set; } = 10;
    public bool EnableBarkPush { get; set; } = true;
    public bool EnableNotifyMePush { get; set; } = false;
    public bool EnableServerChan3Push { get; set; } = false;
    public string BarkToken { get; set; } = string.Empty;
    public string NotifyMeUuid { get; set; } = string.Empty;
    public string ServerChan3Key { get; set; } = string.Empty;
    public string PushPrefix { get; set; } = string.Empty;
    public int PushPrefixLocation { get; set; } = 0;
    public bool EnableOfflineMonitor { get; set; } = false;
    public string OfflineAddonName { get; set; } = "Dialogue";
    public List<string> OfflineMatchTexts { get; set; } = [];
    public int OfflineCooldownSeconds { get; set; } = 30;
    public int OfflineAlertLimit { get; set; } = 3;
    public string OfflinePushTitle { get; set; } = "掉线监控";
    public string OfflinePushContent { get; set; } = "断开连接";
    public bool EnableMonitor { get; set; } = false;
    public string MonitorHost { get; set; } = "127.0.0.1";
    public int MonitorPort { get; set; } = 9527;
    public string MonitorToken { get; set; } = string.Empty;
    public List<MatchRule> Rules { get; set; } = [];

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}

[Serializable]
public class MatchRule
{
    public bool Enabled { get; set; } = true;
    public string MatchText { get; set; } = string.Empty;
    public string ExcludeText { get; set; } = string.Empty;
    public string TerritoryId { get; set; } = string.Empty;
    public string MessageTitle { get; set; } = "目标提醒通知";
    public string MessageContent { get; set; } = "检测到目标提示：{message}";
}
