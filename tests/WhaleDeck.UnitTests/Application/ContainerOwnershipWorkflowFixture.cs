using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerOwnershipWorkflowFixture
{
    internal static ContainerOwnershipPlanDto Plan(bool directory = false) => new(1,
        new(new(new('a', 64), "777:2026-10-30T01:02:03.123456789Z", "work/权限对象"), 2200, 3200, new('B', 64)),
        new('C', 64), new(ulong.MaxValue, 33), new('D', 64), 5100, 5101, new('E', 64), directory ? "Directory" : "File",
        new(ulong.MaxValue, 101, directory ? 16872U : 33184U, 1000, 1001, directory ? 2U : 1U, 7, 1700000000123456789),
        1700000000987654321, new('F', 64), false);
}
