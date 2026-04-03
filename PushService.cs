using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace TargetBarkNotifier;

public sealed class PushService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const int MaxRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly IPluginLog log;
    private readonly IChatGui chatGui;
    private readonly Configuration configuration;
    private readonly Action<NotificationRecord> recordWriter;
    private readonly Func<string, string> placeholderResolver;
    private bool barkMissingTokenNotified;
    private bool notifyMeMissingUuidNotified;

    public PushService(IPluginLog log, IChatGui chatGui, Configuration configuration, Action<NotificationRecord> recordWriter, Func<string, string> placeholderResolver)
    {
        this.log = log;
        this.chatGui = chatGui;
        this.configuration = configuration;
        this.recordWriter = recordWriter;
        this.placeholderResolver = placeholderResolver;
    }

    public async Task TriggerPushAsync(string title, string content, string ruleTag, string source = "")
    {
        var finalTitle = string.IsNullOrWhiteSpace(title) ? "目标提醒通知" : title.Trim();
        var finalContent = content;
        ApplyPrefix(ref finalTitle, ref finalContent);
        var targets = BuildPushTargets(finalTitle, finalContent);
        if (targets.Count == 0)
            return;

        foreach (var target in targets)
        {
            var result = await SendWithRetryAsync(target, finalTitle, finalContent).ConfigureAwait(false);
            if (result.Success)
            {
                recordWriter(new NotificationRecord
                {
                    TimeLocal = DateTime.Now,
                    Success = true,
                    PushProvider = target.Provider,
                    PushIdentity = target.Identity,
                    MatchText = ruleTag,
                    Title = finalTitle,
                    Content = result.SentContent,
                    Detail = string.IsNullOrWhiteSpace(source) ? "OK" : $"OK | 来源={source}"
                });
                log.Information("Rule {RuleTag} triggered successfully ({Provider}): {Url}", ruleTag, target.Provider, result.LastUrl);
            }
            else
            {
                recordWriter(new NotificationRecord
                {
                    TimeLocal = DateTime.Now,
                    Success = false,
                    PushProvider = target.Provider,
                    PushIdentity = target.Identity,
                    MatchText = ruleTag,
                    Title = finalTitle,
                    Content = result.SentContent,
                    Detail = result.Detail
                });

                if (result.Error != null)
                    log.Warning(result.Error, "Rule {RuleTag} failed ({Provider}): {Url}", ruleTag, target.Provider, result.LastUrl);
                else
                    log.Warning("Rule {RuleTag} failed ({Provider}): {Url} ({Detail})", ruleTag, target.Provider, result.LastUrl, result.Detail);
            }
        }
    }

    private static async Task<SendResult> SendWithRetryAsync(PushTarget target, string title, string content)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var contentWithRetry = attempt == 0 ? content : $"{content}（重连{attempt}次）";
            var url = BuildUrl(target, title, contentWithRetry);

            try
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return SendResult.Ok(contentWithRetry, url);

                var statusCode = (int)response.StatusCode;
                var detail = $"HTTP {statusCode} {response.ReasonPhrase}";
                if (statusCode >= 500 && attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                    continue;
                }

                return new SendResult(false, detail, null, contentWithRetry, url);
            }
            catch (TaskCanceledException ex)
            {
                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                    continue;
                }

                return new SendResult(false, $"请求超时（30秒内未完成，已重试{MaxRetries}次）", ex, contentWithRetry, url);
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500 && attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                    continue;
                }

                return new SendResult(false, ex.Message, ex, contentWithRetry, url);
            }
            catch (Exception ex)
            {
                return new SendResult(false, ex.Message, ex, contentWithRetry, url);
            }
        }

        return new SendResult(false, "未知错误", null, content, string.Empty);
    }

    private static string BuildUrl(PushTarget target, string title, string content)
    {
        return target.Provider switch
        {
            "Bark" => BuildBarkUrl(target.Identity, title, content),
            "NotifyMe" => BuildNotifyMeUrl(target.Identity, title, content),
            _ => string.Empty
        };
    }

    private List<PushTarget> BuildPushTargets(string title, string content)
    {
        var list = new List<PushTarget>();

        if (configuration.EnableBarkPush)
        {
            var token = configuration.BarkToken?.Trim() ?? string.Empty;
            if (token.Length > 0)
            {
                barkMissingTokenNotified = false;
                list.Add(new PushTarget
                {
                    Provider = "Bark",
                    Identity = token
                });
            }
            else if (!barkMissingTokenNotified)
            {
                barkMissingTokenNotified = true;
                chatGui.Print("[TBN] Bark Token为空，未发送消息。");
            }
        }
        else
        {
            barkMissingTokenNotified = false;
        }

        if (configuration.EnableNotifyMePush)
        {
            var uuid = configuration.NotifyMeUuid?.Trim() ?? string.Empty;
            if (uuid.Length > 0)
            {
                notifyMeMissingUuidNotified = false;
                list.Add(new PushTarget
                {
                    Provider = "NotifyMe",
                    Identity = uuid
                });
            }
            else if (!notifyMeMissingUuidNotified)
            {
                notifyMeMissingUuidNotified = true;
                chatGui.Print("[TBN] NotifyMe UUID为空，未发送消息。");
            }
        }
        else
        {
            notifyMeMissingUuidNotified = false;
        }

        return list;
    }

    private void ApplyPrefix(ref string title, ref string content)
    {
        var prefix = configuration.PushPrefix?.Trim() ?? string.Empty;
        prefix = placeholderResolver(prefix);
        if (prefix.Length == 0)
            return;

        var location = configuration.PushPrefixLocation;
        if (location == 1 || location == 3)
        {
            title = prefix + title;
        }
        if (location == 2 || location == 3)
        {
            content = prefix + content;
        }
    }

    private static string BuildBarkUrl(string token, string title, string content)
    {
        var encodedToken = Uri.EscapeDataString(token);
        var encodedTitle = Uri.EscapeDataString(title);
        var encodedContent = Uri.EscapeDataString(content);
        return $"https://api.day.app/{encodedToken}/{encodedTitle}/{encodedContent}";
    }

    private static string BuildNotifyMeUrl(string uuid, string title, string body)
    {
        var encodedUuid = Uri.EscapeDataString(uuid);
        var encodedTitle = Uri.EscapeDataString(title);
        var encodedBody = Uri.EscapeDataString(body);
        return $"https://notifyme-server.wzn556.top/?uuid={encodedUuid}&title={encodedTitle}&body={encodedBody}";
    }

    private sealed class PushTarget
    {
        public string Provider { get; init; } = string.Empty;
        public string Identity { get; init; } = string.Empty;
    }

    private sealed record SendResult(bool Success, string Detail, Exception? Error, string SentContent, string LastUrl)
    {
        public static SendResult Ok(string sentContent, string lastUrl) => new(true, "OK", null, sentContent, lastUrl);
    }
}
