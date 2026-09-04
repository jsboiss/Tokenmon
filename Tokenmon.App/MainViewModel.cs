using System.ComponentModel;
using System.IO;
using System.Net.Http;
using Tokenmon.Core;

namespace Tokenmon.App;

public sealed class MainViewModel(
    LocalUsageService usageService,
    CodexRateLimitService rateLimitService,
    CompanionEngine companionEngine,
    JsonStateStore stateStore) : INotifyPropertyChanged
{
    public LocalUsageService UsageService { get; } = usageService;
    public CodexRateLimitService RateLimitService { get; } = rateLimitService;
    public CompanionEngine CompanionEngine { get; } = companionEngine;
    public JsonStateStore StateStore { get; } = stateStore;
    public CompanionState Companion { get; } = stateStore.LoadCompanion();
    public AppSettings Settings { get; } = stateStore.LoadSettings();
    public SemaphoreSlim RefreshLock { get; } = new(1, 1);

    public UsageSnapshot Snapshot { get; private set; } = new(
        DateTimeOffset.Now,
        [
            new ProviderUsage("Codex", new TokenUsage(0, 0, 0, 0), 0, null),
            new ProviderUsage("Claude", new TokenUsage(0, 0, 0, 0), 0, null)
        ]);

    public bool IsRefreshing { get; private set; }
    public CodexRateLimits? CodexLimits { get; private set; }
    public string StatusText { get; private set; } = "Reading local usage…";
    public string? SpritePath { get; private set; }
    public string CompanionName => Companion.IsEgg
        ? Companion.PendingEggRarity is { } rarity ? $"{rarity} Egg" : "Mysterious Egg"
        : $"{(Companion.IsShiny ? "✨ " : string.Empty)}{Companion.CurrentForm?.Name ?? "Unknown"}";
    public string CompanionCaption => Companion.IsEgg
        ? $"{TokenFormatter.Compact(Math.Max(0, CompanionEngine.EggHatchThreshold - Companion.PhaseTokens))} until hatching"
        : $"{Companion.Line?.Rarity} · {Companion.Nature} · Stage {Companion.FormIndex + 1}/{Companion.Line?.Forms.Count}";
    public string TodayTokens => TokenFormatter.Compact(Snapshot.Today.Total);
    public string InputTokens => TokenFormatter.Compact(Snapshot.Today.Input);
    public string OutputTokens => TokenFormatter.Compact(Snapshot.Today.Output);
    public string CacheTokens => TokenFormatter.Compact(Snapshot.Today.CacheRead + Snapshot.Today.CacheWrite);
    public string ProgressLabel => $"{TokenFormatter.Compact(Companion.PhaseTokens)} / {TokenFormatter.Compact(CompanionEngine.CurrentThreshold(Companion))}";
    public double ProgressPercent => Math.Clamp(
        Companion.PhaseTokens * 100d / Math.Max(1, CompanionEngine.CurrentThreshold(Companion)),
        0,
        100);
    public bool IsEgg => Companion.IsEgg;
    public string WalletTokens => TokenFormatter.Compact(Companion.AvailableTokens);
    public string LifetimeTokens => TokenFormatter.Compact(Companion.LifetimeTokens);
    public string RareCandyCount => Companion.RareCandy.ToString();
    public string MintCount => Companion.Mints.ToString();
    public string ShinyCharmStatus => Companion.HasShinyCharm ? "Active · shiny odds 1/48" : "Not owned · shiny odds 1/64";
    public string HistoricalWalletLabel => Companion.HistoricalWalletImportedAt is null
        ? "Historical import pending"
        : $"Includes {TokenFormatter.Compact(Companion.HistoricalWalletTokens)} historical tokens";
    public IReadOnlyList<ProviderUsage> VisibleProviders => Snapshot.Providers.Where(x => x.IsDetected).ToArray();
    public int CollectionCount => PokedexEntries.Count;
    public int GraduationCount => Companion.Graduations;
    public IReadOnlyList<CaughtPokemon> PokedexEntries => Companion.Catches
        .GroupBy(x => x.FinalForm?.Id ?? 0)
        .Select(x => x.OrderByDescending(y => y.CapturedAt).First())
        .OrderBy(x => x.FinalForm?.Id ?? int.MaxValue)
        .ToArray();
    public IReadOnlyList<CatchCard> CatchLog => Companion.Catches
        .OrderByDescending(x => x.CapturedAt)
        .Select(ToCatchCard)
        .ToArray();
    public bool CanBuyMint => Companion.AvailableTokens >= GameRules.Price(ShopProduct.Mint);
    public bool CanBuyCandy => Companion.AvailableTokens >= GameRules.Price(ShopProduct.RareCandy);
    public bool CanBuyCharm => !Companion.HasShinyCharm && Companion.AvailableTokens >= GameRules.Price(ShopProduct.ShinyCharm);
    public bool CanBuyPokemonEgg => Companion.AvailableTokens >= GameRules.Price(ShopProduct.PokemonEgg);
    public bool CanBuyUncommonEgg => Companion.AvailableTokens >= GameRules.Price(ShopProduct.UncommonEgg);
    public bool CanBuyRareEgg => Companion.AvailableTokens >= GameRules.Price(ShopProduct.RareEgg);
    public bool HasCodexLimits => CodexLimits?.FiveHour is not null || CodexLimits?.Weekly is not null;
    public int FiveHourPercent => CodexLimits?.FiveHour?.UsedPercent ?? 0;
    public int WeeklyPercent => CodexLimits?.Weekly?.UsedPercent ?? 0;
    public string FiveHourReset => CodexLimits?.FiveHour?.ResetLabel ?? string.Empty;
    public string WeeklyReset => CodexLimits?.Weekly?.ResetLabel ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsChanged;
    public event EventHandler<string>? CompanionEvent;

    public async Task Refresh(CancellationToken cancellationToken = default)
    {
        if (!await RefreshLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            StatusText = "Refreshing…";
            Notify(nameof(IsRefreshing), nameof(StatusText));
            Snapshot = await UsageService.Read(cancellationToken);
            var previousForm = Companion.CurrentForm?.Id;
            var previousWasEgg = Companion.IsEgg;
            var previousGraduations = Companion.Graduations;
            await CompanionEngine.ApplyUsage(Companion, Snapshot.Today.Total, cancellationToken);
            PublishCompanionEvent(previousForm, previousWasEgg, previousGraduations);
            var importedTokens = await ImportHistoricalWallet(cancellationToken);
            try
            {
                CodexLimits = await RateLimitService.Read(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                CodexLimits = null;
            }
            SpritePath = Companion.CurrentForm is null
                ? null
                : await CompanionEngine.PokemonClient.CacheSprite(
                    Companion.CurrentForm,
                    Companion.IsShiny,
                    cancellationToken);
            await StateStore.SaveCompanion(Companion, cancellationToken);
            StatusText = importedTokens > 0
                ? $"Imported {TokenFormatter.Compact(importedTokens)} historical tokens into your wallet"
                : $"Updated {Snapshot.RefreshedAt:t}";
            NotifyAll();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            StatusText = exception is OperationCanceledException
                ? "Refresh cancelled"
                : "Offline — showing saved companion";
            Notify(nameof(StatusText));
        }
        catch (Exception exception)
        {
            StatusText = $"Refresh failed: {exception.GetType().Name}";
            Notify(nameof(StatusText));
            Directory.CreateDirectory(StateStore.DirectoryPath);
            await File.WriteAllTextAsync(
                Path.Combine(StateStore.DirectoryPath, "last-error.log"),
                exception.ToString(),
                CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
            Notify(nameof(IsRefreshing));
            RefreshLock.Release();
        }
    }

    public async Task SaveSettings()
    {
        await StateStore.SaveSettings(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> UseRareCandy()
    {
        var previousForm = Companion.CurrentForm?.Id;
        var previousWasEgg = Companion.IsEgg;
        var previousGraduations = Companion.Graduations;
        if (!await CompanionEngine.UseRareCandy(Companion))
        {
            StatusText = "No Rare Candy in your Bag";
            Notify(nameof(StatusText));
            return false;
        }

        await RefreshCompanionSprite();
        PublishCompanionEvent(previousForm, previousWasEgg, previousGraduations);
        StatusText = "Rare Candy added 100M growth";
        await SaveGame();
        return true;
    }

    public async Task<bool> UseMint()
    {
        if (!CompanionEngine.UseMint(Companion))
        {
            StatusText = Companion.IsEgg ? "Mints can only be used after hatching" : "No Mint in your Bag";
            Notify(nameof(StatusText));
            return false;
        }

        StatusText = $"Your companion is now {Companion.Nature}";
        await SaveGame();
        return true;
    }

    public async Task<bool> Buy(ShopProduct product)
    {
        if (!CompanionEngine.Buy(Companion, product))
        {
            StatusText = product == ShopProduct.ShinyCharm && Companion.HasShinyCharm
                ? "The Shiny Charm is already active"
                : "Not enough token currency";
            Notify(nameof(StatusText));
            return false;
        }

        if (product is ShopProduct.PokemonEgg or ShopProduct.UncommonEgg or ShopProduct.RareEgg)
        {
            SpritePath = null;
        }

        StatusText = $"Purchased {ProductName(product)}";
        await SaveGame();
        return true;
    }

    private async Task SaveGame()
    {
        await StateStore.SaveCompanion(Companion);
        NotifyAll();
    }

    private async Task RefreshCompanionSprite()
    {
        SpritePath = Companion.CurrentForm is null
            ? null
            : await CompanionEngine.PokemonClient.CacheSprite(Companion.CurrentForm, Companion.IsShiny);
    }

    private async Task<long> ImportHistoricalWallet(CancellationToken cancellationToken)
    {
        if (Companion.HistoricalWalletImportedAt is not null)
        {
            return 0;
        }

        StatusText = "Importing historical usage into your wallet…";
        Notify(nameof(StatusText));
        var allTime = await UsageService.ReadAllTime(cancellationToken);
        if (allTime.Providers.Where(x => x.IsDetected).Any(x => x.Error is not null))
        {
            return 0;
        }

        var credit = Math.Max(0, allTime.Today.Total - Companion.LifetimeTokens);
        Companion.HistoricalWalletTokens = credit;
        Companion.HistoricalWalletImportedAt = DateTimeOffset.Now;
        return credit;
    }

    private static string ProductName(ShopProduct product) => product switch
    {
        ShopProduct.RareCandy => "Rare Candy",
        ShopProduct.ShinyCharm => "Shiny Charm",
        ShopProduct.PokemonEgg => "Pokémon Egg",
        ShopProduct.UncommonEgg => "Uncommon Egg",
        ShopProduct.RareEgg => "Rare Egg",
        _ => product.ToString()
    };

    private CatchCard ToCatchCard(CaughtPokemon caught)
    {
        var form = caught.Id == Companion.CurrentCatchId
            ? Companion.CurrentForm ?? caught.FinalForm
            : caught.FinalForm;
        var fileName = form is null
            ? null
            : $"{form.Id}{(caught.IsShiny ? "-shiny" : string.Empty)}.png";
        var cached = fileName is null
            ? null
            : Path.Combine(StateStore.DirectoryPath, "sprites", fileName);
        var sprite = cached is not null && File.Exists(cached)
            ? cached
            : caught.IsShiny ? form?.ShinySpriteUrl : form?.SpriteUrl;
        var displayName = $"{(caught.IsShiny ? "✨ " : string.Empty)}{form?.Name ?? caught.DisplayName}";
        return new CatchCard(displayName, caught.Nature, caught.Status, sprite);
    }

    private void PublishCompanionEvent(int? previousForm, bool previousWasEgg, int previousGraduations)
    {
        if (Companion.Graduations > previousGraduations)
        {
            CompanionEvent?.Invoke(this, "A companion graduated! A fresh egg has arrived.");
            return;
        }

        if (previousWasEgg && !Companion.IsEgg)
        {
            CompanionEvent?.Invoke(
                this,
                $"{(Companion.IsShiny ? "✨ Shiny " : string.Empty)}{Companion.CurrentForm?.Name} hatched!");
            return;
        }

        if (previousForm is not null && previousForm != Companion.CurrentForm?.Id)
        {
            CompanionEvent?.Invoke(this, $"Your companion evolved into {Companion.CurrentForm?.Name}!");
        }
    }

    private void NotifyAll()
    {
        Notify(
            nameof(Snapshot), nameof(CompanionName), nameof(CompanionCaption),
            nameof(TodayTokens), nameof(InputTokens), nameof(OutputTokens), nameof(CacheTokens),
            nameof(ProgressLabel), nameof(ProgressPercent), nameof(IsEgg), nameof(SpritePath),
            nameof(StatusText), nameof(WalletTokens), nameof(LifetimeTokens),
            nameof(RareCandyCount), nameof(MintCount), nameof(ShinyCharmStatus),
            nameof(CollectionCount), nameof(GraduationCount), nameof(PokedexEntries),
            nameof(CatchLog), nameof(CanBuyMint), nameof(CanBuyCandy), nameof(CanBuyCharm),
            nameof(CanBuyPokemonEgg), nameof(CanBuyUncommonEgg), nameof(CanBuyRareEgg),
            nameof(HasCodexLimits), nameof(FiveHourPercent), nameof(WeeklyPercent),
            nameof(FiveHourReset), nameof(WeeklyReset), nameof(HistoricalWalletLabel),
            nameof(VisibleProviders));
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

public sealed record CatchCard(string DisplayName, string Nature, string Status, string? SpriteUrl);
