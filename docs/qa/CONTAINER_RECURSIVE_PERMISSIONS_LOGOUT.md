# 递归普通权限：原实例保留时正常双端退出与部分结果处置

2026-11-02。产品保持`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/协议1.49，无发布或自动回滚。真实验收源码冻结`8dc8d556fe615042729aa5f1097f4190ba245231`，20份运行依赖逐字与Git blob/SHA核对。新建一套准确自有容器、目录树和临时Authentik身份，**首次完整真实门禁及独立只读核验通过**。之前[撤权两例](CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)、取消/Worker强杀/Agent重启/Unknown等场景不重跑。

## 实际切点及正常注销

正常可见Chrome/Authentik登录→已部署Vue预检/唯一确认→永久SQL/双Job/Worker→已装签名Agent/helper。测试容器无网络、非root1000:1000、只读根、限资源，只绑定本批唯一HDD目录。树为16文件+2目录，每文件2097159完整字节、普通user属性、混合UID/GID和纳秒mtime；原模式文件0640/目录0750，明确预检0600/0700，18项预计修改。

仅为确认后布置观察器，短暂停启原Worker容器；全局其他任务/终端/锁/屏障均0。第一项真实chmod及Completed子回执已发生，聚合Applying、finishedThrough=0/startedThrough=0，按准确pidfd、固定argv、容器root FD和原operation lock FD绑定暂停。保存SQL/原Root字节、全树完整内容和九字段。没有产品故障钩子、SQL/Root/时钟/缓存写入，没有强杀Worker或重启Agent/MaintenanceHost。

原320×600暗色页面查询同一Applying任务，证明取消按钮完整可见、递归任务引用存在，**没有发送取消请求**。观察器就绪后，正常关闭详情，通过可见用户菜单“退出登录”，由产品完成BFF和Authentik双端注销：

- 浏览器退出开始UTC`10:54:10.144`，双端验证完成UTC`10:54:14.774`，落到产品`/login`。
- BFF`/api/v1/auth/me`为401，Authentik当前身份接口为401/403（共享可见退出工具真实断言，不导出会话或Token）；原递归任务GET为401。
- 原递归任务region和sessionStorage引用消失，登录标题可见，页面无横向溢出，保存真实`logged-out.png`。
- 产品在观察器开始后**4.257秒**内结束原helper，验收器未强杀helper。该时间上界从退出信号之前开始，不伪称精确服务内部注销延迟。

退出前后只读GET同一个准确临时Authentik账号，严格匹配Subject/用户名/PK；两次active=true、管理员组及其他快照字段完全相同，没有PATCH/停用/删组。退出后的身份读取UTC`10:54:15.046245`，发生在正常双端注销之后、临时用户删除之前。原正快照到期UTC`12:53:32.2072332`，退出后还剩7157.433秒，因此不是自然过期或身份被删除诱发停止。

退出后再次真实Docker inspect同一CID，Running=true、原PID和StartedAt均不变；原容器实例一直保留到最后准确清理，不借目标消失取得结果。

## Partial、真实计数页面及不同管理员处置

原intent/执行Job/Subject/实例/请求/计划及Root开始时间不换。SQL/Root/receipt/HDD为**Partial：1完成核对、1实际修改、1中止、16未开始、0待证明**，互斥总数18，租约清空而保护保留；执行Job仍Running、cancel_requested_at_utc为空。不能把注销后任务停止记成零修改或整树成功。

切点树、helper退出后树、Worker恢复观察后的树完全相同。每个文件完整2097159字节、inode、UID/GID、mtime/user属性保留，只第一项预期模式及必要ctime变化；已完成项不重做、未开始项不续做。HDD聚合1份、Completed子项历史1份及Root两个子回执Completed/Aborted逐一对账；不创建16个未开始项的假历史。

原临时账号在上述证明之后才实际删除。另一位不同Subject的管理员通过正常可见Authentik登录，进入320×600明色异常处置页，定位同一Partial行；按dt/dd精确核对五类计数并截图，不能只看接口。实际计数不是互相重复累加，页面也保留“历史验证不代表当前HDD可用”的观察边界。

先“暂不处置”（POST0、原SQL/Root/保护/完整源树不变），再唯一明确确认（HTTP202），按钮在小屏完整可见。原执行Job为Failed、intent为Acknowledged，acknowledged_by是当前新Subject，原actor/计数/Root/HDD/文件保持，保护明确释放。处置成功不代表整树成功，不自动恢复或继续权限修改。

追加已装固定privileged helper迟到apply同一原请求，仍Partial/replayed，原Root字节及全树九字段/完整内容不变。这个追加探针**不是单独签名UDS RPC**，不借主业务签名链外推。没有恢复原模式来伪装修改未发生。

## 独立核验与清理

- 主进程退出0、verifiedFixtureRemoved=true；先完整核对源文件内容/九字段/目录集合，再准确unlink/空目录rmdir和CID清理。原SQL/Root/HDD永久历史保留，两个临时用户确实删除。
- 独立只读进程重读实际SQL/Root/已attest HDD，与私有原始字节一致，重算计划/清单/子计划/版本/历史/outcome独立域摘要，核对注销时序、仍启用的原身份、原容器实例和未到期时间、停止边界、不同管理员及迟到回执。清理后不声称重读已删除源字节；DOM/storage断言来自真实浏览器回执并辅以PNG。
- 5条哈希审计，唯一partial-acknowledged是新Subject，其余是原Subject；details只含intentId/containerId与重算instanceHash/pathHash。5张320×600真实PNG，目视复核明色完整五类计数和“暂不处置/确认处置”按钮。
- 11核心healthy；8数据库平台容器ID/镜像/状态/StartedAt不变。API/Gateway及Agent/MaintenanceHost不重启；Worker只有布置观察器时按原容器停启。11个已装helper逐字匹配产品0dede08。
- 52项关联/隔离边界检查通过、0跳过；落盘后8项范围检查、207份MD的1254个相对链接零缺失，Python/Node语法与diff通过；无产品源码更改，不借旧全量测试或CI代替本批证据。新增离线用例首轮误断言“前后相同但数值不同的合法PK也必须失败”；修正测试期望，只拒绝前后PK变更及错误身份/降权后，全52项通过。该测试期望错误不是产品或真实门禁失败。
- 源和HDD历史device均2049，仅同设备元数据留存，不计NVMe业务、异盘灾备或HDD故障。user NOPASSWD授权保留，产品Agent权限未扩大，无主机重启、磁盘填满、真实通知或自动回滚。

## 私有证据及剩余范围

目标表、实施记录和项目图谱阶段已更新为active。同步曾遇连接错误及source字段拒绝；将持续追加的元数据来源列表收敛为目标/状态索引和近期QA入口后，同一eventId收到成功回执并移入synced。旧QA原文及历史动态保留；该同步问题不是产品故障，也没有重复真实操作。

`deploy/artifacts/container-recursive-permission-faults-cdd023c3dc93451f8cfd83fe95868553/`为root/0700，原件0600、不入Git。包含baseline/20份冻结SHA、ready/原正快照期限、bound-helper/cut-root、pre-logout SQL、physical-cut/after-helper-exit/partial/final、logout-authority（原身份和容器实例）、原/新浏览器回执与时序、SQL/Root/HDD、history/late/audit/installed-core/verified/independent-final及5 PNG。无登录凭据截图，凭据只在内存/子进程环境。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 logout
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-cdd023c3dc93451f8cfd83fe95868553
```

已有回执，不为展示重复第一条真实操作。第二条只读复核；未来宿主重启或产品换版应区分运行边界，保留本批历史。

这只签收“原实例保留、首项实际修改后正常双端退出”组合。其余Agent/Worker/回执/HDD/路径与跨盘切点、其他写入类型及首次登记仍待；接着推进递归所有者、ACL、可恢复删除与长期清理、P0-05手动回退来源及其他59项/UI/C#酒馆/供应商/AI Live2D。其他模块注销、完整文件能力和整项交付不能据此签收，17项移出不计完成，master未交付。
