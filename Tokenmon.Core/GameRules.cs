namespace Tokenmon.Core;

public enum ShopProduct
{
    Mint,
    RareCandy,
    ShinyCharm,
    PokemonEgg,
    UncommonEgg,
    RareEgg
}

public static class GameRules
{
    public const long RareCandyXp = 100_000_000;

    public static IReadOnlyList<string> Natures { get; } =
    [
        "Hardy", "Lonely", "Brave", "Adamant", "Naughty",
        "Bold", "Docile", "Relaxed", "Impish", "Lax",
        "Timid", "Hasty", "Serious", "Jolly", "Naive",
        "Modest", "Mild", "Quiet", "Bashful", "Rash",
        "Calm", "Gentle", "Sassy", "Careful", "Quirky"
    ];

    public static long Price(ShopProduct product)
    {
        return product switch
        {
            ShopProduct.Mint => 100_000_000,
            ShopProduct.RareCandy => 500_000_000,
            ShopProduct.ShinyCharm => 3_000_000_000,
            ShopProduct.PokemonEgg => 1_000_000_000,
            ShopProduct.UncommonEgg => 2_500_000_000,
            ShopProduct.RareEgg => 4_000_000_000,
            _ => throw new ArgumentOutOfRangeException(nameof(product))
        };
    }

    public static string RandomNature(string? excluding = null)
    {
        var candidates = string.IsNullOrWhiteSpace(excluding)
            ? Natures
            : Natures.Where(x => !string.Equals(x, excluding, StringComparison.Ordinal)).ToArray();
        return candidates[Random.Shared.Next(candidates.Count)];
    }

    public static bool TryPurchase(CompanionState state, ShopProduct product)
    {
        var price = Price(product);
        if (state.AvailableTokens < price || product == ShopProduct.ShinyCharm && state.HasShinyCharm)
        {
            return false;
        }

        state.SpentTokens += price;
        switch (product)
        {
            case ShopProduct.Mint:
                state.Mints++;
                break;
            case ShopProduct.RareCandy:
                state.RareCandy++;
                break;
            case ShopProduct.ShinyCharm:
                state.HasShinyCharm = true;
                break;
        }

        return true;
    }

    public static PokemonRarity? EggRarity(ShopProduct product)
    {
        return product switch
        {
            ShopProduct.PokemonEgg => null,
            ShopProduct.UncommonEgg => PokemonRarity.Uncommon,
            ShopProduct.RareEgg => PokemonRarity.Rare,
            _ => null
        };
    }

    public static int RarityRank(PokemonRarity rarity) => rarity switch
    {
        PokemonRarity.Common => 0,
        PokemonRarity.Uncommon => 1,
        PokemonRarity.Rare => 2,
        _ => 3
    };
}
