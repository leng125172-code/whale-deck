using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerRecycleWorkflowFixture
{
    internal static ContainerRecyclePlanDto Plan() => new(1,
        new(new(new('a', 64), "777:2026-11-05T01:02:03.123456789Z", "work/回收对象.bin"), new('B', 64)),
        new('C', 64), new(ulong.MaxValue, 33), new('D', 64), 5100, 5101, new('E', 64),
        new(ulong.MaxValue, 101, 33184U, 1000, 1001, 1, 7, 1700000000123456789),
        1700000000987654321, new('F', 64), [new("user.note", [0, 255, 7])]);
}
