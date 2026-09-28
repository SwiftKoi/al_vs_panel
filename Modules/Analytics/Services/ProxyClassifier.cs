using System.Net;
using AlegacyWebPanel.Modules.Analytics.Configuration;
using Microsoft.Extensions.Options;

namespace AlegacyWebPanel.Modules.Analytics.Services;

public interface IProxyClassifier
{
    bool IsConfigured { get; }
    bool IsProxy(string address);
}

public sealed class ProxyClassifier : IProxyClassifier
{
    private readonly IReadOnlyList<IPNetwork> _networks;

    public ProxyClassifier(IOptions<AnalyticsOptions> options)
    {
        _networks = options.Value.ProxyAddresses
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .Select(entry => entry.Contains('/')
                ? IPNetwork.Parse(entry)
                : new IPNetwork(IPAddress.Parse(entry), IPAddress.Parse(entry).GetAddressBytes().Length * 8))
            .ToArray();
    }

    public bool IsConfigured => _networks.Count > 0;

    public bool IsProxy(string address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return _networks.Any(network => network.Contains(ip));
    }
}
