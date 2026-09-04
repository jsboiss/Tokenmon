using Tokenmon.Core;

namespace Tokenmon.Core.Tests;

public sealed class CodexRateLimitServiceTests
{
    [Fact]
    public void ClassifiesWindowsByDurationInsteadOfProtocolPosition()
    {
        var response = """
            {"id":1,"result":{"rateLimits":{"primary":{"usedPercent":22,"windowDurationMins":10080,"resetsAt":1893456000},"secondary":{"usedPercent":44,"windowDurationMins":300,"resetsAt":1893456000}}}}
            """;

        var limits = CodexRateLimitService.ParseResponse(response);

        Assert.NotNull(limits);
        Assert.Equal(44, limits.FiveHour?.UsedPercent);
        Assert.Equal(22, limits.Weekly?.UsedPercent);
    }
}
