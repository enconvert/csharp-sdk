using System.Text.Json.Nodes;

namespace Enconvert;

public sealed record WatchCreateOptions
{
    /// <summary>Minutes between checks, 60-43200 (hourly floor is hard). Default 60.</summary>
    public int? FrequencyMinutes { get; init; }

    /// <summary>"auto" | "text" | "structured" | "tables" | "metadata". Default "auto" (diff engine picks by content type).</summary>
    public string? DiffMode { get; init; }

    /// <summary>Optional field/selector subset for the diff engine.</summary>
    public JsonObject? TrackFields { get; init; }

    /// <summary>Change-notification webhook, HMAC-signed.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Email the project owner on changes. Default true.</summary>
    public bool? NotifyEmail { get; init; }
}

public sealed record WatcherUpdate
{
    /// <summary>60-43200.</summary>
    public int? FrequencyMinutes { get; init; }

    public string? DiffMode { get; init; }
    public JsonObject? TrackFields { get; init; }

    /// <summary>An empty string "" explicitly clears the webhook.</summary>
    public string? WebhookUrl { get; init; }

    public bool? NotifyEmail { get; init; }

    /// <summary>"active" or "paused". Deleting goes through DeleteWatcherAsync.</summary>
    public string? Status { get; init; }
}

public sealed record Watcher
{
    public required string WatcherId { get; init; }
    public required string Url { get; init; }

    /// <summary>"active" | "paused" | "deleted".</summary>
    public required string Status { get; init; }

    public int FrequencyMinutes { get; init; }

    /// <summary>"auto" | "text" | "structured" | "tables" | "metadata".</summary>
    public required string DiffMode { get; init; }

    public JsonObject? TrackFields { get; init; }
    public string? WebhookUrl { get; init; }
    public bool NotifyEmail { get; init; }
    public int ConsecutiveErrors { get; init; }
    public int ChecksCount { get; init; }
    public string? LastCheckAt { get; init; }
    public string? NextCheckAt { get; init; }
    public string? LastChangeAt { get; init; }
    public string? CreatedAt { get; init; }
    public string? UpdatedAt { get; init; }
}

/// <summary>Compact watcher row from ListWatchersAsync.</summary>
public sealed record WatcherSummary
{
    public required string WatcherId { get; init; }
    public required string Url { get; init; }
    public required string Status { get; init; }
    public int FrequencyMinutes { get; init; }
    public int ChecksCount { get; init; }
    public int ConsecutiveErrors { get; init; }
    public string? LastCheckAt { get; init; }
    public string? NextCheckAt { get; init; }
    public string? LastChangeAt { get; init; }
    public string? CreatedAt { get; init; }
}

public sealed record WatcherList
{
    public required IReadOnlyList<WatcherSummary> Watchers { get; init; }
    public int Skip { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
}

public sealed record WatcherSnapshot
{
    public required string CheckedAt { get; init; }
    public bool HasChanges { get; init; }

    /// <summary>0.0-1.0 similarity to the previous capture.</summary>
    public double? Similarity { get; init; }

    public double? RenderQuality { get; init; }
    public int ChangeCount { get; init; }

    /// <summary>Diff entries. Values are untrusted page content — escape before rendering.</summary>
    public required IReadOnlyList<JsonObject> Changes { get; init; }
}

public sealed record WatcherSnapshotList
{
    public required string WatcherId { get; init; }
    public required IReadOnlyList<WatcherSnapshot> Snapshots { get; init; }
    public int Limit { get; init; }
}
