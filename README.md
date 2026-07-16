# Enconvert C# / .NET SDK

Honest eyes for your AI agent — the C# / .NET SDK for [Enconvert](https://enconvert.com). Targets .NET 8+.

Read any web page or file into clean Markdown, JSON, or screenshots, and get a `render_quality` score (0.0–1.0) on **every** read — so a blocked, challenge, or empty-SPA page comes back flagged with a low score and warnings, never mistaken for real content. Perceive, discover, look up, distill, ingest, and watch the web; convert 40+ file and document formats through the same key.

> Wiring an agent (Claude, Cursor, Windsurf, n8n, …)? The [MCP server](https://enconvert.com/mcp) is the native path — `npx @enconvert/mcp setup`. This SDK is the programmatic REST path for everything else.

## Install

```bash
dotnet add package Enconvert
```

## Quick Start

```csharp
using Enconvert;

var client = new EnconvertClient("sk_...");

// Read a page the way your agent should — with a quality score attached.
var op = await client.V2.PerceiveAsync("https://example.com", new PerceiveOptions
{
    Outputs = new[] { "markdown", "structured" },
});
Console.WriteLine($"{op.Outputs["markdown"].Url} {op.RenderQuality}"); // e.g. 0.93
```

---

# V2 — agent-ready data (`client.V2`)

The V2 namespace turns web pages into agent-ready data: render, search, extract, ingest, and monitor. All V2 endpoints require a **private API key** and are plan-gated — a disabled feature or exhausted monthly quota throws `QuotaException` (HTTP 402).

Every render carries `RenderQuality` (0.0–1.0). A low score means the page didn't render cleanly (challenge page, cookie wall, empty shell); the content is still returned, flagged, so a bad read never quietly enters your agent's context.

### Perceive — render a URL into artifacts

```csharp
var op = await client.V2.PerceiveAsync("https://example.com", new PerceiveOptions
{
    Outputs = new[] { "markdown", "screenshot", "structured" },
    Extract = new[] { "tables", "metadata" },
});
Console.WriteLine(op.RenderQuality);          // honesty score, 0.0–1.0
Console.WriteLine(op.Outputs["markdown"].Url); // 15-min signed URL
Console.WriteLine(op.Structured);

// Re-sign artifact URLs later:
var again = await client.V2.GetPerceiveOperationAsync(op.OperationId);

// Batch (<=1000 URLs; small batches run inline, larger return "queued" — poll):
var batch = await client.V2.PerceiveBatchAsync(new[] { "https://a.com", "https://b.com" }, new PerceiveBatchOptions
{
    Outputs = new[] { "markdown" },
    OutputMode = "zip",
});
var done = await client.V2.GetPerceiveBatchAsync(batch.JobId);
```

### Discover — enumerate a site's URLs (no rendering)

```csharp
var found = await client.V2.DiscoverAsync("https://example.com", new DiscoverOptions
{
    Mode = "hybrid",                          // "sitemap" | "crawl" | "hybrid"
    MaxUrls = 200,
    ExcludePatterns = new[] { "/tag/" },
});
Console.WriteLine($"{found.Total} {string.Join(", ", found.Urls)}");
```

### Lookup — web search with optional auto-perceive

```csharp
var search = await client.V2.LookupAsync("best static site generators", new LookupOptions
{
    Category = "web",            // web | news | images | scholar | patents | maps
    NumResults = 10,
    PerceiveTop = 3,             // auto-render top 3 results (uses perceive quota)
});
foreach (var hit in search.Results)
{
    Console.WriteLine($"{hit.Title} {hit.Url} {hit.Perceive?.RenderQuality}");
}
```

### Distill — schema-driven structured extraction

```csharp
using System.Text.Json.Nodes;

var extraction = await client.V2.DistillAsync(new DistillOptions
{
    Urls = new[] { "https://example.com/pricing" },
    Schema = new JsonObject { ["plans"] = "list of plan names with monthly prices" },
    CssSchema = new CssSchema // optional free CSS pass before the LLM tier
    {
        BaseSelector = ".plan-card",
        Fields = new[]
        {
            new CssField { Name = "name", Type = "text", Selector = "h3" },
            new CssField { Name = "price", Type = "text", Selector = ".price" },
        },
    },
});
Console.WriteLine($"{extraction.Results[0].Data} {extraction.Results[0].ExtractionTier}");

// Or discover-then-distill:
await client.V2.DistillAsync(new DistillOptions
{
    DiscoverFrom = new DistillDiscoverFrom { Url = "https://example.com", Mode = "sitemap", MaxPages = 10 },
    Schema = new JsonObject { ["title"] = "page title", ["summary"] = "one-line summary" },
});
```

### Ingest — site or files to RAG-ready JSONL (always async)

Turn a whole site — or a set of uploaded documents — into chunked, RAG-ready JSONL through one pipeline.

```csharp
// From a site:
var job = await client.V2.IngestAsync(new IngestOptions
{
    Mode = "sitemap",
    Url = "https://docs.example.com",
    MaxPages = 100,
    Chunk = new IngestChunkOptions { MaxWords = 512, SentenceOverlap = 1 },
    WebhookUrl = "https://my.app/hooks/enconvert",
});

// Or from uploaded files (PDF, DOCX, PPTX, XLSX, CSV, HTML, EPUB, TXT/MD, legacy/ODF office):
var fileJob = await client.V2.IngestFilesAsync(new[]
{
    new FileInput(await File.ReadAllBytesAsync("handbook.pdf"), "handbook.pdf"),
    new FileInput(await File.ReadAllBytesAsync("notes.docx"), "notes.docx"),
}, new IngestFilesOptions
{
    Chunk = new IngestChunkOptions { MaxWords = 512, SentenceOverlap = 1 },
});

var status = await client.V2.GetIngestJobAsync(job.JobId);            // poll
if (status.Status == "completed") Console.WriteLine(status.OutputUrl); // JSONL

await client.V2.ListIngestJobsAsync(new V2ListOptions { Limit = 20 });
await client.V2.CancelIngestJobAsync(job.JobId); // idempotent

// Webhook signing (HMAC):
var secret = await client.V2.GetWebhookSecretAsync();
await client.V2.RotateWebhookSecretAsync();         // invalidates old secret
await client.V2.RetryIngestWebhookAsync(job.JobId); // re-deliver
```

### Watch — recurring change monitoring

```csharp
var watcher = await client.V2.CreateWatcherAsync("https://example.com/pricing", new WatchCreateOptions
{
    FrequencyMinutes = 60,       // hourly floor
    DiffMode = "auto",           // auto | text | structured | tables | metadata
    WebhookUrl = "https://my.app/hooks/changes",
    NotifyEmail = true,
});

await client.V2.ListWatchersAsync();
await client.V2.GetWatcherAsync(watcher.WatcherId);
await client.V2.GetWatcherSnapshotsAsync(watcher.WatcherId, new SnapshotListOptions { Limit = 10 });
await client.V2.UpdateWatcherAsync(watcher.WatcherId, new WatcherUpdate { Status = "paused" });
await client.V2.UpdateWatcherAsync(watcher.WatcherId, new WatcherUpdate { WebhookUrl = "" }); // clears webhook
await client.V2.DeleteWatcherAsync(watcher.WatcherId); // soft-delete, idempotent
```

### V2 error handling

```csharp
using Enconvert;

try
{
    await client.V2.IngestAsync(new IngestOptions { Urls = new[] { "https://example.com" } });
}
catch (QuotaException)
{
    Console.Error.WriteLine("Upgrade plan or wait for quota reset");
}
```

---

# File conversion

The same key also converts 40+ formats. Two "anything → X" endpoints auto-detect the input; the format-specific endpoints below give you a validated, typed path.

### Anything to Markdown / PDF

```csharp
// Any document → clean Markdown (a RAG-ingestion building block):
await client.ConvertToMarkdownAsync("report.docx", new ConvertToMarkdownOptions { SaveTo = "report.md" });
// PDF, DOCX, PPTX, XLSX, CSV, HTML, EPUB, TXT/MD, and legacy/ODF office. (Images not supported.)

// Almost anything → PDF:
await client.ConvertToPdfAsync("slides.pptx", new ConvertToPdfOptions { SaveTo = "slides.pdf" });
// office/ODF/Pages/Numbers/RTF/CSV, HTML, Markdown, text, images, SVG, EPUB, or a PDF passthrough.
// Only PdfOptions.Grayscale is honored on this endpoint:
await client.ConvertToPdfAsync("scan.pdf", new ConvertToPdfOptions
{
    PdfOptions = new PdfOptions { Grayscale = true },
    SaveTo = "gray.pdf",
});
```

Both accept a path `string`, a `byte[]`, or a `FileInput` record (`new FileInput(bytes, "scan.pdf")`) when bytes need an explicit filename.

### Image Conversion

```csharp
var result = await client.ConvertImageAsync("photo.heic", new ConvertImageOptions
{
    OutputFormat = "webp",
    SaveTo = "photo.webp",
});
```

Any pair among `jpeg`, `png`, `svg`, `heic`, `webp` — plus PDF rasterization:

```csharp
await client.ConvertImageAsync("scan.pdf", new ConvertImageOptions { OutputFormat = "jpeg", SaveTo = "scan.jpeg" });
```

File input accepts a path `string`, a `byte[]`, or a `FileInput` record (`new FileInput(bytes, "scan.pdf")`) when bytes need an explicit filename — pick whichever `ConvertImageAsync`/`ConvertDocumentAsync` overload matches what you have.

Unsupported pairs throw before any request is made. Introspect programmatically:

```csharp
using Enconvert;

Formats.ValidOutputsFor("json"); // ["csv", "toml", "xml", "yaml"]
Formats.ValidOutputsFor("pdf");  // ["jpeg"]
```

### Document Conversion

```csharp
await client.ConvertDocumentAsync("report.docx", new ConvertDocumentOptions { SaveTo = "report.pdf" });
await client.ConvertDocumentAsync("data.json", new ConvertDocumentOptions { OutputFormat = "yaml", SaveTo = "data.yaml" });
await client.ConvertDocumentAsync("notes.md", new ConvertDocumentOptions { OutputFormat = "html", SaveTo = "notes.html" });
```

Supported inputs: `doc`/`docx`, `xls`/`xlsx`, `ppt`/`pptx`, `odt`, `ods`, `odp`, `ots`, `pages`, `numbers`, `html`, `markdown`, `csv`, `json`, `xml`, `yaml`, `toml`. (EPUB → use `ConvertToPdfAsync` / `ConvertToMarkdownAsync`.)

### Supported conversions

| Input | Outputs |
|-------|---------|
| json | csv, toml, xml, yaml |
| xml | csv, json |
| yaml | json |
| csv | json, xml |
| toml | json |
| markdown | html, pdf |
| html | pdf |
| doc, excel, ppt, odt, ods, odp, ots, pages, numbers | pdf |
| jpeg, png, svg, heic, webp | each other (all 20 pairs) |
| pdf | jpeg |

### URL to PDF / Screenshot / Markdown

```csharp
var result = await client.ConvertUrlToPdfAsync("https://example.com", new UrlToPdfOptions
{
    SaveTo = "page.pdf",
});

await client.ConvertUrlToScreenshotAsync("https://example.com", new UrlToScreenshotOptions
{
    ViewportWidth = 1440,
    SaveTo = "screenshot.png",
});

await client.ConvertUrlToMarkdownAsync("https://example.com/article", new UrlToMarkdownOptions
{
    SaveTo = "article.md",
});
```

### Website to PDF / Screenshot (whole-site batch)

Discover every page of a website (via sitemap, or full crawl on Pro/Business plans), convert each one in the background, and receive a single ZIP. Requires a private API key with crawl access.

```csharp
var batch = await client.ConvertWebsiteToPdfAsync("https://example.com", new WebsiteToPdfOptions
{
    CrawlMode = "sitemap",                    // "auto" (default) | "sitemap" | "full"
    ExcludePatterns = new[] { "/blog/tag/" }, // full crawl mode only
});
Console.WriteLine($"{batch.BatchId} {batch.UrlCount} {batch.DiscoveryMethod}");

// Block until done and save the ZIP:
var status = await client.WaitForBatchAsync(batch.BatchId, new WaitForBatchOptions { SaveTo = "site.zip" });
Console.WriteLine($"{status.Completed} of {status.Total} pages converted");

// Or poll yourself:
var s = await client.GetBatchStatusAsync(batch.BatchId);
if (s.Status != "processing") Console.WriteLine(s.ZipDownloadUrl);
```

`ConvertWebsiteToScreenshotAsync` works the same way and produces a ZIP of PNGs.

### PDF options & authenticated pages

```csharp
var result = await client.ConvertUrlToPdfAsync("https://internal.example.com/report", new UrlToPdfOptions
{
    PdfOptions = new PdfOptions
    {
        PageSize = "A4",             // or custom dimensions via PageWidth + PageHeight
        Orientation = "landscape",
        Margins = new PdfMargins { Top = 10, Bottom = 10, Left = 15, Right = 15 },
        Header = new PdfHeaderFooter { Content = "Quarterly Report", Height = 15 },
        Footer = new PdfHeaderFooter { Content = "Confidential", Height = 12 },
    },
    Auth = new HttpBasicAuth { Username = "user", Password = "pass" }, // or Cookies / Headers, plan-gated
    SaveTo = "report.pdf",
});
```

Do not combine `Auth` with an `Authorization` header — the API rejects the conflict.

### Job status (async polling)

```csharp
var status = await client.GetJobStatusAsync("job_abc123");
if (status.Status == "success") Console.WriteLine(status.PresignedUrl);
```

---

## Error Handling

```csharp
using Enconvert;

try
{
    await client.V2.PerceiveAsync("https://example.com");
}
catch (AuthenticationException)
{
    Console.Error.WriteLine("Invalid API key");
}
catch (QuotaException)
{
    Console.Error.WriteLine("Plan feature off or quota exhausted");
}
catch (RateLimitException)
{
    Console.Error.WriteLine("Too many requests — slow down");
}
catch (ApiException e)
{
    Console.Error.WriteLine($"API error [{e.StatusCode}]: {e.Message}");
}
```

The exception hierarchy is `EnconvertException` -> `ApiException` (carries `StatusCode`) -> `AuthenticationException` / `QuotaException` / `RateLimitException`.

## Configuration

```csharp
var client = new EnconvertClient(
    apiKey: "sk_...",
    baseUrl: null,      // override the API base URL; defaults to https://api.enconvert.com
    timeoutMs: 300_000  // default
);
```

Every method accepts an optional trailing `CancellationToken`.

## Get an API Key

Sign up at [enconvert.com](https://enconvert.com). Free tier: 100 ops/month, no credit card.

## License

MIT
