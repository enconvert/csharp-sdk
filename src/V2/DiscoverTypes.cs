namespace Enconvert;

public sealed record DiscoverOptions
{
    /// <summary>"sitemap" | "crawl" | "hybrid". Default "hybrid" (sitemap + HTTP crawl).</summary>
    public string? Mode { get; init; }

    /// <summary>1-1000, default 100.</summary>
    public int? MaxUrls { get; init; }

    /// <summary>1-5, default 2.</summary>
    public int? MaxDepth { get; init; }

    /// <summary>Regex allowlist (re.search semantics), max 50.</summary>
    public IReadOnlyList<string>? IncludePatterns { get; init; }

    /// <summary>Regex denylist, applied after IncludePatterns, max 50.</summary>
    public IReadOnlyList<string>? ExcludePatterns { get; init; }

    /// <summary>Default true.</summary>
    public bool? SameDomainOnly { get; init; }

    public bool? RespectRobots { get; init; }
}

public sealed record DiscoverResult
{
    public required string Url { get; init; }

    /// <summary>"sitemap" | "crawl" | "hybrid".</summary>
    public required string Mode { get; init; }

    public int Total { get; init; }
    public required IReadOnlyList<string> Urls { get; init; }
    public int PagesCrawled { get; init; }

    /// <summary>True when more URLs were found than MaxUrls allowed.</summary>
    public bool Truncated { get; init; }

    public bool RobotsRespected { get; init; }

    /// <summary>Raw counts per source before dedup, e.g. {sitemap: 42, crawl: 30}.</summary>
    public required IReadOnlyDictionary<string, int> Sources { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
