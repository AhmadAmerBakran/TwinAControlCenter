using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class SystemInfoService
{
    private readonly object _gate = new();
    private long _lastReceived;
    private long _lastSent;
    private DateTimeOffset _lastNetworkSample = DateTimeOffset.MinValue;
    private double _downMbps;
    private double _upMbps;
    private string _adapterName = "—";
    private string _adapterDescription = "—";
    private long _linkSpeed;

    public NetworkInfoDto SampleNetwork()
    {
        var adapter = SelectAdapter();
        if (adapter is null) return new NetworkInfoDto("—","No active physical Ethernet adapter detected","—",0,0);
        var stats = adapter.GetIPv4Statistics();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_lastNetworkSample != DateTimeOffset.MinValue)
            {
                var seconds = (now - _lastNetworkSample).TotalSeconds;
                if (seconds > 0.1)
                {
                    _downMbps = Math.Max(0, (stats.BytesReceived - _lastReceived) * 8d / seconds / 1_000_000d);
                    _upMbps = Math.Max(0, (stats.BytesSent - _lastSent) * 8d / seconds / 1_000_000d);
                }
            }
            _lastReceived = stats.BytesReceived; _lastSent = stats.BytesSent; _lastNetworkSample = now;
            _adapterName = adapter.Name; _adapterDescription = adapter.Description; _linkSpeed = adapter.Speed;
            return new NetworkInfoDto(_adapterName, _adapterDescription, FormatSpeed(_linkSpeed), Math.Round(_downMbps,2), Math.Round(_upMbps,2));
        }
    }

    public SystemDetailsDto GetDetails()
    {
        var network = SampleNetwork();
        var drives = DriveInfo.GetDrives().Where(d=>d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d=>new DriveInfoDto(d.Name,d.VolumeLabel,d.DriveFormat,d.TotalSize,d.AvailableFreeSpace)).ToArray();
        return new SystemDetailsDto(network, TimeSpan.FromMilliseconds(Environment.TickCount64), drives, Environment.MachineName, RuntimeInformation.OSDescription);
    }

    private static NetworkInterface? SelectAdapter()
    {
        var all = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Where(n => !ContainsVirtualName(n.Name) && !ContainsVirtualName(n.Description))
            .ToArray();
        return all.FirstOrDefault(n => n.Name.Equals("Ethernet", StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
               ?? all.OrderByDescending(n=>n.Speed).FirstOrDefault();
    }

    private static bool ContainsVirtualName(string value)
    {
        var v = value.ToLowerInvariant();
        return v.Contains("tailscale") || v.Contains("virtualbox") || v.Contains("vmware") || v.Contains("hyper-v") || v.Contains("vpn") || v.Contains("loopback");
    }

    private static string FormatSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000) return $"{bitsPerSecond/1_000_000_000d:0.#} Gbps";
        if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond/1_000_000d:0.#} Mbps";
        return $"{bitsPerSecond/1000d:0.#} Kbps";
    }
}
