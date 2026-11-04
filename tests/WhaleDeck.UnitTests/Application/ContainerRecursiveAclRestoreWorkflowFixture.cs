using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerRecursiveAclRestoreWorkflowFixture
{
    internal static ContainerRecursiveAclRestorePlanDto Plan(bool zeroChanges=false, string kind="access") => new(1,
        new(new(new('a',64),"777:2026-11-04T01:02:03.123456789Z","work/权限树"),new('d',32),new('8',64),new('B',64)),
        new('C',64),new(ulong.MaxValue,33),new('D',64),5100,5101,new('E',64),
        new(ulong.MaxValue,101,16872,1000,1001,2,4096,1700000000123456789),1700000000987654321,new('F',64),4,2,2,zeroChanges?0U:kind=="default"?2U:4U,
        new(new('d',32),new('8',64),0,"666:2026-11-03T01:02:03Z",kind,new('9',64),4,2,2,2),
        new(ulong.MaxValue,66,0x8180,0,0,1,65543,-1,0),new('7',64));
}
