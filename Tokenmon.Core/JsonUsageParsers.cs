using System.Text.Json;

namespace Tokenmon.Core;

public static class JsonUsageParsers
{
    public static bool TryParseClaude(string line, out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (!line.Contains("\"assistant\"", StringComparison.Ordinal) ||
            !line.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (Text(root, "type") != "assistant" ||
                !TryObjectProperty(root, "message", out var message) ||
                !TryObjectProperty(message, "usage", out var usage) ||
                !TryTimestamp(root, out var timestamp))
            {
                return false;
            }

            var id = $"{Text(message, "id")}|{Text(root, "requestId")}";
            usageEvent = new UsageEvent(
                id,
                timestamp,
                new TokenUsage(
                    Number(usage, "input_tokens"),
                    Number(usage, "output_tokens"),
                    Number(usage, "cache_read_input_tokens"),
                    Number(usage, "cache_creation_input_tokens")));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseCodex(string line, string fallbackId, out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (!line.Contains("token_count", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryObjectProperty(root, "payload", out var payload) ||
                Text(payload, "type") != "token_count" ||
                !TryObjectProperty(payload, "info", out var info) ||
                !TryObjectProperty(info, "last_token_usage", out var usage) ||
                !TryTimestamp(root, out var timestamp))
            {
                return false;
            }

            var input = Number(usage, "input_tokens");
            var cached = Number(usage, "cached_input_tokens");
            var fingerprint = TryObjectProperty(info, "total_token_usage", out var total)
                ? total.GetRawText()
                : $"{timestamp:O}|{usage.GetRawText()}";
            usageEvent = new UsageEvent(
                $"codex|{fallbackId}|{fingerprint}",
                timestamp,
                new TokenUsage(
                    Math.Max(0, input - cached),
                    Number(usage, "output_tokens"),
                    cached,
                    0));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseGemini(string line, string fallbackId, out UsageEvent? usageEvent)
    {
        usageEvent = null;
        if (!line.Contains("\"tokens\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryObjectProperty(root, "tokens", out var tokens) || !TryTimestamp(root, out var timestamp))
            {
                return false;
            }

            var input = Number(tokens, "input");
            var cached = Number(tokens, "cached");
            var id = Text(root, "id");
            usageEvent = new UsageEvent(
                $"gemini|{fallbackId}|{(string.IsNullOrEmpty(id) ? timestamp.ToString("O") : id)}",
                timestamp,
                new TokenUsage(
                    Math.Max(0, input - cached) + Number(tokens, "tool"),
                    Number(tokens, "output") + Number(tokens, "thoughts"),
                    cached,
                    0));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long Number(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.TryGetInt64(out var result)
            ? Math.Max(0, result)
            : 0;
    }

    private static string Text(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool TryObjectProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = property;
        return true;
    }

    private static bool TryTimestamp(JsonElement element, out DateTimeOffset timestamp)
    {
        return DateTimeOffset.TryParse(
            Text(element, "timestamp"),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out timestamp);
    }
}

public sealed record UsageEvent(string Id, DateTimeOffset Timestamp, TokenUsage Usage);
