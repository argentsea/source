using FluentAssertions;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>Every timestamp the package stores is UTC: Utc is unchanged, Unspecified is taken as UTC, Local is converted.</summary>
public class UtcTimeTests
{
    private static readonly DateTime Utc = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public void AUtcValueIsUnchanged()
    {
        UtcTime.ToUtc(Utc).Should().Be(Utc).And.Subject!.Value.Kind.Should().Be(DateTimeKind.Utc);
        UtcTime.ToUtcOffset(Utc).Should().Be(new DateTimeOffset(Utc)).And.Subject!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public void AnUnspecifiedValueKeepsItsClockReadingAndBecomesUtc()
    {
        var unspecified = DateTime.SpecifyKind(Utc, DateTimeKind.Unspecified);

        var result = UtcTime.ToUtc(unspecified);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Ticks.Should().Be(unspecified.Ticks, "no clock shift is applied");
    }

    [Fact]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public void ALocalValueIsConvertedToTheSameInstantInUtc()
    {
        // Use a time zone offset that is never zero, so the test means something whatever the machine's zone is.
        var zone = TimeZoneInfo.CreateCustomTimeZone("ArgentSeaTest+5", TimeSpan.FromHours(5), "ArgentSeaTest+5", "ArgentSeaTest+5");
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(Utc, zone), DateTimeKind.Local);
        var machineLocal = Utc.ToLocalTime();

        UtcTime.ToUtc(machineLocal).Should().Be(Utc, "a machine-local time converts back to the same instant");
        UtcTime.ToUtc(machineLocal).Kind.Should().Be(DateTimeKind.Utc);
        UtcTime.ToUtcOffset(machineLocal).Offset.Should().Be(TimeSpan.Zero, "Npgsql accepts only a zero offset for timestamptz");
        UtcTime.ToUtc(local).Should().Be(local.ToUniversalTime(), "conversion follows the machine's local zone, as DateTime.ToUniversalTime does");
    }
}
