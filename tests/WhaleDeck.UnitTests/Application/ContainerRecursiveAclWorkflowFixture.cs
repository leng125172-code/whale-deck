using WhaleDeck.Application.Models;
namespace WhaleDeck.UnitTests.Application;
internal static class ContainerRecursiveAclWorkflowFixture
{
    internal static ContainerAclEntryDto[] Entries(uint mask=4,uint id=2201,uint permissions=6) =>
        [new("user_obj",null,6),new("user",id,permissions),new("group_obj",null,0),new("mask",null,mask),new("other",null,0)];
    internal static ContainerRecursiveAclPlanDto Plan(bool zeroChanges=false) => new(1,
        new(new(new('a',64),"777:2026-11-04T01:02:03.123456789Z","work/权限树"),"access",Entries(),Entries(7),new('B',64)),
        new('C',64),new(ulong.MaxValue,33),new('D',64),5100,5101,new('E',64),
        new(ulong.MaxValue,101,16872,1000,1001,2,4096,1700000000123456789),1700000000987654321,new('F',64),4,2,2,zeroChanges?0U:4U);
}
