using Tokenmon.Core;

namespace Tokenmon.Core.Tests;

public sealed class JsonUsageParsersTests
{
    [Fact]
    public void ParsesClaudeUsage()
    {
        var line = """
            {"type":"assistant","timestamp":"2026-08-22T01:02:03Z","requestId":"r1","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":300,"cache_creation_input_tokens":40}}}
            """;

        var parsed = JsonUsageParsers.TryParseClaude(line, out var usageEvent);

        Assert.True(parsed);
        Assert.NotNull(usageEvent);
        Assert.Equal(460, usageEvent.Usage.Total);
        Assert.Equal("m1|r1", usageEvent.Id);
    }

    [Fact]
    public void DeductsCachedInputFromCodexInput()
    {
        var line = """
            {"timestamp":"2026-08-22T01:02:03Z","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50},"total_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50}}}}
            """;

        var parsed = JsonUsageParsers.TryParseCodex(line, "rollout.jsonl", out var usageEvent);

        Assert.True(parsed);
        Assert.NotNull(usageEvent);
        Assert.Equal(200, usageEvent.Usage.Input);
        Assert.Equal(800, usageEvent.Usage.CacheRead);
        Assert.Equal(50, usageEvent.Usage.Output);
    }

    [Fact]
    public void IgnoresCodexTokenCountWithNullInfo()
    {
        var line = """
            {"timestamp":"2025-10-14T01:02:03Z","payload":{"type":"token_count","info":null}}
            """;

        var parsed = JsonUsageParsers.TryParseCodex(line, "legacy-rollout.jsonl", out var usageEvent);

        Assert.False(parsed);
        Assert.Null(usageEvent);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1_200, "1.2K")]
    [InlineData(2_000_000, "2M")]
    [InlineData(3_100_000_000, "3.1B")]
    public void FormatsTokenCounts(long value, string expected)
    {
        Assert.Equal(expected, TokenFormatter.Compact(value));
    }

    [Fact]
    public void ParsesGeminiToolAndThoughtTokens()
    {
        var line = """
            {"id":"g1","timestamp":"2026-08-22T01:02:03Z","tokens":{"input":1000,"cached":600,"tool":50,"output":100,"thoughts":25}}
            """;

        var parsed = JsonUsageParsers.TryParseGemini(line, "session.jsonl", out var usageEvent);

        Assert.True(parsed);
        Assert.NotNull(usageEvent);
        Assert.Equal(450, usageEvent.Usage.Input);
        Assert.Equal(600, usageEvent.Usage.CacheRead);
        Assert.Equal(125, usageEvent.Usage.Output);
    }
}
