using Tokenmon.Core;

namespace Tokenmon.Core.Tests;

public sealed class CompanionEngineTests
{
    [Fact]
    public void CommonThreeStageThresholdsSumToGraduationTotal()
    {
        var thresholds = Enumerable.Range(0, 3)
            .Select(x => CompanionEngine.PhaseThreshold(PokemonRarity.Common, 3, x))
            .ToArray();

        Assert.Equal(750_000_000, thresholds.Sum());
        Assert.True(thresholds[0] < thresholds[1]);
        Assert.True(thresholds[1] < thresholds[2]);
    }

    [Fact]
    public void EggUsesFiveMillionTokenThreshold()
    {
        Assert.Equal(5_000_000, CompanionEngine.CurrentThreshold(new CompanionState()));
    }
}
