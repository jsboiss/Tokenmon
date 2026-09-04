using System.Text;
using Tokenmon.Core;

namespace Tokenmon.Core.Tests;

public sealed class LocalUsageServiceTests
{
    [Fact]
    public async Task ReadsAnActivelyOpenCodexRollout()
    {
        var home = Path.Combine(Path.GetTempPath(), $"tokenmon-tests-{Guid.NewGuid():N}");
        var sessions = Path.Combine(home, ".codex", "sessions");
        Directory.CreateDirectory(sessions);
        var path = Path.Combine(sessions, "rollout-test.jsonl");
        var timestamp = DateTimeOffset.Now.ToString("O");
        var line = """
            {"timestamp":"$timestamp","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50},"total_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50}}}}
            """.Replace("$timestamp", timestamp, StringComparison.Ordinal);

        try
        {
            await using var writer = new FileStream(
                path,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete);
            await writer.WriteAsync(Encoding.UTF8.GetBytes(line));
            await writer.FlushAsync();

            var snapshot = await new LocalUsageService(home).Read();

            var codex = Assert.Single(snapshot.Providers, x => x.Provider == "Codex");
            Assert.Equal(1_050, codex.Today.Total);
            Assert.Equal(1, codex.Sessions);
            Assert.Null(codex.Error);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task AllTimeReadIncludesUsageFromPreviousDays()
    {
        var home = Path.Combine(Path.GetTempPath(), $"tokenmon-history-tests-{Guid.NewGuid():N}");
        var sessions = Path.Combine(home, ".codex", "sessions");
        Directory.CreateDirectory(sessions);
        var path = Path.Combine(sessions, "rollout-old.jsonl");
        var timestamp = DateTimeOffset.Now.AddDays(-30).ToString("O");
        var line = """
            {"timestamp":"$timestamp","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50},"total_token_usage":{"input_tokens":1000,"cached_input_tokens":800,"output_tokens":50}}}}
            """.Replace("$timestamp", timestamp, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, line);

        try
        {
            var service = new LocalUsageService(home);
            var today = await service.Read();
            var allTime = await service.ReadAllTime();

            Assert.Equal(0, today.Today.Total);
            Assert.Equal(1_050, allTime.Today.Total);
            Assert.True(Assert.Single(allTime.Providers, x => x.Provider == "Codex").IsDetected);
            Assert.False(Assert.Single(allTime.Providers, x => x.Provider == "Gemini").IsDetected);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
