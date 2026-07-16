using System.Text.Json.Nodes;
using Enconvert.Json;

namespace Enconvert;

/// <summary>Shared internal helpers used by both the V1 client and the V2 namespace.</summary>
internal static class Internal
{
    /// <summary>Reads the response body as a mutable JSON object (empty object if the body is empty or isn't a JSON object).</summary>
    public static async Task<JsonObject> ParseJsonObjectAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(text)) return new JsonObject();
        return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }

    /// <summary>
    /// Maps an HTTP error response (status &gt;= 400) to the SDK exception
    /// hierarchy. Extracts a message from the JSON "detail" or "error"
    /// field, falling back to the raw body text or "HTTP &lt;status&gt;".
    /// </summary>
    public static async Task RaiseForStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (status < 400) return;

        var message = await ExtractErrorMessageAsync(response, status, cancellationToken).ConfigureAwait(false);

        if (status == 401 || status == 403) throw new AuthenticationException(message);
        if (status == 402) throw new QuotaException(message);
        if (status == 429) throw new RateLimitException(message);
        throw new ApiException(status, message);
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, int status, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return $"HTTP {status}";
        }

        if (string.IsNullOrEmpty(text)) return $"HTTP {status}";

        try
        {
            if (JsonNode.Parse(text) is JsonObject body)
            {
                if (body["detail"] is JsonValue detailValue && detailValue.TryGetValue<string>(out var detail) && detail.Length > 0)
                {
                    return detail;
                }

                if (body["detail"] is { } detailNode) return detailNode.ToJsonString();

                if (body["error"] is JsonValue errorValue && errorValue.TryGetValue<string>(out var error) && error.Length > 0)
                {
                    return error;
                }

                if (body["error"] is { } errorNode) return errorNode.ToJsonString();

                return body.ToJsonString();
            }
        }
        catch
        {
            // Not JSON — fall through to the raw text.
        }

        return text;
    }

    /// <summary>Serializes PDF options to the API's snake_case wire format, omitting unset fields.</summary>
    public static JsonObject SerializePdfOptions(PdfOptions o)
    {
        var outObj = new JsonObject();
        if (o.PageSize is not null) outObj["page_size"] = o.PageSize;
        if (o.PageWidth is not null) outObj["page_width"] = o.PageWidth;
        if (o.PageHeight is not null) outObj["page_height"] = o.PageHeight;
        if (o.Orientation is not null) outObj["orientation"] = o.Orientation;
        if (o.Margins is not null)
        {
            var m = new JsonObject();
            if (o.Margins.Top is not null) m["top"] = o.Margins.Top;
            if (o.Margins.Bottom is not null) m["bottom"] = o.Margins.Bottom;
            if (o.Margins.Left is not null) m["left"] = o.Margins.Left;
            if (o.Margins.Right is not null) m["right"] = o.Margins.Right;
            outObj["margins"] = m;
        }

        if (o.Scale is not null) outObj["scale"] = o.Scale;
        if (o.Grayscale is not null) outObj["grayscale"] = o.Grayscale;
        if (o.Header is not null) outObj["header"] = SerializeHeaderFooter(o.Header);
        if (o.Footer is not null) outObj["footer"] = SerializeHeaderFooter(o.Footer);
        return outObj;
    }

    private static JsonObject SerializeHeaderFooter(PdfHeaderFooter hf)
    {
        var o = new JsonObject();
        if (hf.Content is not null) o["content"] = hf.Content;
        if (hf.Height is not null) o["height"] = hf.Height;
        return o;
    }

    /// <summary>Generates a UUIDv4 with dashes removed (32 hex chars), matching the API's job_id shape.</summary>
    public static string NewJobId() => Guid.NewGuid().ToString("N");

    public static JsonObject ToJson(this HttpBasicAuth auth) => new()
    {
        ["username"] = auth.Username,
        ["password"] = auth.Password,
    };

    /// <summary>
    /// Cookie fields are forwarded to the browser context as-is (matching
    /// Playwright's own cookie shape), so httpOnly/sameSite stay camelCase
    /// on the wire even though the rest of the API is snake_case.
    /// </summary>
    public static JsonObject ToJson(this BrowserCookie cookie)
    {
        var o = new JsonObject { ["name"] = cookie.Name, ["value"] = cookie.Value };
        if (cookie.Domain is not null) o["domain"] = cookie.Domain;
        if (cookie.Url is not null) o["url"] = cookie.Url;
        if (cookie.Path is not null) o["path"] = cookie.Path;
        if (cookie.Expires is not null) o["expires"] = cookie.Expires;
        if (cookie.HttpOnly is not null) o["httpOnly"] = cookie.HttpOnly;
        if (cookie.Secure is not null) o["secure"] = cookie.Secure;
        if (cookie.SameSite is not null) o["sameSite"] = cookie.SameSite;
        return o;
    }

    /// <summary>Attaches the plan-gated auth/cookies/headers fields to a request body when provided.</summary>
    public static void AppendBrowserAccess(
        JsonObject body,
        HttpBasicAuth? auth,
        IReadOnlyList<BrowserCookie>? cookies,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (auth is not null) body["auth"] = auth.ToJson();
        if (cookies is not null)
        {
            var arr = new JsonArray();
            foreach (var cookie in cookies) arr.Add(cookie.ToJson());
            body["cookies"] = arr;
        }

        if (headers is not null)
        {
            var obj = new JsonObject();
            foreach (var (key, value) in headers) obj[key] = value;
            body["headers"] = obj;
        }
    }
}
