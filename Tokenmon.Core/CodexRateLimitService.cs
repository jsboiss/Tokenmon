using System.Diagnostics;
using System.Text.Json;

namespace Tokenmon.Core;

public sealed record RateLimitWindow(int UsedPercent, int? WindowMinutes, DateTimeOffset? ResetsAt)
{
    public string ResetLabel
    {
        get
        {
            if (ResetsAt is null)
            {
                return string.Empty;
            }

            var remaining = ResetsAt.Value - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                return "reset pending";
            }

            return remaining.TotalDays >= 1
                ? $"resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
                : $"resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }
    }
}

public sealed record CodexRateLimits(RateLimitWindow? FiveHour, RateLimitWindow? Weekly);

public sealed class CodexRateLimitService(string userProfile)
{
    public string UserProfile { get; } = userProfile;

    public async Task<CodexRateLimits?> Read(CancellationToken cancellationToken = default)
    {
        var binary = ResolveBinary();
        if (binary is null)
        {
            return null;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = "app-server",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var errorRead = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteLineAsync("""{"method":"initialize","id":0,"params":{"clientInfo":{"name":"tokenmon","title":"Tokenmon","version":"0.2.0"},"capabilities":{"experimentalApi":true}}}""");
        await process.StandardInput.WriteLineAsync("""{"method":"initialized","params":{}}""");
        await process.StandardInput.WriteLineAsync("""{"method":"account/rateLimits/read","id":1,"params":{}}""");
        await process.StandardInput.FlushAsync(cancellationToken);

        var responseRead = ReadResponse(process, cancellationToken);
        var completed = await Task.WhenAny(
            responseRead,
            Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
        if (completed != responseRead)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryStop(process);
            return null;
        }

        var response = await responseRead;
        TryStop(process);
        await errorRead;
        return response;
    }

    private static async Task<CodexRateLimits?> ReadResponse(
        Process process,
        CancellationToken cancellationToken)
    {
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            var result = ParseResponse(line);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    public static CodexRateLimits? ParseResponse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1 ||
                !root.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("rateLimits", out var limits))
            {
                return null;
            }

            var windows = new[]
            {
                ReadWindow(limits, "primary"),
                ReadWindow(limits, "secondary")
            }.Where(x => x is not null).Cast<RateLimitWindow>().ToArray();
            var fiveHour = windows.FirstOrDefault(x => x.WindowMinutes is > 0 and <= 360);
            var weekly = windows.FirstOrDefault(x => x.WindowMinutes is >= 1_000);
            if (fiveHour is null && weekly is null && windows.Length > 0)
            {
                fiveHour = windows[0];
            }

            return new CodexRateLimits(fiveHour, weekly);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ResolveBinary()
    {
        var npmBinary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@openai", "codex", "node_modules",
            "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc",
            "codex", "codex.exe");
        if (File.Exists(npmBinary))
        {
            return npmBinary;
        }

        var localBinary = Path.Combine(UserProfile, ".codex", "bin", "codex.exe");
        return File.Exists(localBinary) ? localBinary : null;
    }

    private static RateLimitWindow? ReadWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var used = window.TryGetProperty("usedPercent", out var usedValue) ? usedValue.GetInt32() : 0;
        int? minutes = window.TryGetProperty("windowDurationMins", out var minutesValue) &&
            minutesValue.ValueKind == JsonValueKind.Number
                ? minutesValue.GetInt32()
                : null;
        DateTimeOffset? reset = window.TryGetProperty("resetsAt", out var resetValue) &&
            resetValue.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(resetValue.GetInt64())
                : null;
        return new RateLimitWindow(Math.Clamp(used, 0, 100), minutes, reset);
    }

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
