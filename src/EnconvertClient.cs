using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Enconvert.Json;

namespace Enconvert;

/// <summary>
/// Enconvert file conversion client.
/// </summary>
/// <example>
/// <code>
/// var client = new EnconvertClient("sk_...");
/// var result = await client.ConvertUrlToPdfAsync("https://example.com");
/// Console.WriteLine(result.PresignedUrl);
/// </code>
/// </example>
public sealed class EnconvertClient : IDisposable
{
    internal const string DefaultBaseUrl = "https://api.enconvert.com";
    internal const int DefaultTimeoutMs = 300_000;
    private const int DefaultBatchPollIntervalMs = 5_000;
    private const int DefaultBatchTimeoutMs = 1_800_000;

    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly HttpClient _http;
    private readonly HttpClient _download;

    /// <summary>
    /// V2 API namespace: perceive, discover, lookup, distill, ingest, watch.
    /// Requires a private API key; endpoints are plan-gated (QuotaException on 402).
    /// </summary>
    public EnconvertV2 V2 { get; }

    /// <param name="apiKey">Your Enconvert API key.</param>
    /// <param name="baseUrl">Override the API base URL. Defaults to https://api.enconvert.com.</param>
    /// <param name="timeoutMs">Request timeout in milliseconds. Defaults to 300_000 (5 minutes).</param>
    public EnconvertClient(string apiKey, string? baseUrl = null, int timeoutMs = DefaultTimeoutMs)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("Enconvert: 'apiKey' is required", nameof(apiKey));
        }

        _apiKey = apiKey;
        var resolvedBaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _baseUri = new Uri(resolvedBaseUrl + "/");
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        // Downloads target a pre-signed S3 URL directly: no API key header, and no
        // client timeout (large files may legitimately take a while to stream).
        _download = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        V2 = new EnconvertV2((method, path, content, ct) => SendAsync(method, path, content, ct));
    }

    // ------------------------------------------------------------------
    // URL conversions (single page)
    // ------------------------------------------------------------------

    /// <summary>Converts a URL to PDF.</summary>
    public async Task<ConversionResult> ConvertUrlToPdfAsync(string url, UrlToPdfOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new UrlToPdfOptions();
        var body = BuildUrlBody(url, opts);
        body["single_page"] = opts.SinglePage ?? true;
        if (opts.PdfOptions is not null) body["pdf_options"] = Internal.SerializePdfOptions(opts.PdfOptions);

        var data = await PostJsonAsync("/v1/convert/url-to-pdf", body, jobFallback: true, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Converts a URL to a PNG screenshot.</summary>
    public async Task<ConversionResult> ConvertUrlToScreenshotAsync(string url, UrlToScreenshotOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new UrlToScreenshotOptions();
        var body = BuildUrlBody(url, opts);

        var data = await PostJsonAsync("/v1/convert/url-to-screenshot", body, jobFallback: true, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Converts a URL to clean GitHub-Flavored Markdown with YAML frontmatter
    /// (title, description, url, links, images). Strips nav/footer/ads/scripts
    /// and extracts the main article content.
    /// </summary>
    public async Task<ConversionResult> ConvertUrlToMarkdownAsync(string url, UrlToMarkdownOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new UrlToMarkdownOptions();
        var body = BuildUrlBody(url, opts);

        var data = await PostJsonAsync("/v1/convert/url-to-markdown", body, jobFallback: true, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ------------------------------------------------------------------
    // Website conversions (async batch, whole-site crawl)
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts every discovered page of a website to PDF. Async-only: pages
    /// are discovered via sitemap or full crawl (plan-dependent), converted
    /// in the background, and bundled into a single ZIP. Poll with
    /// <see cref="GetBatchStatusAsync"/> or block with <see cref="WaitForBatchAsync"/>.
    /// Requires a private API key with crawl access.
    /// </summary>
    public async Task<BatchSubmission> ConvertWebsiteToPdfAsync(string url, WebsiteToPdfOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new WebsiteToPdfOptions();
        var body = BuildWebsiteBody(url, opts);
        if (opts.SinglePage is not null) body["single_page"] = opts.SinglePage;
        if (opts.PdfOptions is not null) body["pdf_options"] = Internal.SerializePdfOptions(opts.PdfOptions);

        // No job-polling fallback: website submissions have no per-job row, so a
        // 5xx here means the submission itself failed and must surface directly.
        var data = await PostJsonAsync("/v1/convert/website-to-pdf", body, jobFallback: false, cancellationToken).ConfigureAwait(false);
        return ToBatchSubmission(data);
    }

    /// <summary>
    /// Screenshots every discovered page of a website (PNG). Async-only,
    /// bundled into a single ZIP. Poll with <see cref="GetBatchStatusAsync"/>
    /// or block with <see cref="WaitForBatchAsync"/>. Requires a private API
    /// key with crawl access.
    /// </summary>
    public async Task<BatchSubmission> ConvertWebsiteToScreenshotAsync(string url, WebsiteConversionOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new WebsiteConversionOptions();
        var body = BuildWebsiteBody(url, opts);

        var data = await PostJsonAsync("/v1/convert/website-to-screenshot", body, jobFallback: false, cancellationToken).ConfigureAwait(false);
        return ToBatchSubmission(data);
    }

    // ------------------------------------------------------------------
    // File conversions
    // ------------------------------------------------------------------

    /// <summary>
    /// Converts an image between formats (jpeg, png, svg, heic, webp), or
    /// rasterizes a PDF to JPEG. Only pairs implemented by the API are
    /// accepted; unsupported pairs throw before any request is made.
    /// </summary>
    public async Task<ConversionResult> ConvertImageAsync(string filePath, ConvertImageOptions opts, CancellationToken cancellationToken = default)
    {
        var part = await ToFilePartAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ConvertImageCoreAsync(part, opts, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ConvertImageAsync(string, ConvertImageOptions, CancellationToken)"/>
    public Task<ConversionResult> ConvertImageAsync(byte[] data, ConvertImageOptions opts, CancellationToken cancellationToken = default) =>
        ConvertImageCoreAsync(ToFilePart(data), opts, cancellationToken);

    /// <inheritdoc cref="ConvertImageAsync(string, ConvertImageOptions, CancellationToken)"/>
    public Task<ConversionResult> ConvertImageAsync(FileInput file, ConvertImageOptions opts, CancellationToken cancellationToken = default) =>
        ConvertImageCoreAsync(ToFilePart(file), opts, cancellationToken);

    private async Task<ConversionResult> ConvertImageCoreAsync(FilePart part, ConvertImageOptions opts, CancellationToken cancellationToken)
    {
        var inputFormat = Formats.ResolveInputFormat(part.Filename, Formats.ImageFormats);
        var outputFormat = Formats.NormalizeOutputFormat(opts.OutputFormat);
        var endpoint = $"/v1/convert/{Formats.AssertConversionImplemented(inputFormat, outputFormat)}";
        var data = await PostFileAsync(endpoint, part, opts.OutputFilename, pdfOptions: null, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Converts a document (doc, excel, ppt, odt, ods, odp, ots, pages,
    /// numbers, html, markdown, csv, json, xml, yaml, toml). Output
    /// defaults to pdf. Only pairs implemented by the API are accepted;
    /// unsupported pairs throw before any request is made. (EPUB has no
    /// dedicated pair — use <see cref="ConvertToPdfAsync(string, ConvertToPdfOptions?, CancellationToken)"/>
    /// or <see cref="ConvertToMarkdownAsync(string, ConvertToMarkdownOptions?, CancellationToken)"/>.)
    /// </summary>
    public async Task<ConversionResult> ConvertDocumentAsync(string filePath, ConvertDocumentOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var part = await ToFilePartAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ConvertDocumentCoreAsync(part, opts ?? new ConvertDocumentOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ConvertDocumentAsync(string, ConvertDocumentOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertDocumentAsync(byte[] data, ConvertDocumentOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertDocumentCoreAsync(ToFilePart(data), opts ?? new ConvertDocumentOptions(), cancellationToken);

    /// <inheritdoc cref="ConvertDocumentAsync(string, ConvertDocumentOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertDocumentAsync(FileInput file, ConvertDocumentOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertDocumentCoreAsync(ToFilePart(file), opts ?? new ConvertDocumentOptions(), cancellationToken);

    private async Task<ConversionResult> ConvertDocumentCoreAsync(FilePart part, ConvertDocumentOptions opts, CancellationToken cancellationToken)
    {
        var inputFormat = Formats.ResolveInputFormat(part.Filename, Formats.DocumentFormats);
        var outputFormat = Formats.NormalizeOutputFormat(opts.OutputFormat ?? "pdf");
        var endpoint = $"/v1/convert/{Formats.AssertConversionImplemented(inputFormat, outputFormat)}";
        var data = await PostFileAsync(endpoint, part, opts.OutputFilename, opts.PdfOptions, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Converts an uploaded file of (almost) any document format to clean
    /// Markdown — PDF, DOCX, PPTX, XLSX, CSV, HTML, EPUB, TXT/MD, and
    /// legacy/ODF office. The format is auto-detected server-side (no
    /// client-side format check); a RAG-ingestion building block. Images
    /// are not supported.
    /// </summary>
    public async Task<ConversionResult> ConvertToMarkdownAsync(string filePath, ConvertToMarkdownOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var part = await ToFilePartAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ConvertToMarkdownCoreAsync(part, opts ?? new ConvertToMarkdownOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ConvertToMarkdownAsync(string, ConvertToMarkdownOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertToMarkdownAsync(byte[] data, ConvertToMarkdownOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertToMarkdownCoreAsync(ToFilePart(data), opts ?? new ConvertToMarkdownOptions(), cancellationToken);

    /// <inheritdoc cref="ConvertToMarkdownAsync(string, ConvertToMarkdownOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertToMarkdownAsync(FileInput file, ConvertToMarkdownOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertToMarkdownCoreAsync(ToFilePart(file), opts ?? new ConvertToMarkdownOptions(), cancellationToken);

    private async Task<ConversionResult> ConvertToMarkdownCoreAsync(FilePart part, ConvertToMarkdownOptions opts, CancellationToken cancellationToken)
    {
        var data = await PostFileAsync("/v1/convert/anything-to-markdown", part, opts.OutputFilename, pdfOptions: null, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Converts an uploaded file of (almost) any format to PDF —
    /// office/ODF/Pages/Numbers/RTF/CSV, HTML, Markdown, text, raster
    /// images, SVG, EPUB, or an existing PDF (passthrough/normalise). The
    /// format is auto-detected server-side (no client-side format check).
    /// Only <see cref="PdfOptions.Grayscale"/> is honored on this endpoint.
    /// </summary>
    public async Task<ConversionResult> ConvertToPdfAsync(string filePath, ConvertToPdfOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var part = await ToFilePartAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await ConvertToPdfCoreAsync(part, opts ?? new ConvertToPdfOptions(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ConvertToPdfAsync(string, ConvertToPdfOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertToPdfAsync(byte[] data, ConvertToPdfOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertToPdfCoreAsync(ToFilePart(data), opts ?? new ConvertToPdfOptions(), cancellationToken);

    /// <inheritdoc cref="ConvertToPdfAsync(string, ConvertToPdfOptions?, CancellationToken)"/>
    public Task<ConversionResult> ConvertToPdfAsync(FileInput file, ConvertToPdfOptions? opts = null, CancellationToken cancellationToken = default) =>
        ConvertToPdfCoreAsync(ToFilePart(file), opts ?? new ConvertToPdfOptions(), cancellationToken);

    private async Task<ConversionResult> ConvertToPdfCoreAsync(FilePart part, ConvertToPdfOptions opts, CancellationToken cancellationToken)
    {
        var data = await PostFileAsync("/v1/convert/anything-to-pdf", part, opts.OutputFilename, opts.PdfOptions, cancellationToken).ConfigureAwait(false);
        var result = ToConversionResult(data);
        if (opts.SaveTo is not null) await DownloadAsync(result.PresignedUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ------------------------------------------------------------------
    // Job + batch status
    // ------------------------------------------------------------------

    /// <summary>Polls the status of an async conversion job.</summary>
    public async Task<JobStatus> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v1/convert/status/{jobId}", null, cancellationToken).ConfigureAwait(false);
        await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        var data = await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return ToJobStatus(data);
    }

    /// <summary>
    /// Gets the status of an async batch (website conversion). Returns
    /// aggregate counts, per-URL statuses, and download URLs. Private API
    /// keys only.
    /// </summary>
    public async Task<BatchStatus> GetBatchStatusAsync(string batchId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v1/convert/batch/{batchId}", null, cancellationToken).ConfigureAwait(false);
        await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        var data = await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
        return ToBatchStatus(data);
    }

    /// <summary>
    /// Polls a batch until it leaves "processing", then returns its final
    /// status. With <see cref="WaitForBatchOptions.SaveTo"/>, downloads the
    /// batch ZIP once available. Throws <see cref="ApiException"/> (504) on
    /// timeout.
    /// </summary>
    public async Task<BatchStatus> WaitForBatchAsync(string batchId, WaitForBatchOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new WaitForBatchOptions();
        var intervalMs = opts.IntervalMs ?? DefaultBatchPollIntervalMs;
        var timeoutMs = opts.TimeoutMs ?? DefaultBatchTimeoutMs;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (true)
        {
            var status = await GetBatchStatusAsync(batchId, cancellationToken).ConfigureAwait(false);
            if (status.Status != "processing")
            {
                if (opts.SaveTo is not null)
                {
                    if (status.ZipDownloadUrl is null)
                    {
                        throw new ApiException(
                            500,
                            $"Batch {batchId} finished with status '{status.Status}' but no ZIP is available to save");
                    }

                    await DownloadAsync(status.ZipDownloadUrl, opts.SaveTo, cancellationToken).ConfigureAwait(false);
                }

                return status;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new ApiException(504, $"Batch {batchId} did not complete within {timeoutMs}ms");
            }

            await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------
    // Internal helpers (mirror of the Node SDK's postJson / postFile /
    // pollJob / download / toFilePart / raiseForStatus)
    // ------------------------------------------------------------------

    private async Task<JsonObject> PostJsonAsync(string endpoint, JsonObject body, bool jobFallback, CancellationToken cancellationToken)
    {
        var jobId = jobFallback ? Internal.NewJobId() : null;
        if (jobId is not null) body["job_id"] = jobId;
        try
        {
            using var content = JsonContentOf(body);
            using var response = await SendAsync(HttpMethod.Post, endpoint, content, cancellationToken).ConfigureAwait(false);
            await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
            var data = await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
            // Some success responses omit job_id (URL sync path); backfill the
            // client-generated id so callers can still poll GetJobStatusAsync with it.
            if (jobId is not null && !data.ContainsKey("job_id")) data["job_id"] = jobId;
            return data;
        }
        catch (ApiException e) when (jobId is not null && e.StatusCode >= 500)
        {
            return await PollJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonObject> PostFileAsync(
        string endpoint,
        FilePart part,
        string? outputFilename,
        PdfOptions? pdfOptions,
        CancellationToken cancellationToken)
    {
        var jobId = Internal.NewJobId();
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(part.Bytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(part.ContentType);
            form.Add(fileContent, "file", part.Filename);
            form.Add(new StringContent("false"), "direct_download");
            form.Add(new StringContent(jobId), "job_id");
            if (outputFilename is not null) form.Add(new StringContent(outputFilename), "output_filename");
            if (pdfOptions is not null)
            {
                form.Add(new StringContent(Internal.SerializePdfOptions(pdfOptions).ToJsonString()), "pdf_options");
            }

            using var response = await SendAsync(HttpMethod.Post, endpoint, form, cancellationToken).ConfigureAwait(false);
            await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
            var data = await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
            if (!data.ContainsKey("job_id")) data["job_id"] = jobId;
            return data;
        }
        catch (ApiException e) when (e.StatusCode >= 500)
        {
            return await PollJobAsync(jobId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Polls job status until success/failure. Used as a fallback when the HTTP request itself fails with a 5xx.</summary>
    private async Task<JsonObject> PollJobAsync(string jobId, CancellationToken cancellationToken, int maxWaitMs = 300_000, int intervalMs = 3_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            using var response = await SendAsync(HttpMethod.Get, $"/v1/convert/status/{jobId}", null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
            var data = await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
            var status = JsonHelpers.OptStr(data, "status");
            if (status == "success") return data;
            if (status == "failed") throw new ApiException(500, JsonHelpers.OptStr(data, "error") ?? "Conversion failed");
        }

        throw new ApiException(504, "Conversion timed out");
    }

    /// <summary>Saves a presigned URL to a local file. No API key is sent — it's a signed S3 URL.</summary>
    private async Task DownloadAsync(string url, string dest, CancellationToken cancellationToken)
    {
        using var response = await _download.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException((int)response.StatusCode, $"Failed to download: {response.ReasonPhrase}");
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(dest));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (responseStream.ConfigureAwait(false))
        {
            var fileStream = File.Create(dest);
            await using (fileStream.ConfigureAwait(false))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<FilePart> ToFilePartAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var filename = Path.GetFileName(path);
        return new FilePart(bytes, filename, Formats.MimeFor(filename));
    }

    private static FilePart ToFilePart(byte[] data) => new(data, "upload.bin", "application/octet-stream");

    private static FilePart ToFilePart(FileInput file) => new(file.Data, file.Filename, file.ContentType ?? Formats.MimeFor(file.Filename));

    /// <summary>Centralized send with the API key header applied.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path.TrimStart('/'))) { Content = content };
        request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static StringContent JsonContentOf(JsonObject body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public void Dispose()
    {
        _http.Dispose();
        _download.Dispose();
    }

    // ------------------------------------------------------------------
    // Request body builders
    // ------------------------------------------------------------------

    /// <summary>Request body shared by all single-URL conversions.</summary>
    private static JsonObject BuildUrlBody(string url, UrlRenderOptions opts)
    {
        var body = new JsonObject
        {
            ["url"] = url,
            ["direct_download"] = false,
            ["viewport_width"] = opts.ViewportWidth ?? 1920,
            ["viewport_height"] = opts.ViewportHeight ?? 1080,
            ["load_media"] = opts.LoadMedia ?? true,
            ["enable_scroll"] = opts.EnableScroll ?? true,
        };
        if (opts.OutputFilename is not null) body["output_filename"] = opts.OutputFilename;
        Internal.AppendBrowserAccess(body, opts.Auth, opts.Cookies, opts.Headers);
        return body;
    }

    /// <summary>
    /// Request body for website (whole-site) conversions. Render options are
    /// only sent when set — the gateway applies the same defaults per page.
    /// </summary>
    private static JsonObject BuildWebsiteBody(string url, WebsiteConversionOptions opts)
    {
        var body = new JsonObject { ["url"] = url };
        if (opts.CrawlMode is not null) body["crawl_mode"] = opts.CrawlMode;
        if (opts.IncludePatterns is not null) body["include_patterns"] = ToJsonArray(opts.IncludePatterns);
        if (opts.ExcludePatterns is not null) body["exclude_patterns"] = ToJsonArray(opts.ExcludePatterns);
        if (opts.NotificationEmail is not null) body["notification_email"] = opts.NotificationEmail;
        if (opts.CallbackUrl is not null) body["callback_url"] = opts.CallbackUrl;
        if (opts.OutputFilename is not null) body["output_filename"] = opts.OutputFilename;
        if (opts.ViewportWidth is not null) body["viewport_width"] = opts.ViewportWidth;
        if (opts.ViewportHeight is not null) body["viewport_height"] = opts.ViewportHeight;
        if (opts.LoadMedia is not null) body["load_media"] = opts.LoadMedia;
        if (opts.EnableScroll is not null) body["enable_scroll"] = opts.EnableScroll;
        Internal.AppendBrowserAccess(body, opts.Auth, opts.Cookies, opts.Headers);
        return body;
    }

    internal static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }

    // ------------------------------------------------------------------
    // Response mappers
    // ------------------------------------------------------------------

    private static ConversionResult ToConversionResult(JsonObject d)
    {
        // Job-status fallback responses omit `filename`; recover it from the
        // object key so callers never see an empty/undefined value.
        var objectKey = JsonHelpers.OptStr(d, "object_key") ?? "";
        var filename = JsonHelpers.OptStr(d, "filename");
        if (filename is null)
        {
            var idx = objectKey.LastIndexOf('/');
            filename = idx >= 0 ? objectKey[(idx + 1)..] : objectKey;
        }

        return new ConversionResult
        {
            PresignedUrl = JsonHelpers.OptStr(d, "presigned_url") ?? "",
            ObjectKey = objectKey,
            Filename = filename,
            FileSize = JsonHelpers.OptNumInt(d, "file_size"),
            ConversionTimeSeconds = JsonHelpers.OptNum(d, "conversion_time_seconds"),
            JobId = JsonHelpers.OptStr(d, "job_id"),
        };
    }

    private static JobStatus ToJobStatus(JsonObject d) => new()
    {
        Status = JsonHelpers.Str(d, "status"),
        PresignedUrl = JsonHelpers.OptStr(d, "presigned_url"),
        ObjectKey = JsonHelpers.OptStr(d, "object_key"),
        Error = JsonHelpers.OptStr(d, "error"),
    };

    private static BatchSubmission ToBatchSubmission(JsonObject d) => new()
    {
        BatchId = JsonHelpers.OptStr(d, "batch_id") ?? "",
        Status = JsonHelpers.OptStr(d, "status") ?? "processing",
        UrlCount = JsonHelpers.NumInt(d, "url_count"),
        TotalDiscovered = JsonHelpers.OptNumInt(d, "total_discovered"),
        DiscoveryMethod = JsonHelpers.OptStr(d, "discovery_method"),
        OutputFormat = JsonHelpers.OptStr(d, "output_format"),
    };

    private static BatchStatus ToBatchStatus(JsonObject d)
    {
        var items = JsonHelpers.ObjArr(d, "items").Select(item => new BatchItem
        {
            SourceUrl = JsonHelpers.OptStr(item, "source_url") ?? "",
            Status = JsonHelpers.OptStr(item, "status") ?? "",
            DownloadUrl = JsonHelpers.OptStr(item, "download_url"),
            OutputFileSize = JsonHelpers.OptNumInt(item, "output_file_size"),
            Duration = JsonHelpers.OptStr(item, "duration"),
        }).ToList();

        return new BatchStatus
        {
            BatchId = JsonHelpers.OptStr(d, "batch_id") ?? "",
            Status = JsonHelpers.Str(d, "status"),
            Total = JsonHelpers.NumInt(d, "total"),
            Completed = JsonHelpers.NumInt(d, "completed"),
            Failed = JsonHelpers.NumInt(d, "failed"),
            InProgress = JsonHelpers.NumInt(d, "in_progress"),
            OutputMode = JsonHelpers.Str(d, "output_mode"),
            ZipDownloadUrl = JsonHelpers.OptStr(d, "zip_download_url"),
            Items = items,
        };
    }

    private readonly record struct FilePart(byte[] Bytes, string Filename, string ContentType);
}
