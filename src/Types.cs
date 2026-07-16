namespace Enconvert;

/// <summary>Result of a synchronous (or job-polled) file conversion.</summary>
public sealed record ConversionResult
{
    public required string PresignedUrl { get; init; }
    public required string ObjectKey { get; init; }
    public required string Filename { get; init; }
    public int? FileSize { get; init; }
    public double? ConversionTimeSeconds { get; init; }
    public string? JobId { get; init; }
}

/// <summary>Status of an async conversion job: "processing" | "success" | "failed".</summary>
public sealed record JobStatus
{
    public required string Status { get; init; }
    public string? PresignedUrl { get; init; }
    public string? ObjectKey { get; init; }
    public string? Error { get; init; }
}

public sealed record PdfMargins
{
    public double? Top { get; init; }
    public double? Bottom { get; init; }
    public double? Left { get; init; }
    public double? Right { get; init; }
}

/// <summary>Header or footer block rendered on each PDF page.</summary>
public sealed record PdfHeaderFooter
{
    /// <summary>Text content, max 2000 characters.</summary>
    public string? Content { get; init; }

    /// <summary>Block height.</summary>
    public double? Height { get; init; }
}

public sealed record PdfOptions
{
    public string? PageSize { get; init; }

    /// <summary>Custom page width; overrides PageSize when set together with PageHeight.</summary>
    public double? PageWidth { get; init; }

    /// <summary>Custom page height; overrides PageSize when set together with PageWidth.</summary>
    public double? PageHeight { get; init; }

    /// <summary>"portrait" or "landscape".</summary>
    public string? Orientation { get; init; }

    public PdfMargins? Margins { get; init; }
    public double? Scale { get; init; }
    public bool? Grayscale { get; init; }
    public PdfHeaderFooter? Header { get; init; }
    public PdfHeaderFooter? Footer { get; init; }
}

/// <summary>HTTP Basic Auth credentials for pages behind a login (plan-gated).</summary>
public sealed record HttpBasicAuth
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

/// <summary>
/// Cookie injected into the browser context before rendering (plan-gated).
/// The API requires <see cref="Name"/>, <see cref="Value"/>, and either
/// <see cref="Domain"/> or <see cref="Url"/>. When <see cref="Domain"/> is
/// set without <see cref="Path"/>, the API defaults Path to "/".
/// </summary>
public sealed record BrowserCookie
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public string? Domain { get; init; }
    public string? Url { get; init; }
    public string? Path { get; init; }
    public double? Expires { get; init; }
    public bool? HttpOnly { get; init; }
    public bool? Secure { get; init; }

    /// <summary>"Strict" | "Lax" | "None".</summary>
    public string? SameSite { get; init; }
}

/// <summary>
/// File input accepted by ConvertImageAsync / ConvertDocumentAsync when
/// bytes are supplied directly with an explicit filename (and optional
/// content type). Use the string-path or byte[] overloads for the other
/// two input shapes.
/// </summary>
public sealed record FileInput(byte[] Data, string Filename, string? ContentType = null);

/// <summary>Options shared by all URL-based conversions (single page and website).</summary>
public record UrlRenderOptions
{
    public int? ViewportWidth { get; init; }
    public int? ViewportHeight { get; init; }
    public bool? LoadMedia { get; init; }
    public bool? EnableScroll { get; init; }
    public string? OutputFilename { get; init; }

    /// <summary>HTTP Basic Auth for protected pages (plan-gated).</summary>
    public HttpBasicAuth? Auth { get; init; }

    /// <summary>Cookies injected before rendering, max 50 (plan-gated).</summary>
    public IReadOnlyList<BrowserCookie>? Cookies { get; init; }

    /// <summary>Extra request headers, max 20; hop-by-hop headers rejected (plan-gated).</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

public sealed record UrlToPdfOptions : UrlRenderOptions
{
    public string? SaveTo { get; init; }
    public bool? SinglePage { get; init; }
    public PdfOptions? PdfOptions { get; init; }
}

public sealed record UrlToScreenshotOptions : UrlRenderOptions
{
    public string? SaveTo { get; init; }
}

public sealed record UrlToMarkdownOptions : UrlRenderOptions
{
    public string? SaveTo { get; init; }
}

public sealed record ConvertImageOptions
{
    public required string OutputFormat { get; init; }
    public string? SaveTo { get; init; }
    public string? OutputFilename { get; init; }
}

public sealed record ConvertDocumentOptions
{
    public string? OutputFormat { get; init; }
    public string? SaveTo { get; init; }
    public string? OutputFilename { get; init; }
    public PdfOptions? PdfOptions { get; init; }
}

/// <summary>Options for <see cref="EnconvertClient.ConvertToMarkdownAsync(string, ConvertToMarkdownOptions?, CancellationToken)"/> (anything-to-markdown).</summary>
public sealed record ConvertToMarkdownOptions
{
    public string? SaveTo { get; init; }
    public string? OutputFilename { get; init; }
}

/// <summary>Options for <see cref="EnconvertClient.ConvertToPdfAsync(string, ConvertToPdfOptions?, CancellationToken)"/> (anything-to-pdf).</summary>
public sealed record ConvertToPdfOptions
{
    public string? SaveTo { get; init; }
    public string? OutputFilename { get; init; }

    /// <summary>Only <see cref="PdfOptions.Grayscale"/> is honored by the anything-to-pdf endpoint.</summary>
    public PdfOptions? PdfOptions { get; init; }
}

/// <summary>
/// Options shared by ConvertWebsiteToPdfAsync / ConvertWebsiteToScreenshotAsync.
/// Unlike <see cref="UrlRenderOptions"/>, render fields here are only sent
/// when set — the gateway applies its own per-page defaults.
/// </summary>
public record WebsiteConversionOptions : UrlRenderOptions
{
    /// <summary>
    /// URL discovery strategy: "auto" (default, highest mode the plan
    /// allows), "sitemap" (sitemap.xml only), or "full" (sitemap + BFS crawl).
    /// </summary>
    public string? CrawlMode { get; init; }

    /// <summary>Only crawl URLs matching these patterns (full crawl mode).</summary>
    public IReadOnlyList<string>? IncludePatterns { get; init; }

    /// <summary>Skip URLs matching these patterns (full crawl mode).</summary>
    public IReadOnlyList<string>? ExcludePatterns { get; init; }

    /// <summary>Email notified on completion. Defaults to the project owner's email.</summary>
    public string? NotificationEmail { get; init; }

    /// <summary>Webhook POSTed when the batch finishes (plan-gated).</summary>
    public string? CallbackUrl { get; init; }
}

public sealed record WebsiteToPdfOptions : WebsiteConversionOptions
{
    public bool? SinglePage { get; init; }
    public PdfOptions? PdfOptions { get; init; }
}

/// <summary>202 response from an async batch submission (website conversions).</summary>
public sealed record BatchSubmission
{
    public required string BatchId { get; init; }

    /// <summary>Always "processing" on submission.</summary>
    public required string Status { get; init; }

    /// <summary>Number of pages queued for conversion.</summary>
    public int UrlCount { get; init; }

    /// <summary>Total URLs found during discovery (before plan limits applied).</summary>
    public int? TotalDiscovered { get; init; }

    /// <summary>How URLs were discovered: "sitemap" or "full_crawl".</summary>
    public string? DiscoveryMethod { get; init; }

    /// <summary>Output packaging, "zip" for website conversions.</summary>
    public string? OutputFormat { get; init; }
}

/// <summary>Per-URL entry in a batch status response.</summary>
public sealed record BatchItem
{
    public required string SourceUrl { get; init; }

    /// <summary>Raw activity status: "In Progress", "Success", or "Failed".</summary>
    public required string Status { get; init; }

    public string? DownloadUrl { get; init; }
    public int? OutputFileSize { get; init; }
    public string? Duration { get; init; }
}

/// <summary>Status: "processing" | "completed" | "partial" | "failed".</summary>
public sealed record BatchStatus
{
    public required string BatchId { get; init; }
    public required string Status { get; init; }
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int InProgress { get; init; }

    /// <summary>"zip" or "individual".</summary>
    public required string OutputMode { get; init; }

    /// <summary>Presigned URL of the bundled ZIP when OutputMode is "zip".</summary>
    public string? ZipDownloadUrl { get; init; }

    public required IReadOnlyList<BatchItem> Items { get; init; }
}

public sealed record WaitForBatchOptions
{
    /// <summary>Poll interval in milliseconds. Defaults to 5_000.</summary>
    public int? IntervalMs { get; init; }

    /// <summary>Give up after this many milliseconds. Defaults to 1_800_000 (30 minutes).</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Save the batch ZIP to this local path once available.</summary>
    public string? SaveTo { get; init; }
}
