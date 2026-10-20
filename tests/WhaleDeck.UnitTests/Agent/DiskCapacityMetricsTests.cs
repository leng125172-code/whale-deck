using WhaleDeck.Agent.Services;

namespace WhaleDeck.UnitTests.Agent;

public sealed class DiskCapacityMetricsTests
{
    [Theory]
    [InlineData("squashfs")]
    [InlineData("erofs")]
    [InlineData("overlay")]
    [InlineData("tmpfs")]
    [InlineData("devtmpfs")]
    [InlineData("proc")]
    [InlineData("sysfs")]
    [InlineData("cgroup2")]
    [InlineData("nsfs")]
    [InlineData("iso9660")]
    public void ImmutableImagesAndVirtualFilesystemsDoNotBecomeAFullPersistentDisk(string filesystem)
    {
        var snapshot = new FileSystemCapacitySnapshot("/snap/application/42", filesystem, DriveType.Fixed, 100, 0);
        Assert.False(DiskCapacityMetrics.TryCalculate(snapshot, null, out _));
        Assert.False(DiskCapacityMetrics.TryCalculate(snapshot with { FileSystemType = "ext4" },
            new(snapshot.MountPoint, filesystem, "image", 0), out _));
    }

    [Theory]
    [InlineData("ext4", DriveType.Fixed, "/", "/dev/nvme0n1p2")]
    [InlineData("xfs", DriveType.Fixed, "/data", "/dev/sda1")]
    [InlineData("btrfs", DriveType.Fixed, "/data", "/dev/mapper/storage")]
    [InlineData("zfs", DriveType.Fixed, "/data", "pool/storage")]
    [InlineData("vfat", DriveType.Removable, "/media/usb", "/dev/sdb1")]
    [InlineData("nfs4", DriveType.Network, "/mnt/archive", "server:/archive")]
    public void RealPersistentStorageAndUsbAreRetainedWithOnePercentConversion(string filesystem, DriveType type, string point, string source)
    {
        var snapshot = new FileSystemCapacitySnapshot(point, filesystem, type, 10_000, 6_544);
        Assert.True(DiskCapacityMetrics.TryCalculate(snapshot, new(point, filesystem, source, 8), out var metric));
        Assert.Equal("disk.utilization", metric.Kind); Assert.Equal(point, metric.DeviceId);
        Assert.Equal("percent", metric.Unit); Assert.Equal(34.56, metric.Value, 8); Assert.Equal("Good", metric.Quality);
    }

    [Fact]
    public void ActualFullDiskRemainsOneHundredAndNoSamplesAreInventedForInvalidCapacity()
    {
        var snapshot = new FileSystemCapacitySnapshot("/", "ext4", DriveType.Fixed, 1_000, 0);
        Assert.True(DiskCapacityMetrics.TryCalculate(snapshot, null, out var full)); Assert.Equal(100, full.Value);
        Assert.True(DiskCapacityMetrics.TryCalculate(snapshot with { AvailableBytes = 1_000 }, null, out var empty)); Assert.Equal(0, empty.Value);
        Assert.True(DiskCapacityMetrics.TryCalculate(snapshot with { TotalBytes = long.MaxValue, AvailableBytes = long.MaxValue / 2 }, null, out var large));
        Assert.Equal(50, large.Value, 8);
        foreach (var invalid in new[] { snapshot with { TotalBytes = 0 }, snapshot with { TotalBytes = -1 },
                     snapshot with { AvailableBytes = -1 }, snapshot with { AvailableBytes = 1_001 } })
            Assert.False(DiskCapacityMetrics.TryCalculate(invalid, null, out _));
    }

    [Theory]
    [InlineData(7U, "/dev/loop0")]
    [InlineData(7U, "/dev/disk/by-label/image")]
    [InlineData(253U, "/dev/loop42")]
    public void LoopBackImagesAreExcludedEvenWhenTheyContainAnOtherwisePersistentFilesystem(uint major, string source)
    {
        var snapshot = new FileSystemCapacitySnapshot("/mnt/image", "ext4", DriveType.Fixed, 100, 0);
        Assert.False(DiskCapacityMetrics.TryCalculate(snapshot, new(snapshot.MountPoint, "ext4", source, major), out _));
    }

    [Theory]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.NoRootDirectory)]
    public void NonStorageDriveTypesAreExcluded(DriveType type) =>
        Assert.False(DiskCapacityMetrics.TryCalculate(new("/media/image", "ext4", type, 100, 0), null, out _));

    [Fact]
    public void MountInfoDecodesKernelEscapesOptionalFieldsAndUsesTheVisibleOvermount()
    {
        var values = DiskCapacityMetrics.ParseMountInfo([
            "20 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw",
            @"21 20 8:2 / /media/USB\040disk rw - vfat /dev/disk/by-label/USB\040disk rw",
            "22 20 7:4 / /snap/app/42 ro - squashfs /dev/loop4 ro",
            "23 20 0:50 / /var/lib/docker/overlay2/id/merged rw - overlay overlay rw",
            "24 20 8:3 / /data rw - xfs /dev/sdc1 rw",
            "25 20 8:4 / /data rw - ext4 /dev/sdd1 rw"
        ]);
        Assert.Equal(5, values.Count);
        Assert.Equal(new("/media/USB disk", "vfat", "/dev/disk/by-label/USB disk", 8), values["/media/USB disk"]);
        Assert.Equal("/dev/sdd1", values["/data"].Source); Assert.Equal(7U, values["/snap/app/42"].DeviceMajor);
        Assert.Equal("overlay", values["/var/lib/docker/overlay2/id/merged"].FileSystemType);
        Assert.False(DiskCapacityMetrics.TryCalculate(new("/other", "ext4", DriveType.Fixed, 100, 0), values["/data"], out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("20 1 8:1 / / rw - ext4 /dev/sda1")]
    [InlineData("20 1 invalid / / rw - ext4 /dev/sda1 rw")]
    [InlineData("20 1 8:x / / rw - ext4 /dev/sda1 rw")]
    [InlineData("20 1 8:1 / relative rw - ext4 /dev/sda1 rw")]
    public void MalformedMountInfoCannotProduceAStorageSample(string line) => Assert.Empty(DiskCapacityMetrics.ParseMountInfo([line]));

    [Fact]
    public void SnapFullValueCannotDominateTheDashboardMaximumButRealFullDiskStillCan()
    {
        var samples = new[]
        {
            new FileSystemCapacitySnapshot("/", "ext4", DriveType.Fixed, 100, 80),
            new FileSystemCapacitySnapshot("/data", "xfs", DriveType.Fixed, 100, 40),
            new FileSystemCapacitySnapshot("/snap/app/42", "squashfs", DriveType.Fixed, 100, 0),
            new FileSystemCapacitySnapshot("/var/lib/docker/overlay2/id/merged", "overlay", DriveType.Fixed, 100, 0)
        };
        var metrics = samples.Select(snapshot => DiskCapacityMetrics.TryCalculate(snapshot, null, out var metric) ? metric : null)
            .OfType<HostMetric>().ToArray();
        Assert.Equal(2, metrics.Length); Assert.Equal(60, metrics.Max(metric => metric.Value));
        Assert.True(DiskCapacityMetrics.TryCalculate(samples[0] with { AvailableBytes = 0 }, null, out var full));
        Assert.Equal(100, metrics.Append(full).Max(metric => metric.Value));
    }
}
