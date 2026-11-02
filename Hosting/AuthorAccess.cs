using System.Net;
using System.Net.Sockets;

namespace SendMeTheFuckingLog.Hosting;

public interface IDomainAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string domain, CancellationToken token);
}

public sealed class DomainAddressResolver : IDomainAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string domain, CancellationToken token) => Dns.GetHostAddressesAsync(domain, token);
}

public sealed class AuthorAccess(IConfiguration configuration, IDomainAddressResolver resolver,
    TimeProvider clock, ILogger<AuthorAccess> logger) : BackgroundService
{
    private sealed record Snapshot(HashSet<IPAddress> Addresses, DateTimeOffset ExpiresAt);
    private Snapshot snapshot = new([], DateTimeOffset.MinValue);
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    public bool Allows(IPAddress? address)
    {
        var current = Volatile.Read(ref snapshot);
        return address is not null && clock.GetUtcNow() < current.ExpiresAt && current.Addresses.Contains(Normalize(address));
    }

    internal async Task RefreshAsync(CancellationToken token)
    {
        var domains = configuration.GetSection("AuthorAccess:AllowedDomains").Get<string[]>() ?? [];
        if (domains.Length > 32 || domains.Any(domain => string.IsNullOrWhiteSpace(domain)
            || domain.Length > 253 || Uri.CheckHostName(domain) != UriHostNameType.Dns))
        {
            Volatile.Write(ref snapshot, new([], DateTimeOffset.MinValue));
            logger.LogWarning("AuthorAccess:AllowedDomains must contain at most 32 DNS hostnames.");
            return;
        }
        var resolved = await Task.WhenAll(domains.Distinct(StringComparer.OrdinalIgnoreCase).Select(async domain =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { return await resolver.ResolveAsync(domain, timeout.Token).WaitAsync(timeout.Token); }
            catch (Exception error) when (error is SocketException or OperationCanceledException or ArgumentException)
            {
                logger.LogWarning("Unable to resolve author access domain {Domain}; its addresses will not be allowed.", domain);
                return Array.Empty<IPAddress>();
            }
        }));
        token.ThrowIfCancellationRequested();
        var addresses = resolved.SelectMany(value => value)
            .Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Select(Normalize).ToHashSet();
        // Expiry also closes access if background refresh unexpectedly stops.
        Volatile.Write(ref snapshot, new(addresses, clock.GetUtcNow().AddMinutes(11)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(10), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
