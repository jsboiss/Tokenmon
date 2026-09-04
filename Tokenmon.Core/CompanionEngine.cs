namespace Tokenmon.Core;

public sealed class CompanionEngine(PokemonClient pokemonClient)
{
    public const long EggHatchThreshold = 5_000_000;
    public PokemonClient PokemonClient { get; } = pokemonClient;

    public async Task<bool> ApplyUsage(
        CompanionState state,
        long todayTokens,
        CancellationToken cancellationToken = default)
    {
        EnsureMigrated(state);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var gained = state.LastObservedDate == today
            ? Math.Max(0, todayTokens - state.LastObservedTodayTokens)
            : Math.Max(0, todayTokens);
        state.LastObservedDate = today;
        state.LastObservedTodayTokens = todayTokens;
        state.LifetimeTokens += gained;
        state.PhaseTokens += gained;

        return await Advance(state, gained > 0, cancellationToken);
    }

    public async Task<bool> UseRareCandy(
        CompanionState state,
        CancellationToken cancellationToken = default)
    {
        EnsureMigrated(state);
        if (state.RareCandy <= 0)
        {
            return false;
        }

        state.RareCandy--;
        state.PhaseTokens += GameRules.RareCandyXp;
        await Advance(state, true, cancellationToken);
        return true;
    }

    public bool UseMint(CompanionState state)
    {
        EnsureMigrated(state);
        if (state.Mints <= 0 || state.IsEgg)
        {
            return false;
        }

        state.Mints--;
        state.Nature = GameRules.RandomNature(state.Nature);
        var caught = state.Catches.FirstOrDefault(x => x.Id == state.CurrentCatchId);
        if (caught is not null)
        {
            caught.Nature = state.Nature;
        }

        return true;
    }

    public bool Buy(CompanionState state, ShopProduct product)
    {
        EnsureMigrated(state);
        if (!GameRules.TryPurchase(state, product))
        {
            return false;
        }

        if (product is ShopProduct.PokemonEgg or ShopProduct.UncommonEgg or ShopProduct.RareEgg)
        {
            StartFreshEgg(state, GameRules.EggRarity(product));
        }

        return true;
    }

    public void StartFreshEgg(CompanionState state, PokemonRarity? minimumRarity = null)
    {
        if (state.CurrentCatchId is { } currentId)
        {
            var current = state.Catches.FirstOrDefault(x => x.Id == currentId);
            if (current is not null && current.GraduatedAt is null)
            {
                state.Catches.Remove(current);
            }
        }

        state.IsEgg = true;
        state.Line = null;
        state.FormIndex = 0;
        state.PhaseTokens = 0;
        state.Nature = string.Empty;
        state.IsShiny = false;
        state.CurrentCatchId = null;
        state.PendingEggRarity = minimumRarity;
    }

    public void EnsureMigrated(CompanionState state)
    {
        state.Catches ??= [];
        if (state.IsEgg || state.Line is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(state.Nature))
        {
            state.Nature = GameRules.RandomNature();
        }

        if (state.CurrentCatchId is not null && state.Catches.Any(x => x.Id == state.CurrentCatchId))
        {
            return;
        }

        var caught = new CaughtPokemon
        {
            Line = state.Line,
            Nature = state.Nature,
            IsShiny = state.IsShiny
        };
        state.Catches.Add(caught);
        state.CurrentCatchId = caught.Id;
    }

    public static long PhaseThreshold(PokemonRarity rarity, int forms, int formIndex)
    {
        var total = rarity switch
        {
            PokemonRarity.Common => 750_000_000L,
            PokemonRarity.Uncommon => 1_875_000_000L,
            PokemonRarity.Rare => 3_000_000_000L,
            _ => 6_000_000_000L
        };
        var count = Math.Max(1, forms);
        var denominator = count * (count + 1) / 2d;
        return (long)Math.Round(total * (formIndex + 1) / denominator);
    }

    public static long CurrentThreshold(CompanionState state)
    {
        if (state.IsEgg || state.Line is null)
        {
            return EggHatchThreshold;
        }

        return PhaseThreshold(state.Line.Rarity, state.Line.Forms.Count, state.FormIndex);
    }

    private async Task<bool> Advance(
        CompanionState state,
        bool changed,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (state.IsEgg)
            {
                if (state.PhaseTokens < EggHatchThreshold)
                {
                    return changed;
                }

                state.PhaseTokens -= EggHatchThreshold;
                await Hatch(state, cancellationToken);
                changed = true;
                continue;
            }

            if (state.Line is null)
            {
                return changed;
            }

            var threshold = PhaseThreshold(state.Line.Rarity, state.Line.Forms.Count, state.FormIndex);
            if (state.PhaseTokens < threshold)
            {
                return changed;
            }

            state.PhaseTokens -= threshold;
            if (state.FormIndex < state.Line.Forms.Count - 1)
            {
                state.FormIndex++;
                changed = true;
                continue;
            }

            Graduate(state);
            changed = true;
        }
    }

    private async Task Hatch(CompanionState state, CancellationToken cancellationToken)
    {
        state.Line = await PokemonClient.Hatch(state.PendingEggRarity, cancellationToken);
        state.FormIndex = 0;
        state.IsEgg = false;
        state.PendingEggRarity = null;
        state.Nature = GameRules.RandomNature();
        state.IsShiny = Random.Shared.Next(state.HasShinyCharm ? 48 : 64) == 0;
        var caught = new CaughtPokemon
        {
            Line = state.Line,
            Nature = state.Nature,
            IsShiny = state.IsShiny
        };
        state.Catches.Add(caught);
        state.CurrentCatchId = caught.Id;
    }

    private static void Graduate(CompanionState state)
    {
        var caught = state.Catches.FirstOrDefault(x => x.Id == state.CurrentCatchId);
        if (caught is not null)
        {
            caught.GraduatedAt = DateTimeOffset.Now;
        }

        state.Graduations++;
        state.IsEgg = true;
        state.Line = null;
        state.FormIndex = 0;
        state.Nature = string.Empty;
        state.IsShiny = false;
        state.CurrentCatchId = null;
        state.PendingEggRarity = null;
    }
}
