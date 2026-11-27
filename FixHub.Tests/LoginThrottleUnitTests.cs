using FixHub.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FixHub.Tests;

public class LoginThrottleUnitTests
{
    private static LoginThrottle Create(int attempts = 3) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MaxLoginAttempts"] = attempts.ToString(),
                ["Security:LockoutMinutes"] = "15"
            })
            .Build());

    [Fact]
    public void Not_locked_before_limit_and_locked_at_limit()
    {
        var throttle = Create(attempts: 3);

        throttle.RegisterFailure("a@test.com");
        throttle.RegisterFailure("a@test.com");
        Assert.Null(throttle.GetLockRemaining("a@test.com"));

        throttle.RegisterFailure("a@test.com");
        var left = throttle.GetLockRemaining("a@test.com");

        Assert.NotNull(left);
        Assert.InRange(left!.Value.TotalMinutes, 14, 15);
    }

    [Fact]
    public void Reset_clears_failures_and_lock()
    {
        var throttle = Create(attempts: 2);
        throttle.RegisterFailure("a@test.com");
        throttle.RegisterFailure("a@test.com");
        Assert.NotNull(throttle.GetLockRemaining("a@test.com"));

        throttle.Reset("a@test.com");

        Assert.Null(throttle.GetLockRemaining("a@test.com"));
        throttle.RegisterFailure("a@test.com");
        Assert.Null(throttle.GetLockRemaining("a@test.com"));
    }

    [Fact]
    public void Different_keys_are_independent()
    {
        var throttle = Create(attempts: 1);

        throttle.RegisterFailure("a@test.com");

        Assert.NotNull(throttle.GetLockRemaining("a@test.com"));
        Assert.Null(throttle.GetLockRemaining("b@test.com"));
    }

    [Fact]
    public void Defaults_are_five_attempts()
    {
        var throttle = new LoginThrottle(new ConfigurationBuilder().Build());

        for (var i = 0; i < 4; i++) throttle.RegisterFailure("a@test.com");
        Assert.Null(throttle.GetLockRemaining("a@test.com"));

        throttle.RegisterFailure("a@test.com");
        Assert.NotNull(throttle.GetLockRemaining("a@test.com"));
    }
}
