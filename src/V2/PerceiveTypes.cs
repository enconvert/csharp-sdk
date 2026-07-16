using System.Text.Json.Nodes;

namespace Enconvert;

public sealed record PerceiveViewport
{
    /// <summary>320-3840, default 1920.</summary>
    public int? Width { get; init; }

    /// <summary>240-2160, default 1080.</summary>
    public int? Height { get; init; }
}

/// <summary>Per-render options shared by Perceive and PerceiveBatch.</summary>
public record PerceiveOptions
{
    /// <summary>
    /// Artifacts to produce, e.g. "markdown", "markdown_fit", "html_cleaned",
    /// "html_raw", "screenshot", "screenshot_full_page", "pdf", "links",
    /// "images", "structured". Default: ["markdown", "structured"].
    /// </summary>
    public IReadOnlyList<string>? Outputs { get; init; }

    /// <summary>
    /// Heuristic extraction targets, e.g. "tables", "prices", "contacts",
    /// "metadata", "main_content", "headings", "structured_data",
    /// "technologies", "all". Unsupported members yield warnings.
    /// </summary>
    public IReadOnlyList<string>? Extract { get; init; }

    /// <summary>JSON schema for structured extraction (LLM tier, plan-gated).</summary>
    public JsonObject? Schema { get; init; }

    /// <summary>CSS selector (optionally "css:...") or "js:&lt;expr&gt;" to await.</summary>
    public string? WaitFor { get; init; }

    /// <summary>0-60000, default 30000.</summary>
    public int? WaitTimeoutMs { get; init; }

    /// <summary>JavaScript executed after navigation. Max 20000 chars.</summary>
    public string? JsCode { get; init; }

    public PerceiveViewport? Viewport { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public IReadOnlyList<BrowserCookie>? Cookies { get; init; }

    /// <summary>HTTP Basic Auth (plan-gated).</summary>
    public HttpBasicAuth? Auth { get; init; }

    /// <summary>Not yet available server-side — currently rejected with 422.</summary>
    public string? ProxyUrl { get; init; }

    /// <summary>Not yet available server-side — currently rejected with 422.</summary>
    public JsonObject? Geolocation { get; init; }

    /// <summary>Not yet available server-side — currently rejected with 422.</summary>
    public IReadOnlyList<JsonObject>? ActionChain { get; init; }

    /// <summary>Default "enabled" (1h cache). "bypass" skips, "refresh" re-renders.</summary>
    public string? CacheMode { get; init; }

    /// <summary>Only meaningful when Outputs includes "pdf".</summary>
    public PdfOptions? PdfOptions { get; init; }

    /// <summary>Resource types the browser should not load, e.g. "image", "media", "font", "stylesheet", "script".</summary>
    public IReadOnlyList<string>? BlockResources { get; init; }

    public bool? RespectRobots { get; init; }
    public bool? Mobile { get; init; }
}

/// <summary>Options for PerceiveBatch: shared render options plus the output mode.</summary>
public sealed record PerceiveBatchOptions : PerceiveOptions
{
    /// <summary>"manifest" (default) or "zip" (bundle all artifacts once complete).</summary>
    public string? OutputMode { get; init; }
}

public sealed record PerceiveResult
{
    public required string OperationId { get; init; }

    /// <summary>"queued" | "processing" | "completed" | "failed".</summary>
    public required string Status { get; init; }

    public required string Url { get; init; }
    public string? UrlFinal { get; init; }
    public string? ContentHash { get; init; }

    /// <summary>0.0-1.0 render quality score.</summary>
    public double? RenderQuality { get; init; }

    public bool CacheHit { get; init; }

    /// <summary>Keyed by output name (e.g. "markdown", "screenshot_full_page").</summary>
    public required IReadOnlyDictionary<string, V2OutputArtifact> Outputs { get; init; }

    /// <summary>Present when Extract/Schema was requested. Shape is caller-defined.</summary>
    public JsonObject? Structured { get; init; }

    /// <summary>"heuristic" | "css" | "llm".</summary>
    public string? ExtractionTier { get; init; }

    public required V2Tokens Tokens { get; init; }
    public double CostCents { get; init; }
    public int? DurationMs { get; init; }
    public string? Error { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record PerceiveBatchResult
{
    public required string JobId { get; init; }

    /// <summary>"queued" | "processing" | "completed" | "failed" | "partial".</summary>
    public required string Status { get; init; }

    /// <summary>"manifest" or "zip".</summary>
    public required string OutputMode { get; init; }

    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Pending { get; init; }

    /// <summary>Bundle of every successful artifact (OutputMode "zip", once done).</summary>
    public V2OutputArtifact? Zip { get; init; }

    /// <summary>One entry per URL. Empty on the initial 202 — poll GetPerceiveBatchAsync.</summary>
    public required IReadOnlyList<PerceiveResult> Items { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
