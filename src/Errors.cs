namespace Enconvert;

/// <summary>Base type for every exception raised by the Enconvert SDK.</summary>
public class EnconvertException : Exception
{
    public EnconvertException(string message)
        : base(message)
    {
    }
}

/// <summary>An error returned by the Enconvert API (HTTP status &gt;= 400).</summary>
public class ApiException : EnconvertException
{
    /// <summary>The HTTP status code that produced this error.</summary>
    public int StatusCode { get; }

    public ApiException(int statusCode, string message)
        : base($"[{statusCode}] {message}")
    {
        StatusCode = statusCode;
    }
}

/// <summary>Thrown for HTTP 401/403 responses. Always reports <see cref="ApiException.StatusCode"/> 401.</summary>
public class AuthenticationException : ApiException
{
    public AuthenticationException(string message = "Invalid or missing API key")
        : base(401, message)
    {
    }
}

/// <summary>Thrown for HTTP 429 (rate limit exceeded) responses.</summary>
public class RateLimitException : ApiException
{
    public RateLimitException(string message = "Rate limit exceeded")
        : base(429, message)
    {
    }
}

/// <summary>
/// Thrown for HTTP 402 responses: a plan feature is disabled or the monthly
/// quota is exhausted. Raised by V2 endpoints in particular.
/// </summary>
public class QuotaException : ApiException
{
    public QuotaException(string message = "Plan feature not enabled or quota exhausted")
        : base(402, message)
    {
    }
}
