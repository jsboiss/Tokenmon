using System.Runtime.CompilerServices;

namespace Tokenmon.Core;

public sealed class LocalUsageService(string userHome)
{
    public string UserHome { get; } = userHome;

    public async Task<UsageSnapshot> Read(CancellationToken cancellationToken = default)
    {
        return await Read(DateTimeOffset.Now.Date, cancellationToken);
    }

    public async Task<UsageSnapshot> ReadAllTime(CancellationToken cancellationToken = default)
    {
        return await Read(null, cancellationToken);
    }

    private async Task<UsageSnapshot> Read(
        DateTimeOffset? localStart,
        CancellationToken cancellationToken)
    {
        var reads = new[]
        {
            ReadProvider(
                "Codex",
                [Path.Combine(UserHome, ".codex", "sessions"), Path.Combine(UserHome, ".codex", "archived_sessions")],
                "*.jsonl",
                JsonUsageParsers.TryParseCodex,
                localStart,
                cancellationToken),
            ReadProvider(
                "Claude",
                [Path.Combine(UserHome, ".claude", "projects")],
                "*.jsonl",
                (string line, string _, out UsageEvent? usageEvent) =>
                    JsonUsageParsers.TryParseClaude(line, out usageEvent),
                localStart,
                cancellationToken),
            ReadProvider(
                "Gemini",
                [Path.Combine(UserHome, ".gemini", "tmp")],
                "*.jsonl",
                JsonUsageParsers.TryParseGemini,
                localStart,
                cancellationToken)
        };
        var providers = await Task.WhenAll(reads);
        return new UsageSnapshot(DateTimeOffset.Now, providers);
    }

    private static async Task<ProviderUsage> ReadProvider(
        string provider,
        IReadOnlyList<string> roots,
        string pattern,
        UsageParser parser,
        DateTimeOffset? localStart,
        CancellationToken cancellationToken)
    {
        try
        {
            var detected = roots.Any(Directory.Exists);
            var events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
            var sessions = 0;
            string? warning = null;
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, pattern, EnumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (localStart is not null && File.GetLastWriteTime(file) < localStart.Value.AddDays(-1))
                    {
                        continue;
                    }

                    var found = false;
                    try
                    {
                        await foreach (var line in ReadSharedLines(file, cancellationToken))
                        {
                            if (!parser(line, file, out var usageEvent) ||
                                usageEvent is null ||
                                localStart is not null && usageEvent.Timestamp.ToLocalTime().Date != localStart.Value)
                            {
                                continue;
                            }

                            events[usageEvent.Id] = usageEvent;
                            found = true;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        warning = exception.Message;
                    }

                    if (found)
                    {
                        sessions++;
                    }
                }
            }

            var usage = events.Values.Aggregate(
                new TokenUsage(0, 0, 0, 0),
                (x, y) => x + y.Usage);
            DateTimeOffset? lastActivity = events.Count == 0
                ? null
                : events.Values.Max(x => x.Timestamp);
            return new ProviderUsage(provider, usage, sessions, lastActivity, warning, detected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProviderUsage(
                provider,
                new TokenUsage(0, 0, 0, 0),
                0,
                null,
                exception.Message,
                roots.Any(Directory.Exists));
        }
    }

    private static async IAsyncEnumerable<string> ReadSharedLines(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            yield return line;
        }
    }

    private static EnumerationOptions EnumerationOptions => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    private delegate bool UsageParser(string line, string fallbackId, out UsageEvent? usageEvent);
}
