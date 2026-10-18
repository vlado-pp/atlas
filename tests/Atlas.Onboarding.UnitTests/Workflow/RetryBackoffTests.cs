using Atlas.Onboarding.Api.Workflow;

namespace Atlas.Onboarding.UnitTests.Workflow;

public sealed class RetryBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 10)]
    [InlineData(3, 40)]
    public void For_GrowsExponentiallyWithinJitter(int attempt, double expectedSeconds)
    {
        var delay = RetryBackoff.For(attempt, Base, Max, new Random(42));

        delay.TotalSeconds.ShouldBeInRange(expectedSeconds * 0.8, expectedSeconds * 1.2);
    }

    [Fact]
    public void For_ManyAttempts_IsCappedAtTheMaximum()
    {
        var delay = RetryBackoff.For(50, Base, Max, new Random(42));

        delay.ShouldBeLessThanOrEqualTo(Max * 1.2);
        delay.ShouldBeGreaterThanOrEqualTo(Max * 0.8);
    }
}
