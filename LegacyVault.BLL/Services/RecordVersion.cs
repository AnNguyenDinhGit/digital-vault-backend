namespace LegacyVault.BLL.Services;

// UpdatedAt is also an optimistic concurrency token: it must change even within one clock tick.
internal static class RecordVersion
{
    public static DateTime Next(DateTime previous)
    {
        var now = DateTime.UtcNow;
        return now > previous ? now : previous.AddTicks(1);
    }
}
