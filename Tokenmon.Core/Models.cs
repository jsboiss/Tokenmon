using System.Text.Json.Serialization;

namespace Tokenmon.Core;

public sealed record TokenUsage(long Input, long Output, long CacheRead, long CacheWrite)
{
    public long Total => Input + Output + CacheRead + CacheWrite;

    public static TokenUsage operator +(TokenUsage x, TokenUsage y) => new(
        x.Input + y.Input,
        x.Output + y.Output,
        x.CacheRead + y.CacheRead,
        x.CacheWrite + y.CacheWrite);
}

public sealed record ProviderUsage(
    string Provider,
    TokenUsage Today,
    int Sessions,
    DateTimeOffset? LastActivity,
    string? Error = null,
    bool IsDetected = false);

public sealed record UsageSnapshot(DateTimeOffset RefreshedAt, IReadOnlyList<ProviderUsage> Providers)
{
    public TokenUsage Today => Providers.Aggregate(
        new TokenUsage(0, 0, 0, 0),
        (x, y) => x + y.Today);
}

public enum PokemonRarity
{
    Common,
    Uncommon,
    Rare,
    Legendary
}

public sealed record PokemonForm(int Id, string Name, string SpriteUrl)
{
    [JsonIgnore]
    public string ShinySpriteUrl =>
        $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/shiny/{Id}.png";
}

public sealed record PokemonLine(IReadOnlyList<PokemonForm> Forms, PokemonRarity Rarity)
{
    public int CaptureRate { get; init; } = 255;
}

public sealed class CaughtPokemon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PokemonLine Line { get; set; } = new([], PokemonRarity.Common);
    public string Nature { get; set; } = "Hardy";
    public bool IsShiny { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? GraduatedAt { get; set; }

    [JsonIgnore]
    public PokemonForm? FinalForm => Line.Forms.LastOrDefault();

    [JsonIgnore]
    public string DisplayName => $"{(IsShiny ? "✨ " : string.Empty)}{FinalForm?.Name ?? "Unknown"}";

    [JsonIgnore]
    public string? SpriteUrl => IsShiny ? FinalForm?.ShinySpriteUrl : FinalForm?.SpriteUrl;

    [JsonIgnore]
    public string Status => GraduatedAt is null ? "Raising" : $"Graduated {GraduatedAt.Value.LocalDateTime:d}";
}

public sealed class CompanionState
{
    public bool IsEgg { get; set; } = true;
    public PokemonLine? Line { get; set; }
    public int FormIndex { get; set; }
    public long PhaseTokens { get; set; }
    public long LifetimeTokens { get; set; }
    public long LastObservedTodayTokens { get; set; }
    public DateOnly LastObservedDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public string Nature { get; set; } = string.Empty;
    public bool IsShiny { get; set; }
    public Guid? CurrentCatchId { get; set; }
    public List<CaughtPokemon> Catches { get; set; } = [];
    public int RareCandy { get; set; }
    public int Mints { get; set; }
    public bool HasShinyCharm { get; set; }
    public long SpentTokens { get; set; }
    public long HistoricalWalletTokens { get; set; }
    public DateTimeOffset? HistoricalWalletImportedAt { get; set; }
    public int Graduations { get; set; }
    public PokemonRarity? PendingEggRarity { get; set; }

    [JsonIgnore]
    public PokemonForm? CurrentForm => Line is null
        ? null
        : Line.Forms[Math.Clamp(FormIndex, 0, Line.Forms.Count - 1)];

    [JsonIgnore]
    public long AvailableTokens => Math.Max(0, LifetimeTokens + HistoricalWalletTokens - SpentTokens);
}

public sealed class AppSettings
{
    public bool ShowFloatingPet { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public int RefreshMinutes { get; set; } = 3;
    public double PetLeft { get; set; } = double.NaN;
    public double PetTop { get; set; } = double.NaN;
    public double PetSize { get; set; } = 112;
}
