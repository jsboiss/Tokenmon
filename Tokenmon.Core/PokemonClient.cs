using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tokenmon.Core;

public sealed partial class PokemonClient(HttpClient httpClient, string cacheDirectory)
{
    public HttpClient HttpClient { get; } = httpClient;
    public string CacheDirectory { get; } = cacheDirectory;

    public async Task<PokemonLine> Hatch(
        PokemonRarity? minimumRarity = null,
        CancellationToken cancellationToken = default)
    {
        JsonDocument? selectedSpecies = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var id = Random.Shared.Next(1, 650);
            var species = await GetJson($"https://pokeapi.co/api/v2/pokemon-species/{id}", cancellationToken);
            var root = species.RootElement;
            var isBaseSpecies = root.GetProperty("evolves_from_species").ValueKind == JsonValueKind.Null;
            var captureRate = root.GetProperty("capture_rate").GetInt32();
            var rarity = Rarity(
                captureRate,
                root.GetProperty("is_legendary").GetBoolean(),
                root.GetProperty("is_mythical").GetBoolean());
            var meetsGuarantee = minimumRarity is null ||
                GameRules.RarityRank(rarity) >= GameRules.RarityRank(minimumRarity.Value);
            var acceptedByWeight = Random.Shared.Next(1, 256) <= Math.Max(1, captureRate);
            if (isBaseSpecies && meetsGuarantee && acceptedByWeight)
            {
                selectedSpecies = species;
                break;
            }

            species.Dispose();
        }

        var fallbackId = minimumRarity is PokemonRarity.Uncommon or PokemonRarity.Rare
            ? 147
            : 133;
        selectedSpecies ??= await GetJson(
            $"https://pokeapi.co/api/v2/pokemon-species/{fallbackId}",
            cancellationToken);
        using var selected = selectedSpecies;
        var selectedRoot = selected.RootElement;
        var selectedCaptureRate = selectedRoot.GetProperty("capture_rate").GetInt32();
        var legendary = selectedRoot.GetProperty("is_legendary").GetBoolean();
        var mythical = selectedRoot.GetProperty("is_mythical").GetBoolean();
        var chainUrl = selectedRoot.GetProperty("evolution_chain").GetProperty("url").GetString()
            ?? throw new InvalidDataException("PokéAPI returned no evolution chain.");

        using var chain = await GetJson(chainUrl, cancellationToken);
        var forms = new List<PokemonForm>();
        ReadEvolutionPath(chain.RootElement.GetProperty("chain"), forms);
        if (forms.Count == 0)
        {
            forms.Add(new PokemonForm(fallbackId, $"Pokémon #{fallbackId}", SpriteUrl(fallbackId)));
        }

        return new PokemonLine(forms, Rarity(selectedCaptureRate, legendary, mythical))
        {
            CaptureRate = selectedCaptureRate
        };
    }

    public async Task<string?> CacheSprite(
        PokemonForm form,
        bool shiny = false,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var path = Path.Combine(CacheDirectory, $"{form.Id}{(shiny ? "-shiny" : string.Empty)}.png");
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            var bytes = await HttpClient.GetByteArrayAsync(
                shiny ? form.ShinySpriteUrl : form.SpriteUrl,
                cancellationToken);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return path;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<JsonDocument> GetJson(string url, CancellationToken cancellationToken)
    {
        await using var stream = await HttpClient.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static void ReadEvolutionPath(JsonElement node, List<PokemonForm> forms)
    {
        var species = node.GetProperty("species");
        var url = species.GetProperty("url").GetString() ?? string.Empty;
        var match = SpeciesIdRegex().Match(url);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
        {
            var name = species.GetProperty("name").GetString() ?? $"Pokémon #{id}";
            forms.Add(new PokemonForm(id, DisplayName(name), SpriteUrl(id)));
        }

        var next = node.GetProperty("evolves_to");
        if (next.GetArrayLength() > 0)
        {
            ReadEvolutionPath(next[0], forms);
        }
    }

    private static PokemonRarity Rarity(int captureRate, bool legendary, bool mythical)
    {
        if (legendary || mythical)
        {
            return PokemonRarity.Legendary;
        }

        if (captureRate <= 45)
        {
            return PokemonRarity.Rare;
        }

        return captureRate <= 120 ? PokemonRarity.Uncommon : PokemonRarity.Common;
    }

    private static string SpriteUrl(int id) =>
        $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/versions/generation-v/black-white/{id}.png";

    private static string DisplayName(string name) =>
        string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    [GeneratedRegex(@"/pokemon-species/(\d+)/?$")]
    private static partial Regex SpeciesIdRegex();
}
