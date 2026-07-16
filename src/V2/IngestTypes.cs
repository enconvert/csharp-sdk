namespace Enconvert;

public sealed record IngestChunkOptions
{
    /// <summary>Words per chunk, 32-4000, default 512.</summary>
    public int? MaxWords { get; init; }

    /// <summary>Sentences repeated between consecutive chunks, 0-10, default 1.</summary>
    public int? SentenceOverlap { get; init; }
}

public sealed record IngestOptions
{
    /// <summary>"urls" | "sitemap" | "crawl". Default "urls".</summary>
    public string? Mode { get; init; }

    /// <summary>Seed URL — required for "sitemap"/"crawl", forbidden for "urls".</summary>
    public string? Url { get; init; }

    /// <summary>Explicit URLs (max 1000) — required for "urls", forbidden otherwise.</summary>
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>Discovery cap for sitemap/crawl, 1-1000, default 50.</summary>
    public int? MaxPages { get; init; }

    /// <summary>1-5, default 2.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Default true.</summary>
    public bool? SameDomainOnly { get; init; }

    public IReadOnlyList<string>? IncludePatterns { get; init; }
    public IReadOnlyList<string>? ExcludePatterns { get; init; }
    public bool? RespectRobots { get; init; }
    public string? WaitFor { get; init; }
    public int? WaitTimeoutMs { get; init; }
    public IngestChunkOptions? Chunk { get; init; }

    /// <summary>Completion webhook, HMAC-signed (see GetWebhookSecretAsync).</summary>
    public string? WebhookUrl { get; init; }
}

/// <summary>
/// Options for <see cref="EnconvertV2.IngestFilesAsync"/> (POST
/// /v2/ingest/files). Uploaded documents are converted to Markdown and
/// chunked through the same pipeline as <see cref="EnconvertV2.IngestAsync"/>.
/// </summary>
public sealed record IngestFilesOptions
{
    /// <summary>Heading-aware chunker parameters.</summary>
    public IngestChunkOptions? Chunk { get; init; }

    /// <summary>Completion webhook, HMAC-signed (see GetWebhookSecretAsync).</summary>
    public string? WebhookUrl { get; init; }
}

public sealed record IngestJob
{
    public required string JobId { get; init; }

    /// <summary>"queued" | "discovering" | "processing" | "completed" | "failed" | "canceled".</summary>
    public required string Status { get; init; }

    /// <summary>"urls" | "sitemap" | "crawl" | "files".</summary>
    public required string Mode { get; init; }

    public int PagesDiscovered { get; init; }
    public int PagesProcessed { get; init; }
    public int PagesFailed { get; init; }
    public int TotalChunks { get; init; }

    /// <summary>Signed URL to the final JSONL, once completed.</summary>
    public string? OutputUrl { get; init; }

    public string? ErrorMessage { get; init; }
    public string? WebhookUrl { get; init; }
    public bool WebhookDelivered { get; init; }
    public string? CreatedAt { get; init; }
    public string? CompletedAt { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Compact job row from ListIngestJobsAsync (WebhookUrl replaced by a flag).</summary>
public sealed record IngestJobSummary
{
    public required string JobId { get; init; }
    public required string Status { get; init; }
    public required string Mode { get; init; }
    public int PagesDiscovered { get; init; }
    public int PagesProcessed { get; init; }
    public int PagesFailed { get; init; }
    public int TotalChunks { get; init; }
    public string? OutputUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public bool WebhookConfigured { get; init; }
    public bool WebhookDelivered { get; init; }
    public string? CreatedAt { get; init; }
    public string? CompletedAt { get; init; }
}

public sealed record IngestJobList
{
    public required IReadOnlyList<IngestJobSummary> Jobs { get; init; }
    public int Skip { get; init; }
    public int Limit { get; init; }
    public bool HasMore { get; init; }
}

public sealed record WebhookSecret
{
    public required string Secret { get; init; }

    /// <summary>Header carrying the HMAC signature, e.g. "X-Enconvert-Signature".</summary>
    public required string SignatureHeader { get; init; }

    public required string TimestampHeader { get; init; }
    public required string SignatureScheme { get; init; }
    public int ReplayToleranceSeconds { get; init; }

    /// <summary>True when this response just replaced the previous secret.</summary>
    public bool Rotated { get; init; }
}

public sealed record WebhookRetryResult
{
    public required string JobId { get; init; }
    public bool Delivered { get; init; }
    public int Attempts { get; init; }

    /// <summary>HTTP status of the last attempt; null on network error.</summary>
    public int? StatusCode { get; init; }

    public required string Detail { get; init; }
}
