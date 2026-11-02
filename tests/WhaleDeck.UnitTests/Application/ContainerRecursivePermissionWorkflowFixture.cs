using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerRecursivePermissionWorkflowFixture
{
    internal static ContainerRecursivePermissionPlanDto Plan(bool zeroChanges = false) => new(1,
        new(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", "work/权限树"), "0600", "0750", new('B', 64)),
        new('C', 64), new(ulong.MaxValue, 33), new('D', 64), 5100, 5101, new('E', 64),
        new(ulong.MaxValue, 101, 16872, 1000, 1001, 2, 4096, 1700000000123456789),
        1700000000987654321, new('F', 64), 4, 2, 2, zeroChanges ? 0U : 2U);
}
