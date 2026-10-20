using System.Globalization;

namespace WhaleDeck.Agent.Services;

public sealed record FileSystemCapacitySnapshot(string MountPoint, string FileSystemType, DriveType DriveType,
    long TotalBytes, long AvailableBytes);

public sealed record LinuxFileSystemMount(string MountPoint, string FileSystemType, string Source, uint DeviceMajor);

/// <summary>Capacity of persistent filesystems, not disk I/O busy time or immutable system images.</summary>
public static class DiskCapacityMetrics
{
    private static readonly HashSet<string> VirtualFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "squashfs", "erofs", "overlay", "aufs", "tmpfs", "devtmpfs", "ramfs", "rootfs", "proc", "sysfs",
        "cgroup", "cgroup2", "devpts", "securityfs", "debugfs", "tracefs", "pstore", "configfs", "efivarfs",
        "mqueue", "hugetlbfs", "nsfs", "autofs", "binfmt_misc", "fusectl", "bpf", "rpc_pipefs", "selinuxfs",
        "iso9660", "udf"
    };

    public static bool TryCalculate(FileSystemCapacitySnapshot snapshot, LinuxFileSystemMount? mount, out HostMetric metric)
    {
        metric = new("disk.utilization", snapshot.MountPoint, 0, "percent");
        if (snapshot.DriveType is DriveType.CDRom or DriveType.Ram or DriveType.NoRootDirectory ||
            string.IsNullOrWhiteSpace(snapshot.MountPoint) || snapshot.TotalBytes <= 0 ||
            snapshot.AvailableBytes < 0 || snapshot.AvailableBytes > snapshot.TotalBytes ||
            VirtualFileSystems.Contains(snapshot.FileSystemType) ||
            mount is not null && (mount.MountPoint != snapshot.MountPoint || VirtualFileSystems.Contains(mount.FileSystemType) ||
                mount.DeviceMajor == 7 || mount.Source.StartsWith("/dev/loop", StringComparison.Ordinal))) return false;

        var percent = (snapshot.TotalBytes - snapshot.AvailableBytes) * 100d / snapshot.TotalBytes;
        metric = metric with { Value = percent };
        return true;
    }

    public static IReadOnlyDictionary<string, LinuxFileSystemMount> ParseMountInfo(IEnumerable<string> lines)
    {
        var mounts = new Dictionary<string, LinuxFileSystemMount>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length > 16_384) continue;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var separator = Array.IndexOf(fields, "-");
            if (separator < 6 || separator + 3 >= fields.Length) continue;
            var device = fields[2].Split(':', 2);
            if (device.Length != 2 || !uint.TryParse(device[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
                !uint.TryParse(device[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)) continue;
            var point = Unescape(fields[4]);
            if (!point.StartsWith('/')) continue;
            // Later mountinfo entries win for overmounts, matching the visible mounted filesystem.
            mounts[point] = new(point, fields[separator + 1], Unescape(fields[separator + 2]), major);
        }
        return mounts;
    }

    private static string Unescape(string value) => value.Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal).Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
}
