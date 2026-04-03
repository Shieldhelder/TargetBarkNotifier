using System;

namespace TargetBarkNotifier;

public sealed class NotificationRecord
{
    public DateTime TimeLocal { get; init; }
    public bool Ignored { get; init; }
    public bool Success { get; init; }
    public string PushProvider { get; init; } = string.Empty;
    public string PushIdentity { get; init; } = string.Empty;
    public string MatchText { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
