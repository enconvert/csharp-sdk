namespace Enconvert;

/// <summary>
/// Format tables mirroring the gateway's CONVERTER_MAP (api/v1/convert.py).
///
/// <see cref="ImplementedConversions"/> is the client-side gate: the gateway
/// returns 503 for any "{input}-to-{output}" endpoint not in its
/// CONVERTER_MAP, so unsupported pairs are rejected here with a useful
/// message instead of paying a network round-trip for a guaranteed failure.
/// </summary>
public static class Formats
{
    /// <summary>The 43 implemented "{input}-to-{output}" conversion endpoints.</summary>
    public static readonly IReadOnlySet<string> ImplementedConversions = new HashSet<string>
    {
        // Structured text (13)
        "json-to-xml",
        "xml-to-json",
        "json-to-yaml",
        "yaml-to-json",
        "csv-to-json",
        "json-to-csv",
        "json-to-toml",
        "toml-to-json",
        "csv-to-xml",
        "xml-to-csv",
        "markdown-to-html",
        "markdown-to-pdf",
        "html-to-pdf",
        // Documents (9) — EPUB→PDF now flows through anything-to-pdf, not a dedicated pair.
        "doc-to-pdf",
        "excel-to-pdf",
        "ppt-to-pdf",
        "odt-to-pdf",
        "ods-to-pdf",
        "odp-to-pdf",
        "ots-to-pdf",
        "pages-to-pdf",
        "numbers-to-pdf",
        // Images (21)
        "jpeg-to-png",
        "png-to-jpeg",
        "jpeg-to-svg",
        "svg-to-jpeg",
        "jpeg-to-heic",
        "heic-to-jpeg",
        "jpeg-to-webp",
        "webp-to-jpeg",
        "png-to-svg",
        "svg-to-png",
        "png-to-heic",
        "heic-to-png",
        "png-to-webp",
        "webp-to-png",
        "svg-to-heic",
        "heic-to-svg",
        "svg-to-webp",
        "webp-to-svg",
        "heic-to-webp",
        "webp-to-heic",
        "pdf-to-jpeg",
    };

    // Extension -> API format name (input side)
    internal static readonly IReadOnlyDictionary<string, string> ImageFormats = new Dictionary<string, string>
    {
        [".jpg"] = "jpeg",
        [".jpeg"] = "jpeg",
        [".png"] = "png",
        [".svg"] = "svg",
        [".heic"] = "heic",
        [".webp"] = "webp",
        // PDF is an image input solely for pdf-to-jpeg (rasterization).
        [".pdf"] = "pdf",
    };

    internal static readonly IReadOnlyDictionary<string, string> DocumentFormats = new Dictionary<string, string>
    {
        [".doc"] = "doc",
        [".docx"] = "doc",
        [".xls"] = "excel",
        [".xlsx"] = "excel",
        [".ppt"] = "ppt",
        [".pptx"] = "ppt",
        [".html"] = "html",
        [".htm"] = "html",
        [".odt"] = "odt",
        [".ods"] = "ods",
        [".odp"] = "odp",
        [".ots"] = "ots",
        [".pages"] = "pages",
        [".numbers"] = "numbers",
        // .epub has no dedicated document pair — use ConvertToPdfAsync / ConvertToMarkdownAsync.
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".csv"] = "csv",
        [".json"] = "json",
        [".xml"] = "xml",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".toml"] = "toml",
    };

    private static readonly IReadOnlyDictionary<string, string> MimeByExt = new Dictionary<string, string>
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".heic"] = "image/heic",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".odt"] = "application/vnd.oasis.opendocument.text",
        [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".odp"] = "application/vnd.oasis.opendocument.presentation",
        [".epub"] = "application/epub+zip",
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".toml"] = "application/toml",
    };

    // Common aliases users pass that differ from the API's canonical format names.
    private static readonly IReadOnlyDictionary<string, string> OutputFormatAliases = new Dictionary<string, string>
    {
        ["jpg"] = "jpeg",
        ["yml"] = "yaml",
        ["htm"] = "html",
        ["md"] = "markdown",
    };

    /// <summary>Returns the lowercased extension of <paramref name="name"/> (including the leading dot), or "" if none.</summary>
    public static string ExtOf(string name)
    {
        var i = name.LastIndexOf('.');
        return i == -1 ? "" : name[i..].ToLowerInvariant();
    }

    /// <summary>Maps a filename's extension to its MIME type, defaulting to application/octet-stream.</summary>
    public static string MimeFor(string name) =>
        MimeByExt.TryGetValue(ExtOf(name), out var mime) ? mime : "application/octet-stream";

    /// <summary>Maps a filename's extension to its API input format, or throws.</summary>
    internal static string ResolveInputFormat(string name, IReadOnlyDictionary<string, string> map)
    {
        var ext = ExtOf(name);
        if (!map.TryGetValue(ext, out var format))
        {
            var supported = string.Join(", ", map.Keys.Distinct().OrderBy(k => k, StringComparer.Ordinal));
            throw new ArgumentException($"Unsupported file extension '{ext}'. Supported: {supported}");
        }

        return format;
    }

    /// <summary>Lowercases, strips a single leading dot, and resolves aliases (jpg, yml, htm, md).</summary>
    public static string NormalizeOutputFormat(string format)
    {
        var f = format.ToLowerInvariant();
        if (f.StartsWith('.')) f = f[1..];
        return OutputFormatAliases.TryGetValue(f, out var alias) ? alias : f;
    }

    /// <summary>Lists the output formats the API implements for a given input format.</summary>
    public static IReadOnlyList<string> ValidOutputsFor(string inputFormat)
    {
        var prefix = $"{inputFormat}-to-";
        return ImplementedConversions
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Asserts "{input}-to-{output}" is an implemented endpoint and returns its
    /// name. Throws with the list of valid outputs for that input otherwise.
    /// </summary>
    internal static string AssertConversionImplemented(string inputFormat, string outputFormat)
    {
        var endpoint = $"{inputFormat}-to-{outputFormat}";
        if (!ImplementedConversions.Contains(endpoint))
        {
            var outputs = ValidOutputsFor(inputFormat);
            var hint = outputs.Count > 0
                ? $"Supported outputs for '{inputFormat}': {string.Join(", ", outputs)}"
                : $"No conversions are available for input format '{inputFormat}'";
            throw new ArgumentException($"Conversion '{inputFormat}' to '{outputFormat}' is not supported. {hint}.");
        }

        return endpoint;
    }
}
