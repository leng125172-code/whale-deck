using System.Reflection;
using System.Security.Cryptography.Pkcs;

namespace WhaleDeck.UnitTests.Infrastructure;

public sealed class PkcsDependencyTests
{
    [Fact]
    public void LoadedPkcsPackageMeetsPatchedVersionFloor()
    {
        // AssemblyVersion stays 10.0.0.0 across servicing releases; inspect the product version instead.
        var information = typeof(SignedCms).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(information);
        var version = Version.Parse(information.InformationalVersion.Split('+')[0]);
        Assert.True(version >= new Version(10, 0, 13), $"Pkcs {version} is below the patched 10.0.13 floor.");
    }
}
