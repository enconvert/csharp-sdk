using System.Text.Json.Nodes;

namespace Enconvert.Json;

/// <summary>
/// Guarded field extraction over <see cref="JsonObject"/>, mirroring the Node
/// SDK's str/optStr/num/optNum/strArr/optObj helpers. Every accessor is safe
/// against absent fields (the API uses response_model_exclude_none, so
/// optional fields may be missing entirely) and against unexpected types.
/// </summary>
internal static class JsonHelpers
{
    public static string Str(JsonObject o, string key, string fallback = "")
    {
        return TryGetString(o, key, out var s) ? s : fallback;
    }

    public static string? OptStr(JsonObject o, string key)
    {
        return TryGetString(o, key, out var s) ? s : null;
    }

    public static double Num(JsonObject o, string key, double fallback = 0)
    {
        return TryGetDouble(o, key, out var n) ? n : fallback;
    }

    public static double? OptNum(JsonObject o, string key)
    {
        return TryGetDouble(o, key, out var n) ? n : null;
    }

    public static int NumInt(JsonObject o, string key, int fallback = 0)
    {
        return TryGetDouble(o, key, out var n) ? (int)n : fallback;
    }

    public static int? OptNumInt(JsonObject o, string key)
    {
        return TryGetDouble(o, key, out var n) ? (int)n : null;
    }

    public static bool Bool(JsonObject o, string key)
    {
        return o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    }

    /// <summary>True unless the field is present and literally the JSON boolean false (mirrors "field !== false").</summary>
    public static bool NotFalse(JsonObject o, string key)
    {
        return !(o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b == false);
    }

    public static List<string> StrArr(JsonObject o, string key)
    {
        var result = new List<string>();
        if (o[key] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonValue v && v.TryGetValue<string>(out var s)) result.Add(s);
            }
        }

        return result;
    }

    public static JsonObject? OptObj(JsonObject o, string key)
    {
        return o[key] is JsonObject sub ? sub : null;
    }

    public static List<JsonObject> ObjArr(JsonObject o, string key)
    {
        var result = new List<JsonObject>();
        if (o[key] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject sub) result.Add(sub);
            }
        }

        return result;
    }

    public static Dictionary<string, int> NumDict(JsonObject o, string key)
    {
        var result = new Dictionary<string, int>();
        if (o[key] is JsonObject sub)
        {
            foreach (var (k, v) in sub)
            {
                if (v is JsonValue jv && jv.TryGetValue<double>(out var n)) result[k] = (int)n;
            }
        }

        return result;
    }

    private static bool TryGetString(JsonObject o, string key, out string value)
    {
        if (o[key] is JsonValue v && v.TryGetValue<string>(out var s))
        {
            value = s;
            return true;
        }

        value = "";
        return false;
    }

    private static bool TryGetDouble(JsonObject o, string key, out double value)
    {
        if (o[key] is JsonValue v && v.TryGetValue<double>(out var n))
        {
            value = n;
            return true;
        }

        value = 0;
        return false;
    }
}
