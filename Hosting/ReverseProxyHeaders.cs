using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;

namespace SendMeTheFuckingLog.Hosting;

// Accept one explicitly trusted HTTP proxy. This class never forwards requests.
public sealed class ReverseProxyHeaders
{
    private readonly HashSet<IPAddress> peers = [];
    public bool Enabled { get; }
    public ForwardedHeadersOptions Options { get; } = new()
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
        RequireHeaderSymmetry = true
    };

    public ReverseProxyHeaders(IConfiguration configuration)
    {
        Enabled = configuration.GetValue<bool>("ReverseProxy:Enabled");
        if (!Enabled) return;
        var addresses = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        if (addresses.Length is < 1 or > 32)
            throw new InvalidOperationException("ReverseProxy:KnownProxies requires 1 to 32 explicit proxy IP addresses.");
        Options.KnownProxies.Clear();
        Options.KnownIPNetworks.Clear();
        foreach (var text in addresses)
        {
            if (!IPAddress.TryParse(text, out var address)
                || (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0))
                throw new InvalidOperationException("ReverseProxy:KnownProxies only accepts IP addresses without a scope ID.");
            address = Normalize(address);
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast
                || (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224))
                throw new InvalidOperationException("ReverseProxy:KnownProxies requires individual unicast IP addresses.");
            peers.Add(address);
            Options.KnownProxies.Add(address);
            if (address.AddressFamily == AddressFamily.InterNetwork)
                Options.KnownProxies.Add(address.MapToIPv6());
        }
    }

    public bool IsLocalHealthRequest(HttpContext context)
    {
        if (context.Request.Path != "/healthz" || !HttpMethods.IsGet(context.Request.Method)
            || context.Connection.RemoteIpAddress is not { } peer || !IPAddress.IsLoopback(Normalize(peer)))
            return false;
        // A forwarded loopback address must never turn a public request into a liveness probe.
        return !Enabled || (context.Request.Host.Host == "localhost"
            && !context.Request.Headers.Keys.Any(name => name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)));
    }

    public int ValidateRequest(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } peer || !peers.Contains(Normalize(peer)))
            return StatusCodes.Status403Forbidden;
        var clients = context.Request.Headers.GetCommaSeparatedValues("X-Forwarded-For");
        var schemes = context.Request.Headers.GetCommaSeparatedValues("X-Forwarded-Proto");
        if (clients.Length is < 1 or > 32 || !IPAddress.TryParse(clients[^1], out _)
            || schemes.Length != 1 || schemes[0] is not ("http" or "https"))
            return StatusCodes.Status400BadRequest;
        // The nearest proxy must append/overwrite the client IP. Earlier entries may be visitor supplied.
        context.Request.Headers["X-Forwarded-For"] = clients[^1];
        return StatusCodes.Status200OK;
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
