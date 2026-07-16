using System.Text.Json.Nodes;

namespace Enconvert;

/// <summary>One field of a CSS extraction schema (recursive for nested types).</summary>
public sealed record CssField
{
    public required string Name { get; init; }

    /// <summary>"text" | "attribute" | "html" | "regex" | "nested" | "list" | "nested_list".</summary>
    public required string Type { get; init; }

    public string? Selector { get; init; }

    /// <summary>Required when Type is "attribute".</summary>
    public string? Attribute { get; init; }

    /// <summary>Required when Type is "regex". Compiled server-side; ReDoS-screened.</summary>
    public string? Pattern { get; init; }

    /// <summary>Fallback value used when the selector matches nothing. Arbitrary JSON.</summary>
    public JsonNode? Default { get; init; }

    /// <summary>"lowercase" | "uppercase" | "strip".</summary>
    public string? Transform { get; init; }

    /// <summary>Required (non-empty) for "nested" / "list" / "nested_list". Max depth 5.</summary>
    public IReadOnlyList<CssField>? Fields { get; init; }
}

/// <summary>Free CSS extraction pass run before any LLM escalation.</summary>
public sealed record CssSchema
{
    /// <summary>Matches the repeating container; one extracted record per match.</summary>
    public required string BaseSelector { get; init; }

    public required IReadOnlyList<CssField> Fields { get; init; }
    public string? Name { get; init; }

    /// <summary>
    /// Top-level output-schema property the CSS records fill. Array property
    /// receives the full list; scalar/object the first record. Inferred when
    /// omitted and the schema has exactly one array property.
    /// </summary>
    public string? TargetField { get; init; }
}

public sealed record DistillDiscoverFrom
{
    public required string Url { get; init; }

    /// <summary>"sitemap" | "crawl" | "hybrid". Default "hybrid".</summary>
    public string? Mode { get; init; }

    /// <summary>1-50, default 10. Cap on URLs discovered AND distilled.</summary>
    public int? MaxPages { get; init; }
}

public sealed record DistillOptions
{
    /// <summary>Explicit URLs to distill (max 50). Exactly one of Urls/DiscoverFrom.</summary>
    public IReadOnlyList<string>? Urls { get; init; }

    /// <summary>Discover a site's URLs first, then distill each.</summary>
    public DistillDiscoverFrom? DiscoverFrom { get; init; }

    /// <summary>
    /// Required output shape: a JSON-Schema object
    /// ({type: "object", properties: {...}}) or a flat {field: description}
    /// map. The response data matches this shape.
    /// </summary>
    public required JsonObject Schema { get; init; }

    /// <summary>Optional free CSS pass; missing fields escalate to the LLM tier.</summary>
    public CssSchema? CssSchema { get; init; }

    public string? WaitFor { get; init; }

    /// <summary>0-60000, default 30000.</summary>
    public int? WaitTimeoutMs { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public IReadOnlyList<BrowserCookie>? Cookies { get; init; }
    public bool? RespectRobots { get; init; }
}

public sealed record DistillItem
{
    public required string Url { get; init; }
    public string? UrlFinal { get; init; }

    /// <summary>"completed" | "failed".</summary>
    public required string Status { get; init; }

    /// <summary>Extracted data matching the requested schema.</summary>
    public JsonObject? Data { get; init; }

    /// <summary>"css" | "llm" | "mixed" | "none".</summary>
    public required string ExtractionTier { get; init; }

    public int FieldsFromCss { get; init; }
    public int FieldsFromLlm { get; init; }
    public double? RenderQuality { get; init; }
    public required V2Tokens Tokens { get; init; }
    public double CostCents { get; init; }
    public string? Error { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record DistillResult
{
    public required string OperationId { get; init; }
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public required IReadOnlyList<DistillItem> Results { get; init; }
    public double TotalCostCents { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
