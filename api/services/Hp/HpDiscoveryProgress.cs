namespace api.Services.Hp;

/// <summary>Live progress of one discovery run. Written by <see cref="HpPrinterDiscoveryService"/>, read by <c>GET /Hp/GetDiscover</c> while it runs.</summary>
public sealed class HpDiscoveryProgress
{
    public const string PhaseMdns = "mdns";
    public const string PhaseSubnetScan = "subnet-scan";
    public const string PhaseInspecting = "inspecting";

    private volatile string? _phase;
    private int _hostsToProbe;
    private int _hostsProbed;
    private int _devicesToInspect;
    private int _devicesInspected;

    /// <summary><c>mdns</c>, <c>subnet-scan</c> or <c>inspecting</c>; null before the run starts.</summary>
    public string? Phase => _phase;
    public int HostsToProbe => Volatile.Read(ref _hostsToProbe);
    public int HostsProbed => Volatile.Read(ref _hostsProbed);
    public int DevicesToInspect => Volatile.Read(ref _devicesToInspect);
    public int DevicesInspected => Volatile.Read(ref _devicesInspected);

    public void SetPhase(string phase) => _phase = phase;
    public void AddHostsToProbe(int count) => Interlocked.Add(ref _hostsToProbe, count);
    public void HostProbed() => Interlocked.Increment(ref _hostsProbed);
    public void SetDevicesToInspect(int count) => Volatile.Write(ref _devicesToInspect, count);
    public void DeviceInspected() => Interlocked.Increment(ref _devicesInspected);
}
