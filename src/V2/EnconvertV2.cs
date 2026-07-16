using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Enconvert.Json;

namespace Enconvert;

/// <summary>Authenticated, timeout-wrapped send bound to the client's base URL and API key.</summary>
internal delegate Task<HttpResponseMessage> RequestDelegate(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken);

/// <summary>
/// V2 API namespace, reached as <c>client.V2</c>.
///
/// One method per V2 endpoint (21 total across six groups: perceive,
/// discover, lookup, distill, ingest, watch). Options use PascalCase
/// properties that are serialized to the API's snake_case wire format;
/// responses are mapped back the same way. User-data payloads (schemas,
/// extracted data, tracked fields, diff changes) pass through untouched as
/// <see cref="JsonObject"/>.
///
/// All V2 endpoints require a private API key (public keys are rejected)
/// and are plan-gated: a disabled feature or exhausted monthly quota raises
/// <see cref="QuotaException"/> (HTTP 402).
/// </summary>
public sealed class EnconvertV2
{
    private readonly RequestDelegate _request;

    internal EnconvertV2(RequestDelegate request)
    {
        _request = request;
    }

    // ------------------------------------------------------------------
    // Perceive — render a URL into agent-ready artifacts
    // ------------------------------------------------------------------

    /// <summary>
    /// Renders one URL into the requested outputs (markdown, screenshots,
    /// PDF, links, structured data, ...). Synchronous: returns the completed
    /// operation with 15-minute signed artifact URLs.
    /// </summary>
    public async Task<PerceiveResult> PerceiveAsync(string url, PerceiveOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var body = SerializePerceiveOptions(opts ?? new PerceiveOptions());
        body["url"] = url;
        return ToPerceiveResult(await PostAsync("/v2/perceive", body, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Re-fetches a perceive operation by id (per_...). Artifact URLs are
    /// freshly re-signed on every call.
    /// </summary>
    public async Task<PerceiveResult> GetPerceiveOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/perceive/{Uri.EscapeDataString(operationId)}", cancellationToken).ConfigureAwait(false);
        return ToPerceiveResult(data);
    }

    /// <summary>
    /// Perceives up to 1000 URLs with one shared options block. Small
    /// batches run inline (completed result); larger ones return status
    /// "queued" — poll <see cref="GetPerceiveBatchAsync"/> with the jobId.
    /// </summary>
    public async Task<PerceiveBatchResult> PerceiveBatchAsync(
        IReadOnlyList<string> urls,
        PerceiveBatchOptions? opts = null,
        CancellationToken cancellationToken = default)
    {
        opts ??= new PerceiveBatchOptions();
        var body = new JsonObject
        {
            ["urls"] = EnconvertClient.ToJsonArray(urls),
            ["options"] = SerializePerceiveOptions(opts),
        };
        if (opts.OutputMode is not null) body["output_mode"] = opts.OutputMode;
        return ToPerceiveBatchResult(await PostAsync("/v2/perceive/batch", body, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Polls a perceive batch by jobId. Items fill in as URLs complete.</summary>
    public async Task<PerceiveBatchResult> GetPerceiveBatchAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/perceive/batch/{Uri.EscapeDataString(jobId)}", cancellationToken).ConfigureAwait(false);
        return ToPerceiveBatchResult(data);
    }

    // ------------------------------------------------------------------
    // Discover — enumerate a site's URLs without rendering
    // ------------------------------------------------------------------

    /// <summary>
    /// Lists a site's URLs via sitemap, HTTP crawl, or both. No browser
    /// rendering — fast and does not consume perceive quota.
    /// </summary>
    public async Task<DiscoverResult> DiscoverAsync(string url, DiscoverOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new DiscoverOptions();
        var body = new JsonObject { ["url"] = url };
        if (opts.Mode is not null) body["mode"] = opts.Mode;
        if (opts.MaxUrls is not null) body["max_urls"] = opts.MaxUrls;
        if (opts.MaxDepth is not null) body["max_depth"] = opts.MaxDepth;
        if (opts.IncludePatterns is not null) body["include_patterns"] = EnconvertClient.ToJsonArray(opts.IncludePatterns);
        if (opts.ExcludePatterns is not null) body["exclude_patterns"] = EnconvertClient.ToJsonArray(opts.ExcludePatterns);
        if (opts.SameDomainOnly is not null) body["same_domain_only"] = opts.SameDomainOnly;
        if (opts.RespectRobots is not null) body["respect_robots"] = opts.RespectRobots;
        return ToDiscoverResult(await PostAsync("/v2/discover", body, cancellationToken).ConfigureAwait(false));
    }

    // ------------------------------------------------------------------
    // Lookup — web search with optional auto-perceive
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs a categorized web search. With PerceiveTop &gt; 0, the top-N
    /// result URLs are auto-perceived (each consumes one perceive-quota
    /// unit) and carry their full <see cref="PerceiveResult"/> inline.
    /// </summary>
    public async Task<LookupResult> LookupAsync(string query, LookupOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new LookupOptions();
        var body = new JsonObject { ["query"] = query };
        if (opts.Category is not null) body["category"] = opts.Category;
        if (opts.Country is not null) body["country"] = opts.Country;
        if (opts.Locale is not null) body["locale"] = opts.Locale;
        if (opts.TimeFilter is not null) body["time_filter"] = opts.TimeFilter;
        if (opts.NumResults is not null) body["num_results"] = opts.NumResults;
        if (opts.Page is not null) body["page"] = opts.Page;
        if (opts.Location is not null) body["location"] = opts.Location;
        if (opts.Autocorrect is not null) body["autocorrect"] = opts.Autocorrect;
        if (opts.PerceiveTop is not null) body["perceive_top"] = opts.PerceiveTop;
        return ToLookupResult(await PostAsync("/v2/lookup", body, cancellationToken).ConfigureAwait(false));
    }

    // ------------------------------------------------------------------
    // Distill — schema-driven structured extraction
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts structured data matching <see cref="DistillOptions.Schema"/>
    /// from explicit URLs or from a discovered site. An optional CssSchema
    /// answers fields for free; anything it misses escalates to the LLM
    /// tier (plan-gated).
    /// </summary>
    public async Task<DistillResult> DistillAsync(DistillOptions opts, CancellationToken cancellationToken = default)
    {
        var hasUrls = opts.Urls is { Count: > 0 };
        var hasDiscover = opts.DiscoverFrom is not null;
        if (hasUrls == hasDiscover)
        {
            throw new ArgumentException("Distill: provide exactly one of 'Urls' or 'DiscoverFrom'");
        }

        if (opts.Schema is null)
        {
            throw new ArgumentException("Distill: 'Schema' is required and must be an object");
        }

        var body = new JsonObject { ["schema"] = opts.Schema.DeepClone() };
        if (hasUrls) body["urls"] = EnconvertClient.ToJsonArray(opts.Urls!);
        if (opts.DiscoverFrom is not null)
        {
            var df = new JsonObject { ["url"] = opts.DiscoverFrom.Url };
            if (opts.DiscoverFrom.Mode is not null) df["mode"] = opts.DiscoverFrom.Mode;
            if (opts.DiscoverFrom.MaxPages is not null) df["max_pages"] = opts.DiscoverFrom.MaxPages;
            body["discover_from"] = df;
        }

        if (opts.CssSchema is not null) body["css_schema"] = SerializeCssSchema(opts.CssSchema);
        if (opts.WaitFor is not null) body["wait_for"] = opts.WaitFor;
        if (opts.WaitTimeoutMs is not null) body["wait_timeout_ms"] = opts.WaitTimeoutMs;
        if (opts.Headers is not null) body["headers"] = ToJsonObject(opts.Headers);
        if (opts.Cookies is not null) body["cookies"] = ToJsonArray(opts.Cookies);
        if (opts.RespectRobots is not null) body["respect_robots"] = opts.RespectRobots;
        return ToDistillResult(await PostAsync("/v2/distill", body, cancellationToken).ConfigureAwait(false));
    }

    // ------------------------------------------------------------------
    // Ingest — site to RAG-ready JSONL chunks (always async)
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts an ingest job: turns explicit URLs or a discovered site into
    /// chunked, RAG-ready JSONL. Always asynchronous — returns the queued
    /// job; poll <see cref="GetIngestJobAsync"/> or configure WebhookUrl for
    /// completion.
    /// </summary>
    public async Task<IngestJob> IngestAsync(IngestOptions opts, CancellationToken cancellationToken = default)
    {
        var mode = opts.Mode ?? "urls";
        if (mode == "urls")
        {
            if (opts.Urls is not { Count: > 0 })
            {
                throw new ArgumentException("Ingest: mode 'urls' requires a non-empty 'Urls' list");
            }

            if (opts.Url is not null)
            {
                throw new ArgumentException("Ingest: mode 'urls' does not accept 'Url'");
            }
        }
        else
        {
            if (string.IsNullOrEmpty(opts.Url))
            {
                throw new ArgumentException($"Ingest: mode '{mode}' requires a seed 'Url'");
            }

            if (opts.Urls is not null)
            {
                throw new ArgumentException($"Ingest: mode '{mode}' does not accept 'Urls'");
            }
        }

        var body = new JsonObject();
        if (opts.Mode is not null) body["mode"] = opts.Mode;
        if (opts.Url is not null) body["url"] = opts.Url;
        if (opts.Urls is not null) body["urls"] = EnconvertClient.ToJsonArray(opts.Urls);
        if (opts.MaxPages is not null) body["max_pages"] = opts.MaxPages;
        if (opts.MaxDepth is not null) body["max_depth"] = opts.MaxDepth;
        if (opts.SameDomainOnly is not null) body["same_domain_only"] = opts.SameDomainOnly;
        if (opts.IncludePatterns is not null) body["include_patterns"] = EnconvertClient.ToJsonArray(opts.IncludePatterns);
        if (opts.ExcludePatterns is not null) body["exclude_patterns"] = EnconvertClient.ToJsonArray(opts.ExcludePatterns);
        if (opts.RespectRobots is not null) body["respect_robots"] = opts.RespectRobots;
        if (opts.WaitFor is not null) body["wait_for"] = opts.WaitFor;
        if (opts.WaitTimeoutMs is not null) body["wait_timeout_ms"] = opts.WaitTimeoutMs;
        if (opts.Chunk is not null)
        {
            var chunk = new JsonObject();
            if (opts.Chunk.MaxWords is not null) chunk["max_words"] = opts.Chunk.MaxWords;
            if (opts.Chunk.SentenceOverlap is not null) chunk["sentence_overlap"] = opts.Chunk.SentenceOverlap;
            body["chunk"] = chunk;
        }

        if (opts.WebhookUrl is not null) body["webhook_url"] = opts.WebhookUrl;
        return ToIngestJob(await PostAsync("/v2/ingest", body, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Ingests one or more uploaded files into RAG-ready JSONL chunks — the
    /// file counterpart of <see cref="IngestAsync"/>, sharing the same job
    /// lifecycle (mode "files"). PDF, DOCX, PPTX, XLSX, CSV, HTML, EPUB,
    /// TXT/MD and legacy/ODF office are accepted. Always asynchronous; poll
    /// <see cref="GetIngestJobAsync"/> or configure a webhook.
    /// </summary>
    public async Task<IngestJob> IngestFilesAsync(
        IEnumerable<FileInput> files,
        IngestFilesOptions? opts = null,
        CancellationToken cancellationToken = default)
    {
        opts ??= new IngestFilesOptions();
        var fileList = files.ToList();
        if (fileList.Count == 0)
        {
            throw new ArgumentException("IngestFiles: provide at least one file");
        }

        using var form = new MultipartFormDataContent();
        foreach (var file in fileList)
        {
            var fileContent = new ByteArrayContent(file.Data);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? Formats.MimeFor(file.Filename));
            form.Add(fileContent, "files", file.Filename);
        }

        if (opts.Chunk?.MaxWords is not null) form.Add(new StringContent($"{opts.Chunk.MaxWords}"), "max_words");
        if (opts.Chunk?.SentenceOverlap is not null) form.Add(new StringContent($"{opts.Chunk.SentenceOverlap}"), "sentence_overlap");
        if (opts.WebhookUrl is not null) form.Add(new StringContent(opts.WebhookUrl), "webhook_url");

        using var response = await _request(HttpMethod.Post, "/v2/ingest/files", form, cancellationToken).ConfigureAwait(false);
        await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        return ToIngestJob(await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Lists ingest jobs, newest first.</summary>
    public async Task<IngestJobList> ListIngestJobsAsync(V2ListOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/ingest{ListQuery(opts)}", cancellationToken).ConfigureAwait(false);
        return new IngestJobList
        {
            Jobs = JsonHelpers.ObjArr(data, "jobs").Select(ToIngestJobSummary).ToList(),
            Skip = JsonHelpers.NumInt(data, "skip"),
            Limit = JsonHelpers.NumInt(data, "limit", 20),
            HasMore = JsonHelpers.Bool(data, "has_more"),
        };
    }

    /// <summary>Gets one ingest job by id (ing_...).</summary>
    public async Task<IngestJob> GetIngestJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/ingest/{Uri.EscapeDataString(jobId)}", cancellationToken).ConfigureAwait(false);
        return ToIngestJob(data);
    }

    /// <summary>
    /// Cancels a queued/processing ingest job. Idempotent: canceling an
    /// already-terminal job returns it unchanged.
    /// </summary>
    public async Task<IngestJob> CancelIngestJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var data = await JsonAsync(HttpMethod.Delete, $"/v2/ingest/{Uri.EscapeDataString(jobId)}", null, cancellationToken).ConfigureAwait(false);
        return ToIngestJob(data);
    }

    /// <summary>
    /// Re-delivers the completion webhook of a completed job (409 if the
    /// job is not completed, 400 if it has no webhook configured).
    /// </summary>
    public async Task<WebhookRetryResult> RetryIngestWebhookAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var d = await PostAsync($"/v2/ingest/{Uri.EscapeDataString(jobId)}/retry-webhook", null, cancellationToken).ConfigureAwait(false);
        return new WebhookRetryResult
        {
            JobId = JsonHelpers.Str(d, "job_id"),
            Delivered = JsonHelpers.Bool(d, "delivered"),
            Attempts = JsonHelpers.NumInt(d, "attempts"),
            StatusCode = JsonHelpers.OptNumInt(d, "status_code"),
            Detail = JsonHelpers.Str(d, "detail"),
        };
    }

    /// <summary>
    /// Gets (creating on first call) the project's webhook signing secret
    /// and the header/scheme details needed to verify deliveries.
    /// </summary>
    public async Task<WebhookSecret> GetWebhookSecretAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetAsync("/v2/ingest/webhook-secret", cancellationToken).ConfigureAwait(false);
        return ToWebhookSecret(data);
    }

    /// <summary>
    /// Rotates the webhook signing secret. Signatures made with the
    /// previous secret stop verifying immediately.
    /// </summary>
    public async Task<WebhookSecret> RotateWebhookSecretAsync(CancellationToken cancellationToken = default)
    {
        var data = await PostAsync("/v2/ingest/webhook-secret/rotate", null, cancellationToken).ConfigureAwait(false);
        return ToWebhookSecret(data);
    }

    // ------------------------------------------------------------------
    // Watch — recurring change monitoring
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a watcher that re-renders <paramref name="url"/> on a fixed
    /// cadence (hourly floor) and notifies on changes via email and/or
    /// webhook.
    /// </summary>
    public async Task<Watcher> CreateWatcherAsync(string url, WatchCreateOptions? opts = null, CancellationToken cancellationToken = default)
    {
        opts ??= new WatchCreateOptions();
        var body = new JsonObject { ["url"] = url };
        if (opts.FrequencyMinutes is not null) body["frequency_minutes"] = opts.FrequencyMinutes;
        if (opts.DiffMode is not null) body["diff_mode"] = opts.DiffMode;
        if (opts.TrackFields is not null) body["track_fields"] = opts.TrackFields.DeepClone();
        if (opts.WebhookUrl is not null) body["webhook_url"] = opts.WebhookUrl;
        if (opts.NotifyEmail is not null) body["notify_email"] = opts.NotifyEmail;
        return ToWatcher(await PostAsync("/v2/watch", body, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Lists watchers, newest first.</summary>
    public async Task<WatcherList> ListWatchersAsync(V2ListOptions? opts = null, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/watch{ListQuery(opts)}", cancellationToken).ConfigureAwait(false);
        return new WatcherList
        {
            Watchers = JsonHelpers.ObjArr(data, "watchers").Select(ToWatcherSummary).ToList(),
            Skip = JsonHelpers.NumInt(data, "skip"),
            Limit = JsonHelpers.NumInt(data, "limit", 20),
            HasMore = JsonHelpers.Bool(data, "has_more"),
        };
    }

    /// <summary>Gets one watcher by id (wat_...). Deleted watchers read as 404.</summary>
    public async Task<Watcher> GetWatcherAsync(string watcherId, CancellationToken cancellationToken = default)
    {
        var data = await GetAsync($"/v2/watch/{Uri.EscapeDataString(watcherId)}", cancellationToken).ConfigureAwait(false);
        return ToWatcher(data);
    }

    /// <summary>Pages through a watcher's check history, newest first.</summary>
    public async Task<WatcherSnapshotList> GetWatcherSnapshotsAsync(
        string watcherId,
        SnapshotListOptions? opts = null,
        CancellationToken cancellationToken = default)
    {
        var query = opts?.Limit is not null ? $"?limit={opts.Limit}" : "";
        var data = await GetAsync($"/v2/watch/{Uri.EscapeDataString(watcherId)}/snapshots{query}", cancellationToken).ConfigureAwait(false);
        return new WatcherSnapshotList
        {
            WatcherId = JsonHelpers.Str(data, "watcher_id"),
            Snapshots = JsonHelpers.ObjArr(data, "snapshots").Select(ToWatcherSnapshot).ToList(),
            Limit = JsonHelpers.NumInt(data, "limit", 20),
        };
    }

    /// <summary>
    /// Updates a watcher. At least one field is required. Set
    /// <see cref="WatcherUpdate.WebhookUrl"/> to "" to clear the webhook;
    /// resuming a paused watcher re-checks the plan's watcher cap.
    /// </summary>
    public async Task<Watcher> UpdateWatcherAsync(string watcherId, WatcherUpdate updates, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (updates.FrequencyMinutes is not null) body["frequency_minutes"] = updates.FrequencyMinutes;
        if (updates.DiffMode is not null) body["diff_mode"] = updates.DiffMode;
        if (updates.TrackFields is not null) body["track_fields"] = updates.TrackFields.DeepClone();
        if (updates.WebhookUrl is not null) body["webhook_url"] = updates.WebhookUrl;
        if (updates.NotifyEmail is not null) body["notify_email"] = updates.NotifyEmail;
        if (updates.Status is not null) body["status"] = updates.Status;
        if (body.Count == 0)
        {
            throw new ArgumentException("UpdateWatcher: provide at least one field to update");
        }

        var data = await JsonAsync(
            HttpMethod.Patch,
            $"/v2/watch/{Uri.EscapeDataString(watcherId)}",
            JsonContentOf(body),
            cancellationToken).ConfigureAwait(false);
        return ToWatcher(data);
    }

    /// <summary>
    /// Soft-deletes a watcher (idempotent). Returns the tombstoned watcher
    /// with status "deleted".
    /// </summary>
    public async Task<Watcher> DeleteWatcherAsync(string watcherId, CancellationToken cancellationToken = default)
    {
        var data = await JsonAsync(HttpMethod.Delete, $"/v2/watch/{Uri.EscapeDataString(watcherId)}", null, cancellationToken).ConfigureAwait(false);
        return ToWatcher(data);
    }

    // ------------------------------------------------------------------
    // HTTP helpers
    // ------------------------------------------------------------------

    private async Task<JsonObject> JsonAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var response = await _request(method, path, content, cancellationToken).ConfigureAwait(false);
        await Internal.RaiseForStatusAsync(response, cancellationToken).ConfigureAwait(false);
        return await Internal.ParseJsonObjectAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonObject> PostAsync(string path, JsonObject? body, CancellationToken cancellationToken) =>
        JsonAsync(HttpMethod.Post, path, body is null ? null : JsonContentOf(body), cancellationToken);

    private Task<JsonObject> GetAsync(string path, CancellationToken cancellationToken) =>
        JsonAsync(HttpMethod.Get, path, null, cancellationToken);

    private static StringContent JsonContentOf(JsonObject body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    // ------------------------------------------------------------------
    // Request serializers
    // ------------------------------------------------------------------

    private static JsonObject SerializePerceiveOptions(PerceiveOptions o)
    {
        var outObj = new JsonObject();
        if (o.Outputs is not null) outObj["outputs"] = EnconvertClient.ToJsonArray(o.Outputs);
        if (o.Extract is not null) outObj["extract"] = EnconvertClient.ToJsonArray(o.Extract);
        if (o.Schema is not null) outObj["schema"] = o.Schema.DeepClone();
        if (o.WaitFor is not null) outObj["wait_for"] = o.WaitFor;
        if (o.WaitTimeoutMs is not null) outObj["wait_timeout_ms"] = o.WaitTimeoutMs;
        if (o.JsCode is not null) outObj["js_code"] = o.JsCode;
        if (o.Viewport is not null)
        {
            var viewport = new JsonObject();
            if (o.Viewport.Width is not null) viewport["width"] = o.Viewport.Width;
            if (o.Viewport.Height is not null) viewport["height"] = o.Viewport.Height;
            outObj["viewport"] = viewport;
        }

        if (o.Headers is not null) outObj["headers"] = ToJsonObject(o.Headers);
        if (o.Cookies is not null) outObj["cookies"] = ToJsonArray(o.Cookies);
        if (o.Auth is not null) outObj["auth"] = o.Auth.ToJson();
        if (o.ProxyUrl is not null) outObj["proxy_url"] = o.ProxyUrl;
        if (o.Geolocation is not null) outObj["geolocation"] = o.Geolocation.DeepClone();
        if (o.ActionChain is not null)
        {
            var arr = new JsonArray();
            foreach (var step in o.ActionChain) arr.Add(step.DeepClone());
            outObj["action_chain"] = arr;
        }

        if (o.CacheMode is not null) outObj["cache_mode"] = o.CacheMode;
        if (o.PdfOptions is not null) outObj["pdf_options"] = Internal.SerializePdfOptions(o.PdfOptions);
        if (o.BlockResources is not null) outObj["block_resources"] = EnconvertClient.ToJsonArray(o.BlockResources);
        if (o.RespectRobots is not null) outObj["respect_robots"] = o.RespectRobots;
        if (o.Mobile is not null) outObj["mobile"] = o.Mobile;
        return outObj;
    }

    private static JsonObject SerializeCssField(CssField f)
    {
        var outObj = new JsonObject { ["name"] = f.Name, ["type"] = f.Type };
        if (f.Selector is not null) outObj["selector"] = f.Selector;
        if (f.Attribute is not null) outObj["attribute"] = f.Attribute;
        if (f.Pattern is not null) outObj["pattern"] = f.Pattern;
        if (f.Default is not null) outObj["default"] = f.Default.DeepClone();
        if (f.Transform is not null) outObj["transform"] = f.Transform;
        if (f.Fields is not null)
        {
            var arr = new JsonArray();
            foreach (var field in f.Fields) arr.Add(SerializeCssField(field));
            outObj["fields"] = arr;
        }

        return outObj;
    }

    private static JsonObject SerializeCssSchema(CssSchema s)
    {
        var fields = new JsonArray();
        foreach (var field in s.Fields) fields.Add(SerializeCssField(field));
        var outObj = new JsonObject { ["baseSelector"] = s.BaseSelector, ["fields"] = fields };
        if (s.Name is not null) outObj["name"] = s.Name;
        if (s.TargetField is not null) outObj["target_field"] = s.TargetField;
        return outObj;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, string> map)
    {
        var o = new JsonObject();
        foreach (var (k, v) in map) o[k] = v;
        return o;
    }

    private static JsonArray ToJsonArray(IReadOnlyList<BrowserCookie> cookies)
    {
        var arr = new JsonArray();
        foreach (var cookie in cookies) arr.Add(cookie.ToJson());
        return arr;
    }

    // ------------------------------------------------------------------
    // Response mappers. Optional fields may be absent entirely
    // (response_model_exclude_none) — every access is guarded.
    // ------------------------------------------------------------------

    private static V2Tokens ToTokens(JsonObject? d) => new()
    {
        Input = d is not null ? JsonHelpers.NumInt(d, "input") : 0,
        Output = d is not null ? JsonHelpers.NumInt(d, "output") : 0,
    };

    private static V2OutputArtifact ToOutputArtifact(JsonObject d) => new()
    {
        Url = JsonHelpers.OptStr(d, "url"),
        ObjectKey = JsonHelpers.Str(d, "object_key"),
        SizeBytes = JsonHelpers.NumInt(d, "size_bytes"),
        ContentType = JsonHelpers.Str(d, "content_type", "application/octet-stream"),
        ExpiresIn = JsonHelpers.NumInt(d, "expires_in", 900),
    };

    private static PerceiveResult ToPerceiveResult(JsonObject d)
    {
        var rawOutputs = JsonHelpers.OptObj(d, "outputs");
        var outputs = new Dictionary<string, V2OutputArtifact>();
        if (rawOutputs is not null)
        {
            foreach (var (name, node) in rawOutputs)
            {
                if (node is JsonObject artifact) outputs[name] = ToOutputArtifact(artifact);
            }
        }

        return new PerceiveResult
        {
            OperationId = JsonHelpers.Str(d, "operation_id"),
            Status = JsonHelpers.Str(d, "status"),
            Url = JsonHelpers.Str(d, "url"),
            UrlFinal = JsonHelpers.OptStr(d, "url_final"),
            ContentHash = JsonHelpers.OptStr(d, "content_hash"),
            RenderQuality = JsonHelpers.OptNum(d, "render_quality"),
            CacheHit = JsonHelpers.Bool(d, "cache_hit"),
            Outputs = outputs,
            Structured = JsonHelpers.OptObj(d, "structured"),
            ExtractionTier = JsonHelpers.OptStr(d, "extraction_tier"),
            Tokens = ToTokens(JsonHelpers.OptObj(d, "tokens")),
            CostCents = JsonHelpers.Num(d, "cost_cents"),
            DurationMs = JsonHelpers.OptNumInt(d, "duration_ms"),
            Error = JsonHelpers.OptStr(d, "error"),
            Warnings = JsonHelpers.StrArr(d, "warnings"),
        };
    }

    private static PerceiveBatchResult ToPerceiveBatchResult(JsonObject d)
    {
        var zip = JsonHelpers.OptObj(d, "zip");
        return new PerceiveBatchResult
        {
            JobId = JsonHelpers.Str(d, "job_id"),
            Status = JsonHelpers.Str(d, "status"),
            OutputMode = JsonHelpers.OptStr(d, "output_mode") ?? "manifest",
            Total = JsonHelpers.NumInt(d, "total"),
            Completed = JsonHelpers.NumInt(d, "completed"),
            Failed = JsonHelpers.NumInt(d, "failed"),
            Pending = JsonHelpers.NumInt(d, "pending"),
            Zip = zip is not null ? ToOutputArtifact(zip) : null,
            Items = JsonHelpers.ObjArr(d, "items").Select(ToPerceiveResult).ToList(),
            Warnings = JsonHelpers.StrArr(d, "warnings"),
        };
    }

    private static DiscoverResult ToDiscoverResult(JsonObject d) => new()
    {
        Url = JsonHelpers.Str(d, "url"),
        Mode = JsonHelpers.Str(d, "mode"),
        Total = JsonHelpers.NumInt(d, "total"),
        Urls = JsonHelpers.StrArr(d, "urls"),
        PagesCrawled = JsonHelpers.NumInt(d, "pages_crawled"),
        Truncated = JsonHelpers.Bool(d, "truncated"),
        RobotsRespected = JsonHelpers.Bool(d, "robots_respected"),
        Sources = JsonHelpers.NumDict(d, "sources"),
        Warnings = JsonHelpers.StrArr(d, "warnings"),
    };

    private static LookupItem ToLookupItem(JsonObject d)
    {
        var perceive = JsonHelpers.OptObj(d, "perceive");
        return new LookupItem
        {
            Title = JsonHelpers.OptStr(d, "title"),
            Url = JsonHelpers.OptStr(d, "url"),
            Snippet = JsonHelpers.OptStr(d, "snippet"),
            Position = JsonHelpers.OptNumInt(d, "position"),
            Source = JsonHelpers.OptStr(d, "source"),
            Date = JsonHelpers.OptStr(d, "date"),
            ImageUrl = JsonHelpers.OptStr(d, "image_url"),
            ThumbnailUrl = JsonHelpers.OptStr(d, "thumbnail_url"),
            Extra = JsonHelpers.OptObj(d, "extra") ?? new JsonObject(),
            Perceive = perceive is not null ? ToPerceiveResult(perceive) : null,
        };
    }

    private static LookupResult ToLookupResult(JsonObject d) => new()
    {
        LookupId = JsonHelpers.OptNumInt(d, "lookup_id"),
        Query = JsonHelpers.Str(d, "query"),
        Category = JsonHelpers.Str(d, "category"),
        Country = JsonHelpers.OptStr(d, "country"),
        Locale = JsonHelpers.OptStr(d, "locale"),
        TimeFilter = JsonHelpers.OptStr(d, "time_filter"),
        Total = JsonHelpers.NumInt(d, "total"),
        Results = JsonHelpers.ObjArr(d, "results").Select(ToLookupItem).ToList(),
        PerceiveTop = JsonHelpers.NumInt(d, "perceive_top"),
        PerceiveOperationIds = JsonHelpers.StrArr(d, "perceive_operation_ids"),
        AnswerBox = JsonHelpers.OptObj(d, "answer_box"),
        KnowledgeGraph = JsonHelpers.OptObj(d, "knowledge_graph"),
        Credits = JsonHelpers.OptNumInt(d, "credits"),
        CostCents = JsonHelpers.Num(d, "cost_cents"),
        Warnings = JsonHelpers.StrArr(d, "warnings"),
    };

    private static DistillItem ToDistillItem(JsonObject d) => new()
    {
        Url = JsonHelpers.Str(d, "url"),
        UrlFinal = JsonHelpers.OptStr(d, "url_final"),
        Status = JsonHelpers.OptStr(d, "status") ?? "completed",
        Data = JsonHelpers.OptObj(d, "data"),
        ExtractionTier = JsonHelpers.OptStr(d, "extraction_tier") ?? "none",
        FieldsFromCss = JsonHelpers.NumInt(d, "fields_from_css"),
        FieldsFromLlm = JsonHelpers.NumInt(d, "fields_from_llm"),
        RenderQuality = JsonHelpers.OptNum(d, "render_quality"),
        Tokens = ToTokens(JsonHelpers.OptObj(d, "tokens")),
        CostCents = JsonHelpers.Num(d, "cost_cents"),
        Error = JsonHelpers.OptStr(d, "error"),
        Warnings = JsonHelpers.StrArr(d, "warnings"),
    };

    private static DistillResult ToDistillResult(JsonObject d) => new()
    {
        OperationId = JsonHelpers.Str(d, "operation_id"),
        Total = JsonHelpers.NumInt(d, "total"),
        Completed = JsonHelpers.NumInt(d, "completed"),
        Failed = JsonHelpers.NumInt(d, "failed"),
        Results = JsonHelpers.ObjArr(d, "results").Select(ToDistillItem).ToList(),
        TotalCostCents = JsonHelpers.Num(d, "total_cost_cents"),
        Warnings = JsonHelpers.StrArr(d, "warnings"),
    };

    private static IngestJob ToIngestJob(JsonObject d) => new()
    {
        JobId = JsonHelpers.Str(d, "job_id"),
        Status = JsonHelpers.Str(d, "status"),
        Mode = JsonHelpers.Str(d, "mode"),
        PagesDiscovered = JsonHelpers.NumInt(d, "pages_discovered"),
        PagesProcessed = JsonHelpers.NumInt(d, "pages_processed"),
        PagesFailed = JsonHelpers.NumInt(d, "pages_failed"),
        TotalChunks = JsonHelpers.NumInt(d, "total_chunks"),
        OutputUrl = JsonHelpers.OptStr(d, "output_url"),
        ErrorMessage = JsonHelpers.OptStr(d, "error_message"),
        WebhookUrl = JsonHelpers.OptStr(d, "webhook_url"),
        WebhookDelivered = JsonHelpers.Bool(d, "webhook_delivered"),
        CreatedAt = JsonHelpers.OptStr(d, "created_at"),
        CompletedAt = JsonHelpers.OptStr(d, "completed_at"),
        Warnings = JsonHelpers.StrArr(d, "warnings"),
    };

    private static IngestJobSummary ToIngestJobSummary(JsonObject d) => new()
    {
        JobId = JsonHelpers.Str(d, "job_id"),
        Status = JsonHelpers.Str(d, "status"),
        Mode = JsonHelpers.Str(d, "mode"),
        PagesDiscovered = JsonHelpers.NumInt(d, "pages_discovered"),
        PagesProcessed = JsonHelpers.NumInt(d, "pages_processed"),
        PagesFailed = JsonHelpers.NumInt(d, "pages_failed"),
        TotalChunks = JsonHelpers.NumInt(d, "total_chunks"),
        OutputUrl = JsonHelpers.OptStr(d, "output_url"),
        ErrorMessage = JsonHelpers.OptStr(d, "error_message"),
        WebhookConfigured = JsonHelpers.Bool(d, "webhook_configured"),
        WebhookDelivered = JsonHelpers.Bool(d, "webhook_delivered"),
        CreatedAt = JsonHelpers.OptStr(d, "created_at"),
        CompletedAt = JsonHelpers.OptStr(d, "completed_at"),
    };

    private static WebhookSecret ToWebhookSecret(JsonObject d) => new()
    {
        Secret = JsonHelpers.Str(d, "secret"),
        SignatureHeader = JsonHelpers.Str(d, "signature_header"),
        TimestampHeader = JsonHelpers.Str(d, "timestamp_header"),
        SignatureScheme = JsonHelpers.Str(d, "signature_scheme"),
        ReplayToleranceSeconds = JsonHelpers.NumInt(d, "replay_tolerance_seconds"),
        Rotated = JsonHelpers.Bool(d, "rotated"),
    };

    private static Watcher ToWatcher(JsonObject d) => new()
    {
        WatcherId = JsonHelpers.Str(d, "watcher_id"),
        Url = JsonHelpers.Str(d, "url"),
        Status = JsonHelpers.Str(d, "status"),
        FrequencyMinutes = JsonHelpers.NumInt(d, "frequency_minutes"),
        DiffMode = JsonHelpers.Str(d, "diff_mode"),
        TrackFields = JsonHelpers.OptObj(d, "track_fields"),
        WebhookUrl = JsonHelpers.OptStr(d, "webhook_url"),
        NotifyEmail = JsonHelpers.NotFalse(d, "notify_email"),
        ConsecutiveErrors = JsonHelpers.NumInt(d, "consecutive_errors"),
        ChecksCount = JsonHelpers.NumInt(d, "checks_count"),
        LastCheckAt = JsonHelpers.OptStr(d, "last_check_at"),
        NextCheckAt = JsonHelpers.OptStr(d, "next_check_at"),
        LastChangeAt = JsonHelpers.OptStr(d, "last_change_at"),
        CreatedAt = JsonHelpers.OptStr(d, "created_at"),
        UpdatedAt = JsonHelpers.OptStr(d, "updated_at"),
    };

    private static WatcherSummary ToWatcherSummary(JsonObject d) => new()
    {
        WatcherId = JsonHelpers.Str(d, "watcher_id"),
        Url = JsonHelpers.Str(d, "url"),
        Status = JsonHelpers.Str(d, "status"),
        FrequencyMinutes = JsonHelpers.NumInt(d, "frequency_minutes"),
        ChecksCount = JsonHelpers.NumInt(d, "checks_count"),
        ConsecutiveErrors = JsonHelpers.NumInt(d, "consecutive_errors"),
        LastCheckAt = JsonHelpers.OptStr(d, "last_check_at"),
        NextCheckAt = JsonHelpers.OptStr(d, "next_check_at"),
        LastChangeAt = JsonHelpers.OptStr(d, "last_change_at"),
        CreatedAt = JsonHelpers.OptStr(d, "created_at"),
    };

    private static WatcherSnapshot ToWatcherSnapshot(JsonObject d) => new()
    {
        CheckedAt = JsonHelpers.Str(d, "checked_at"),
        HasChanges = JsonHelpers.Bool(d, "has_changes"),
        Similarity = JsonHelpers.OptNum(d, "similarity"),
        RenderQuality = JsonHelpers.OptNum(d, "render_quality"),
        ChangeCount = JsonHelpers.NumInt(d, "change_count"),
        Changes = JsonHelpers.ObjArr(d, "changes"),
    };

    private static string ListQuery(V2ListOptions? opts)
    {
        if (opts is null) return "";
        var parts = new List<string>();
        if (opts.Skip is not null) parts.Add($"skip={opts.Skip.Value}");
        if (opts.Limit is not null) parts.Add($"limit={opts.Limit.Value}");
        return parts.Count == 0 ? "" : $"?{string.Join("&", parts)}";
    }
}
