# 递归 ACL 历史读取安装态验收

日期：2026-11-04。**宿主协议1.54及常规签名历史读取已安装并分项验收；管理员业务/API/Vue、历史恢复写入及完整故障矩阵仍未完成。** 不增加59项主任务的完整完成数，不把原生root探针当作Authentik人类授权或页面证据。

## 版本、标准安装与保护

Agent/MaintenanceHost使用准确CI成功的 `3f44c233d06bd42c686301efe81ab197cade9ab5`（CI41823225450 completed/success；功能2b30e09的CI41820072967亦成功）。探针及验证源码修正版本 `01dc834f0024811914e806c2a79fa4b2469698b5`；其CI41828151799仍运行，不冒称终态成功。三类来源分列，原生探针运行时校验自身来源，脚本核对已安装模块与准确宿主Git字节。

在独立临时detached工作树3f44c23上调用仓库标准publish.sh/install.sh；工作树只增加公共SDK摘要行，不复制私有env，SDK容器仅挂git archive的公共源码。两个宿主自包含.NETCore/AspNetCore及Pkcs均为10.0.13，完整SHA256SUMS和实际apphost --build-info通过。标准发布Invocation a6d3957fa18445f9967579340a3fe981、安装Invocation 4c780c6e5e3248159cba259a58e21dbe均准确退出0，停止QA unit前已保存状态与journal；安装后的validate.sh含namespace/FD映射/ACL真实依赖及模块元数据检查通过。

Agent PID4044173→340852，MaintenanceHost3598736→340848，实际来源均3f44c23。API/Worker/Gateway及其余核心容器仍为原b880208应用版本，11核心ID/镜像/StartedAt保持且healthy，不是四镜像覆盖发布。原591份保护文件全部字节保持；标准安装预期重写的产品sudoers、capability key、resources.json、agent.env四份元数据如实分列，其余旧文件九字段保持。user NOPASSWD文件和发布账本未修改，凭据不旋转/不输出。目录Root94个文件（47 JSON及47锁）也保持。

**维护连续性未通过：** 标准宿主重启期间48个约0.2秒间隔样本中3次未取得HTTP200，随后恢复healthy/Ready/no-store。完整样本保存，不用最后健康掩盖中断，也不由采样推断准确中断时长；P0-05零中断门槛仍待实现与验收。不回滚dev、不重启主机或Worker，不改网络/防火墙。

## 成功的新资源验收

成功父级Invocation `2d07f0ecb42c43f98b960d7db2b1cb09`、清理后独立Invocation `fe09b12fab924271a3cfaa38a4d23412` 均准确退出0。新隔离无网络、非root/dropALL/read-only-root容器及独占bind中完成八个签名递归ACL场景：default/access/noop/mask/命名UID0/default-noop/default清空/写前discard；七Completed、一Aborted，真实28份子前像和七聚合HDD留存。十八规划/八操作拒绝与迟到同ID返回均保持原物理与Root/HDD，不物理重做已终态操作。

在同一个仍活的容器上，真实专属历史服务完成：

| 检查 | 实际结果 |
|---|---|
| 有版本游标列表 | 8页，准确覆盖本CID/path的7份聚合，含末尾空页，无重复/遗漏 |
| 固定摘要逐项读取 | 28页，每份完整4项，异质前像与两ACL/原模式、命名ID0和权限0保持 |
| 拒绝边界 | List21/Read19，共40项，覆盖独立域/方法/签名/篡改/过期/重放、缺presence/范围、核心/文件/缺失目标、版本和摘要 |
| 每次读取后保持 | 完整源字节/九字段/两ACL/普通属性、Root和attested HDD快照均保持，无读取回退写入 |
| 清理前独立进程 | 重新打开ContainerRoot及全部FD，重验namespace/非IDMAP/源字节与原件、真实HDD摘要/manifest/前像 |
| 清理后独立进程 | 重验Root/HDD及副本、完整转录顺序和摘要、精确源/CID缺席；不声称重读已删除字节 |

计数表示完整捕获计划，不能当成原执行修改数；两份零变更历史仍保留完整前像。常规Completed来源通过不替代Partial/Unknown、来源篡改/丢失、实例变化、读取取消/超时等剩余组合。当前只读取，没有执行历史恢复。

## 首次失败与修复边界

首次Invocation `74a9843df45141cf92e750458276c822` 总门禁准确退出1：八个权限场景已完成，8页列表和21拒绝已保存，首个Read的探针退出read/InvalidArgument，父级记录HISTORY_PROBE_EOF。检查服务执行顺序确认缺offset先按请求格式拒绝，而探针误期待PermissionDenied。01dc834只修探针预期为InvalidArgument，并增加有界拒绝序号/值读取阶段；新增真实Service.Read专项证明在binding/driver前拒绝缺offset，产品代码和已安装宿主未改变。

原失败不补签：保留failure.json、完整转录和退出回执；只读独立补核真实Root/HDD的七聚合/28子前像及完整源字节/两ACL，662份保护文件保持后精确清理旧源。原CID已由原fixture正常删除，不重建旧CID或重放旧操作；未创建verified.json。修复后另建上述全新隔离资源取得成功回执。旧failed artifact永久保留originalGatePassed=false。

94项协议/SDK/请求顺序专项、150项Linux-root递归工具均通过，探针Release构建0警告/0错误，8范围及Python语法/diff检查通过。本轮没有修改产品业务/Schema/Vue，不把上批4744/569（7默认原生跳过）说成本轮全量重跑。修正探针经标准固定SDK自包含构建，准确Invocation e10af15d1d0f4611bc32814a79b74c6c退出0，产物terminal-probe.zZeQGr整体校验通过。

## 旧单项 ACL 与历史兼容

同一已安装1.54宿主上，以另一套新资源执行旧单项ACL九场景，八Completed/一Aborted、8份新HDD；13规划/4操作拒绝、真实迟到签名RPC不重写、完整物理/原inode/owner/mtime/属性保持及精确清理通过。旧历史服务12页/8快照及29权限拒绝通过，真实Root/HDD前像保持。父级Invocation `320eb898dee045999c5753076ccfb863`、独立 `456c37098a614f69b856d3f7d5200d4c` 准确退出0，不是旧二进制全量/人类授权/UI验收。

## 最终状态、证据及下一步

最终750份配置/普通及递归ACL Root/HDD保护文件中，安装后的旧591份、成功读取前的旧733份逐项保持；新增包括失败/成功两轮递归和兼容轮的真实Root/HDD，不覆盖原件。目录Root94文件保持，三套自有源/CID全部准确缺席。11核心healthy，宿主3f44c23和应用b880208分列，活动任务/终端/锁/屏障/租约0，永久递归业务仍30意图（9Succeeded/5Canceled/1Expired/3Failed/12Acknowledged）、28执行/58Job；没有伪造SQL意图来取得业务证据。逻辑HDD与源同device2049，不冒充异盘布局或异盘恢复。

私有证据（不入Git、目录0700/数据0600，发布可执行文件0700）：

- `recursive-acl-history-host-3f44c23-990de455f1f44a159510cc94e75c9916/`：标准发布/安装、实际来源/依赖、维护48样本和前后保护。
- `container-recursive-acl-agent-f2ce8776f19047139efab3dd48ef8af1/`：原失败及只读补核/精确清理，不补签。
- `container-recursive-acl-agent-dbe9f128e2be4ccc80f4c2dbe34ccb79/`：成功28页、清理前/后独立核验和准确退出。
- `container-acl-agent-8469644bc0854a8392e164604de98c15/`：旧ACL/历史兼容及独立核验。

临时公开源码工作树的普通用户清理首次因两份root Python缓存失败；逐一验证缓存来源后移除，只对仍与3f44c23 Git blob逐字相符的117个临时文件清理，临时目录和工作树登记现均缺席。安装产物和失败证据保留，不递归改仓库或证据所有权。

下一步接永久管理员授权/审计、Application服务/API/Vue历史列表与逐项详情，再部署完成真实Authentik→页面→API→Agent→实际前像整链；随后逐inode历史恢复计划/写入及故障矩阵、可恢复删除与长期清理、维护入口连续性及其余59项。不用root签名能力替代人类授权、不把通用递归ACL目标覆盖异质历史、不自动回滚dev。[当前目标](../GOAL_P0_P5.md)、[协议候选](CONTAINER_RECURSIVE_ACL_HISTORY_RPC.md)、[探针及旧能力边界](CONTAINER_RECURSIVE_ACL_HISTORY_PROBE.md)。

项目图谱事件 `whale-deck.container-acl.01dc834.recursive-history-installed` 已获对应project/key/event成功回执，阶段保持active，不将分项实机签收记为整个ACL或59项完成。
