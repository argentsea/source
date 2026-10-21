namespace ArgentSea.Orleans.Pg;

/// <summary>
/// Every timestamp this package stores is UTC, in timestamptz columns. Npgsql rejects a DateTime that is not UTC for
/// timestamptz, so values are normalized before they are sent: a Utc value is unchanged, an Unspecified value is taken to be
/// UTC already (its clock reading is kept), and a Local value is converted to UTC. Values read back are Utc.
/// </summary>
internal static class UtcTime
{
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    internal static DateTimeOffset ToUtcOffset(DateTime value) => new(ToUtc(value), TimeSpan.Zero);
}
