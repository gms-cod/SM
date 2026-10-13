using StartupMonitor.Service.Services.Firewall;
using Xunit;

namespace StartupMonitor.Tests.Services;

public sealed class PortDefenderEscalationTests
{
    [Theory]
    [InlineData(1, 15, false)]
    [InlineData(2, 30, false)]
    [InlineData(3, 60, false)]
    [InlineData(4, 1440, false)] // 24 часа = 1440 минут
    [InlineData(5, 0, true)]     // Permanent
    [InlineData(6, 0, true)]     // 6+ также Permanent
    [InlineData(10, 0, true)]
    public void CalculateBanDuration_StandardEscalationLevels_CalculatesExpectedDuration(
        int repeatCount,
        int expectedMinutes,
        bool expectPermanent)
    {
        var result = PortDefenderEscalation.CalculateBanDuration(repeatCount);

        if (expectPermanent)
        {
            Assert.True(result.IsPermanent);
            Assert.Null(result.Duration);
        }
        else
        {
            Assert.False(result.IsPermanent);
            Assert.NotNull(result.Duration);
            Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), result.Duration.Value);
        }
    }

    [Fact]
    public void RecordViolation_SequentialViolationsWithin24Hours_EscalatesCorrectly()
    {
        var policy = new PortDefenderEscalation(
            baseBanDuration: TimeSpan.FromMinutes(15),
            resetPeriod: TimeSpan.FromHours(24));

        var ip = "185.220.101.5";
        var t0 = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

        // 1-й бан: 15 минут
        var ban1 = policy.RecordViolation(ip, t0);
        Assert.Equal(1, ban1.RepeatCount);
        Assert.False(ban1.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(15), ban1.Duration);
        Assert.Equal(t0.AddMinutes(15), ban1.ExpiresAtUtc);

        // 2-й бан спустя 20 минут (после истечения первого бана, но < 24 часов): удвоение (30 минут)
        var t1 = t0.AddMinutes(20);
        var ban2 = policy.RecordViolation(ip, t1);
        Assert.Equal(2, ban2.RepeatCount);
        Assert.False(ban2.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(30), ban2.Duration);
        Assert.Equal(t1.AddMinutes(30), ban2.ExpiresAtUtc);

        // 3-й бан спустя 1 час: удвоение (60 минут)
        var t2 = t1.AddHours(1);
        var ban3 = policy.RecordViolation(ip, t2);
        Assert.Equal(3, ban3.RepeatCount);
        Assert.False(ban3.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(60), ban3.Duration);
        Assert.Equal(t2.AddMinutes(60), ban3.ExpiresAtUtc);

        // 4-й бан спустя 2 часа: 24 часа
        var t3 = t2.AddHours(2);
        var ban4 = policy.RecordViolation(ip, t3);
        Assert.Equal(4, ban4.RepeatCount);
        Assert.False(ban4.IsPermanent);
        Assert.Equal(TimeSpan.FromHours(24), ban4.Duration);
        Assert.Equal(t3.AddHours(24), ban4.ExpiresAtUtc);

        // 5-й бан спустя 2 часа: постоянный (Permanent)
        var t4 = t3.AddHours(2);
        var ban5 = policy.RecordViolation(ip, t4);
        Assert.Equal(5, ban5.RepeatCount);
        Assert.True(ban5.IsPermanent);
        Assert.Null(ban5.Duration);
        Assert.Null(ban5.ExpiresAtUtc);
    }

    [Fact]
    public void RecordViolation_CooldownExceeds24Hours_ResetsRepeatCountToBase()
    {
        var policy = new PortDefenderEscalation(
            baseBanDuration: TimeSpan.FromMinutes(15),
            resetPeriod: TimeSpan.FromHours(24));

        var ip = "45.142.214.9";
        var t0 = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);

        // 1-й бан
        var ban1 = policy.RecordViolation(ip, t0);
        Assert.Equal(1, ban1.RepeatCount);
        Assert.Equal(TimeSpan.FromMinutes(15), ban1.Duration);

        // 2-й бан через 1 час (внутри 24 часов) -> эскалация до 30 мин
        var ban2 = policy.RecordViolation(ip, t0.AddHours(1));
        Assert.Equal(2, ban2.RepeatCount);
        Assert.Equal(TimeSpan.FromMinutes(30), ban2.Duration);

        // 3-й бан через 26 часов после последнего (превышает 24 часа затишья)
        // -> счетчик рецидивов должен сброситься к 1, а срок — к базовому (15 мин)
        var tReset = t0.AddHours(1).AddHours(26);
        var ban3 = policy.RecordViolation(ip, tReset);
        Assert.Equal(1, ban3.RepeatCount);
        Assert.False(ban3.IsPermanent);
        Assert.Equal(TimeSpan.FromMinutes(15), ban3.Duration);
        Assert.Equal(tReset.AddMinutes(15), ban3.ExpiresAtUtc);
    }

    [Fact]
    public void GetRepeatCount_ReturnsZeroWhenExpiredOrUnknown()
    {
        var policy = new PortDefenderEscalation(resetPeriod: TimeSpan.FromHours(24));
        var ip = "91.240.118.4";
        var t0 = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, policy.GetRepeatCount(ip, t0));

        policy.RecordViolation(ip, t0);
        Assert.Equal(1, policy.GetRepeatCount(ip, t0.AddHours(5)));

        // Спустя 25 часов без нарушений
        Assert.Equal(0, policy.GetRepeatCount(ip, t0.AddHours(25)));
    }

    [Fact]
    public void ManualResetAndClear_ClearsViolationState()
    {
        var policy = new PortDefenderEscalation();
        var ip = "194.26.29.112";

        policy.RecordViolation(ip);
        Assert.Equal(1, policy.GetRepeatCount(ip));

        var reset = policy.Reset(ip);
        Assert.True(reset);
        Assert.Equal(0, policy.GetRepeatCount(ip));

        policy.RecordViolation("1.1.1.1");
        policy.RecordViolation("2.2.2.2");
        policy.Clear();

        Assert.Equal(0, policy.GetRepeatCount("1.1.1.1"));
        Assert.Equal(0, policy.GetRepeatCount("2.2.2.2"));
    }
}
