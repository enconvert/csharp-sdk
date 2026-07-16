using System.Text.Json.Nodes;

namespace Enconvert;

public sealed record LookupOptions
{
    /// <summary>"web" | "news" | "images" | "scholar" | "patents" | "maps". Default "web".</summary>
    public string? Category { get; init; }

    /// <summary>Google "gl" country code, e.g. "us", "in".</summary>
    public string? Country { get; init; }

    /// <summary>Google "hl" interface language, e.g. "en".</summary>
    public string? Locale { get; init; }

    /// <summary>"hour" | "day" | "week" | "month" | "year".</summary>
    public string? TimeFilter { get; init; }

    /// <summary>1-100, default 10.</summary>
    public int? NumResults { get; init; }

    /// <summary>1-10, default 1.</summary>
    public int? Page { get; init; }

    /// <summary>Free-text location, e.g. "Austin, Texas".</summary>
    public string? Location { get; init; }

    /// <summary>Default true.</summary>
    public bool? Autocorrect { get; init; }

    /// <summary>
    /// Auto-perceive the top-N result URLs (0-10, default 0). Each consumes
    /// one perceive-quota unit and runs a full browser render.
    /// </summary>
    public int? PerceiveTop { get; init; }
}

/// <summary>One search hit, optionally carrying its full perceive result.</summary>
public sealed record LookupItem
{
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Snippet { get; init; }
    public int? Position { get; init; }
    public string? Source { get; init; }
    public string? Date { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }

    /// <summary>Provider-specific passthrough fields.</summary>
    public required JsonObject Extra { get; init; }

    /// <summary>Present for the top-N results when PerceiveTop &gt; 0 and it succeeded.</summary>
    public PerceiveResult? Perceive { get; init; }
}

public sealed record LookupResult
{
    /// <summary>Audit row id; null when the audit write failed (results still valid).</summary>
    public int? LookupId { get; init; }

    public required string Query { get; init; }

    /// <summary>"web" | "news" | "images" | "scholar" | "patents" | "maps".</summary>
    public required string Category { get; init; }

    public string? Country { get; init; }
    public string? Locale { get; init; }

    /// <summary>"hour" | "day" | "week" | "month" | "year".</summary>
    public string? TimeFilter { get; init; }

    public int Total { get; init; }
    public required IReadOnlyList<LookupItem> Results { get; init; }

    /// <summary>How many results were actually perceived (may be below requested).</summary>
    public int PerceiveTop { get; init; }

    public required IReadOnlyList<string> PerceiveOperationIds { get; init; }
    public JsonObject? AnswerBox { get; init; }
    public JsonObject? KnowledgeGraph { get; init; }

    /// <summary>Search-provider credits consumed.</summary>
    public int? Credits { get; init; }

    public double CostCents { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
