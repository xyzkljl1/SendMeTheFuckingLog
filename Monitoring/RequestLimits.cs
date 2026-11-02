namespace SendMeTheFuckingLog.Monitoring;

public sealed class RequestLimits
{
    public long MaxRequestsPerHour { get; set; } = 10_000;
    public long MaxRequestsPerDay { get; set; } = 50_000;
    public long MaxRequestsPerWeek { get; set; } = 200_000;

    public void Validate()
    {
        foreach (var value in new[] { MaxRequestsPerHour, MaxRequestsPerDay, MaxRequestsPerWeek })
            if (value is < 1 or > 1_000_000_000_000)
                throw new InvalidOperationException("GlobalRateLimit values must be between 1 and 1000000000000.");
    }
}
