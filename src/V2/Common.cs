namespace Enconvert;

/// <summary>Shared paging options for V2 list endpoints.</summary>
public sealed record V2ListOptions
{
    /// <summary>Rows to skip (default 0).</summary>
    public int? Skip { get; init; }

    /// <summary>Page size, 1-100 (default 20).</summary>
    public int? Limit { get; init; }
}

public sealed record SnapshotListOptions
{
    /// <summary>Page size, 1-100 (default 20).</summary>
    public int? Limit { get; init; }
}

/// <summary>A rendered output stored server-side, addressed by signed URL.</summary>
public sealed record V2OutputArtifact
{
    /// <summary>Pre-signed download URL (15 minutes). Re-signed on every status GET.</summary>
    public string? Url { get; init; }

    public required string ObjectKey { get; init; }
    public long SizeBytes { get; init; }
    public required string ContentType { get; init; }
    public int ExpiresIn { get; init; }
}

public sealed record V2Tokens
{
    public int Input { get; init; }
    public int Output { get; init; }
}
