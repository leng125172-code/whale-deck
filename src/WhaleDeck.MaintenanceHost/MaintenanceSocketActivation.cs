using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;

namespace WhaleDeck.MaintenanceHost;

public static class MaintenanceSocketActivation
{
    private const string DescriptorName = "whaledeck-maintenance-http";
    private static readonly string[] Variables = ["LISTEN_PID", "LISTEN_FDS", "LISTEN_FDNAMES"];

    public static bool Configure(WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["Maintenance:SocketActivation"] ?? "disabled";
        if (mode is not ("required" or "disabled")) throw Invalid();
        if (mode == "disabled")
        {
            // Direct binding remains available for development/test hosts only
            // when no inherited activation context is present.
            if (Variables.Any(name => Environment.GetEnvironmentVariable(name) is not null)) throw Invalid();
            return false;
        }
        if (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("LISTEN_PID") != Environment.ProcessId.ToString(CultureInfo.InvariantCulture) ||
            Environment.GetEnvironmentVariable("LISTEN_FDS") != "2" ||
            Environment.GetEnvironmentVariable("LISTEN_FDNAMES") != DescriptorName + ":" + DescriptorName) throw Invalid();

        // Validate the entire inherited set before handing either descriptor
        // to Kestrel. Do not assume a manager-specific descriptor order.
        var ports = new HashSet<int>();
        for (var descriptor = 3; descriptor <= 4; descriptor++)
        {
            if (Option(descriptor, 3) != 1 || Option(descriptor, 30) != 1 ||
                Option(descriptor, 39) != 2 || Option(descriptor, 38) != 6) throw Invalid();
            uint size = 16;
            if (getsockname(descriptor, out var address, ref size) != 0 || size != 16 ||
                address.Family != 2 || address.Address != 0) throw Invalid();
            var port = (ushort)IPAddress.NetworkToHostOrder(unchecked((short)address.Port));
            if (port is not (8080 or 8081) || !ports.Add(port)) throw Invalid();
        }
        for (var descriptor = 3; descriptor <= 4; descriptor++)
        {
            var flags = fcntl(descriptor, 1, 0);
            if (flags < 0 || fcntl(descriptor, 2, flags | 1) != 0) throw Invalid();
        }
        foreach (var variable in Variables) Environment.SetEnvironmentVariable(variable, null);
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Kestrel owns this process's descriptors. PID 1 keeps independent
            // references to the same listeners throughout a service restart.
            options.ListenHandle(3);
            options.ListenHandle(4);
        });
        return true;
    }

    private static int Option(int descriptor, int option)
    {
        uint size = sizeof(int);
        if (getsockopt(descriptor, 1, option, out var value, ref size) != 0 || size != sizeof(int)) throw Invalid();
        return value;
    }
    private static InvalidOperationException Invalid() => new("MAINTENANCE_SOCKET_ACTIVATION_INVALID");

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private struct NativeAddress
    {
        public ushort Family;
        public ushort Port;
        public uint Address;
    }
    [DllImport("libc", SetLastError = true)]
    private static extern int getsockname(int descriptor, out NativeAddress address, ref uint length);
    [DllImport("libc", SetLastError = true)]
    private static extern int getsockopt(int descriptor, int level, int option, out int value, ref uint length);
    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int descriptor, int command, int value);
}
