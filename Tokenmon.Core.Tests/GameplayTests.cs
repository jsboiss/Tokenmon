using Tokenmon.Core;

namespace Tokenmon.Core.Tests;

public sealed class GameplayTests
{
    [Fact]
    public void PurchasesUseLifetimeTokensAsWalletCurrency()
    {
        var state = new CompanionState
        {
            LifetimeTokens = 600_000_000
        };

        var purchased = GameRules.TryPurchase(state, ShopProduct.RareCandy);

        Assert.True(purchased);
        Assert.Equal(1, state.RareCandy);
        Assert.Equal(500_000_000, state.SpentTokens);
        Assert.Equal(100_000_000, state.AvailableTokens);
    }

    [Fact]
    public void HistoricalTokensFundWalletWithoutChangingGrowth()
    {
        var state = new CompanionState
        {
            LifetimeTokens = 20_000_000,
            HistoricalWalletTokens = 2_000_000_000,
            PhaseTokens = 15_000_000
        };

        var purchased = GameRules.TryPurchase(state, ShopProduct.PokemonEgg);

        Assert.True(purchased);
        Assert.Equal(1_020_000_000, state.AvailableTokens);
        Assert.Equal(15_000_000, state.PhaseTokens);
    }

    [Fact]
    public void ShinyCharmCanOnlyBePurchasedOnce()
    {
        var state = new CompanionState
        {
            LifetimeTokens = 10_000_000_000
        };

        Assert.True(GameRules.TryPurchase(state, ShopProduct.ShinyCharm));
        Assert.False(GameRules.TryPurchase(state, ShopProduct.ShinyCharm));
        Assert.Equal(3_000_000_000, state.SpentTokens);
    }

    [Fact]
    public void MigratesAnExistingMvpCompanionIntoTheCatchLog()
    {
        var state = ActiveCompanion();
        var engine = Engine();

        engine.EnsureMigrated(state);

        var caught = Assert.Single(state.Catches);
        Assert.Equal(state.CurrentCatchId, caught.Id);
        Assert.Equal("Vanillite", caught.Line.Forms[0].Name);
        Assert.Contains(state.Nature, GameRules.Natures);
    }

    [Fact]
    public void FreshEggRemovesAnUngraduatedCatch()
    {
        var state = ActiveCompanion();
        var engine = Engine();
        engine.EnsureMigrated(state);

        engine.StartFreshEgg(state, PokemonRarity.Rare);

        Assert.True(state.IsEgg);
        Assert.Empty(state.Catches);
        Assert.Equal(PokemonRarity.Rare, state.PendingEggRarity);
    }

    [Fact]
    public async Task FinalThresholdGraduatesAndStartsANewEgg()
    {
        var state = ActiveCompanion();
        state.Line = new PokemonLine(
            [new PokemonForm(132, "Ditto", "https://example.test/132.png")],
            PokemonRarity.Common);
        state.PhaseTokens = CompanionEngine.PhaseThreshold(PokemonRarity.Common, 1, 0) - 1;
        state.LastObservedDate = DateOnly.FromDateTime(DateTime.Now);
        state.LastObservedTodayTokens = 0;
        var engine = Engine();

        await engine.ApplyUsage(state, 1);

        Assert.True(state.IsEgg);
        Assert.Equal(1, state.Graduations);
        Assert.NotNull(Assert.Single(state.Catches).GraduatedAt);
    }

    private static CompanionState ActiveCompanion() => new()
    {
        IsEgg = false,
        Line = new PokemonLine(
            [
                new PokemonForm(582, "Vanillite", "https://example.test/582.png"),
                new PokemonForm(583, "Vanillish", "https://example.test/583.png"),
                new PokemonForm(584, "Vanilluxe", "https://example.test/584.png")
            ],
            PokemonRarity.Rare)
    };

    private static CompanionEngine Engine() => new(
        new PokemonClient(new HttpClient(), Path.Combine(Path.GetTempPath(), "tokenmon-gameplay-tests")));
}
