using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WhaleDeck.Api.Controllers;
using WhaleDeck.Api.Files;
using WhaleDeck.Application.Models;
using WhaleDeck.Application.Services;
using WhaleDeck.Infrastructure.Agent;

namespace WhaleDeck.IntegrationTests.Security;

public sealed class FileTransportTests
{
    [Fact]
    public void FileRoutesAlwaysRequireAdministratorAndAreNeverAnonymous()
    {
        Assert.Contains(typeof(FilesController).GetCustomAttributes<AuthorizeAttribute>(), item => item.Policy == "Administrator");
        Assert.Empty(typeof(FilesController).GetMethods().SelectMany(method => method.GetCustomAttributes<AllowAnonymousAttribute>()));
        Assert.Contains(typeof(FileMutationsController).GetCustomAttributes<AuthorizeAttribute>(), item => item.Policy == "Administrator");
        Assert.Empty(typeof(FileMutationsController).GetMethods().SelectMany(method => method.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
    [Theory]
    [InlineData("/etc/passwd")][InlineData("a/../b")][InlineData("a//b")][InlineData("a\\b")][InlineData("a\nb")]
    public void FileLocationsRejectAmbiguousAndEscapingNames(string path) => Assert.Throws<ArgumentException>(() => FileManagementService.Location(new("files.shared", path)));
    [Fact]
    public async Task IntentAndReceiveKeysAreValidatedBeforeAnyCookieOrAgentAccess()
    {
        var controller = new FilesController(null!, null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Create(new("signed-plan", true, Guid.NewGuid().ToString("D")), default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await controller.Content(Guid.NewGuid(), default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>(await controller.Cancel(Guid.NewGuid(), default));
        var mutations = new FileMutationsController(null!, null!) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await mutations.Create(new("signed-plan", true, Guid.NewGuid().ToString("D")), default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>((await mutations.Resolve(new("signed-plan", true, Guid.NewGuid().ToString("D")), default)).Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>(await mutations.Cancel(Guid.NewGuid(), default));
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestResult>(await mutations.Acknowledge(Guid.NewGuid(), new(1, true), default));
    }
    [Fact]
    public void FilePlanMacBindsLoginPathVersionDigestAndAllDeclaredMetadata()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wd-file-plan-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var key = Path.Combine(folder, "key"); File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        try
        {
            var protector = new FilePlanProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:CapabilityKeyFile"] = key }).Build());
            var claims = new FileUploadPlanClaims("admin", new('A', 64), new("files.shared", "资料/readme.txt", 3, new('B', 64), "ABSENT"), DateTimeOffset.UtcNow.AddMinutes(5));
            var signed = protector.Sign(claims); Assert.Equal(claims, protector.Verify(signed));
            var changed = claims with { LoginBinding = new('C', 64), File = claims.File with { Path = "other" } };
            var payload = protector.Sign(changed).Split('.')[0];
            Assert.Throws<ArgumentException>(() => protector.Verify(payload + "." + signed.Split('.')[1]));
            Assert.Throws<ArgumentException>(() => protector.Verify(new('A', 32769)));
            var binding = new FileUploadBinding(Guid.NewGuid(), claims.Actor, claims.LoginBinding, claims.File, claims.ExpiresAtUtc);
            var seal = protector.Fingerprint(binding);
            Assert.NotEqual(seal, protector.Fingerprint(binding with { Id = Guid.NewGuid() }));
            Assert.NotEqual(seal, protector.Fingerprint(binding with { File = binding.File with { Path = "other" } }));
            Assert.NotEqual(seal, protector.Fingerprint(binding with { ExpiresAtUtc = binding.ExpiresAtUtc.AddSeconds(1) }));
        }
        finally { File.Delete(key); Directory.Delete(folder); }
    }
    [Theory]
    [InlineData("valid")][InlineData("digest")][InlineData("missing-end")][InlineData("rpc-after-end")][InlineData("version")][InlineData("duplicate-end")]
    public async Task DownloadWithholdsFinalChunkUntilDigestAndRpcCompletion(string variant)
    {
        var context = new DefaultHttpContext(); using var output = new MemoryStream(); context.Response.Body = output;
        if (variant == "valid")
        {
            await FileHttpTransport.CopyVerifiedAsync(Parts(variant), context.Response, new('A', 64), default);
            Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
            Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => FileHttpTransport.CopyVerifiedAsync(Parts(variant), context.Response, new('A', 64), default));
            Assert.True(output.Length < 3);
        }
    }
    [Fact]
    public async Task EmptyFileRequiresSuccessfulEndNotMerelyZeroContentLength()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await Assert.ThrowsAsync<InvalidOperationException>(() => FileHttpTransport.CopyVerifiedAsync(Empty(), context.Response, new('A', 64), default));
        static async IAsyncEnumerable<FileDownloadPart> Empty()
        { await Task.Yield(); yield return new("metadata", new("empty", "File", 0, 0, new('A', 64), "0600", false), default, 0, "", "", 0); }
    }
    private static async IAsyncEnumerable<FileDownloadPart> Parts(string variant, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
        yield return new("metadata", new("中文.bin", "File", 3, 0, new('A', 64), "0600", false), default, 0, "", "", 0);
        yield return new("chunk", null, new byte[] { 1, 2 }, 0, "", "", 0);
        yield return new("chunk", null, new byte[] { 3 }, 2, "", "", 0);
        if (variant == "missing-end") yield break;
        var end = new FileDownloadPart("end", null, default, 0, variant == "digest" ? new('F', 64) : Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), variant == "version" ? new('B', 64) : new('A', 64), 3);
        yield return end;
        if (variant == "rpc-after-end") throw new InvalidOperationException("simulated RPC completion or audit failure");
        if (variant == "duplicate-end") yield return end;
    }
}
