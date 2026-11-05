# 独立回收恢复1.57安装态签名RPC分项

2026-11-06，产品f333a69af5a7ce4b839285187360b9a79a200a2f，门禁91fa9a9f、修订探针46d10784。承接[原生探针候选](CONTAINER_FILE_RECYCLING_RESTORE_PROBE.md)，本批标准安装宿主并完成全新资源的一来源/两个独立恢复操作签名RPC、原inode/HDD分项。永久业务、真实身份页面与完整故障矩阵尚未验收；首轮失败来源仍留存待恢复清理。

## 标准安装、精确CI与生产范围

产品f333a69a的准确CI42071523556已completed/success，12项全部成功。通过独立detached worktree的标准publish.sh/install.sh发布安装，构建仅git archive与公开固定SDK digest，无私有环境文件进入构建。两个自包含程序的完整SHA256SUMS与build-info身份通过，Microsoft.NETCore.App/Microsoft.AspNetCore.App及Pkcs均10.0.13，安装后的validate.sh通过。

发布unit whaledeck-qa-restore-publish-fb197b6eebc7、Invocation9fa2eebc77444019be03d66c03a5af27；安装unit whaledeck-qa-restore-install-fb197b6eebc7、Invocationa5caa9b1ae5f4ee0bac6573d3d9f4ab1，均准确exited/Result=success/ExecMainStatus=0，TimeoutStartSec=600。没有重放安装。

当前Agent1875174与MaintenanceHost1875158均f333a69a/NRestarts=0；Agent实际major1/minor57并具两项回收/恢复能力。安装新Root/claims为root:root/0700，新内核/传输为root:root/0750，安装后最初为空。API/Worker/Gateway/Web及运行checkout仍58b8174，没有把宿主安装报成四容器覆盖发布。11核心容器ID/镜像/状态/StartedAt保持，未回滚dev或重启主机。

维护入口按2秒间隔实际采样20次，约38.017秒，均HTTP200；不证明瞬时零中断或跨设备/动态网卡连续性。

首次安装保持检查过于严格而失败：标准安装刷新了agent-capability.key的ctime，以及resources.json、Agent sudo规则、agent.env的mtime/ctime，共四个固定文件七个时间戳字段。原before/after及11条差异（另四条为两个宿主PID/build身份）保存。随后只允许这七个明确字段时间向前，其内容SHA、设备/inode/模式/UID/GID/链接/大小及其他身份字段逐一相同；其余全部保护与历史字段精确比较通过。user NOPASSWD规则保持，未放宽整份配置或忽略整个保护快照。

## 首轮失败保留与探针修订

首轮3ce57573已完成原来源Apply/Inspect及新恢复计划边界，但在reject-refusal-1停止：嵌套来源变更后未重算计划指纹，先触发计划验证InvalidArgument，未到预期的签名绑定PermissionDenied。有一次篡改Apply RPC被拒绝，未进入原生恢复执行，独立恢复Root/claim当时均缺席。该轮保持failed，不追加伪造通过结论。早期保持记录中的newApplyNotInvoked措辞混淆了RPC尝试与被接受的原生执行；apply-attempt-clarification.json已明确更正为“RPC已尝试并拒绝、原生恢复未开始”，原字段和旧manifest按原字节保留，新manifest同时涵盖更正。

修订46d10784让计划篡改后重算完整合法指纹，再携带原请求签名；由此明确测试有效结构的未授权正文仍遭绑定拒绝。五项新C#用例验证这些计划实际通过结构检查但请求SHA已不同；82关联C#、116工具（含七项新门禁检查）、54原生回归、8范围检查全部通过，零失败/零跳过。探针标准自包含重建通过，正向产品代码和宿主版本未改动或重装。

旧失败源的Root/HDD原件与物理留存inode/字节/属性已只读复核保持，源操作未重放；其自有容器/目录由retain_unsettled策略保留，尚待独立恢复与准确清理。首次只读复核脚本传相对证据路径而被trusted检查拒绝，改成绝对路径后通过；未改变原件或降低路径校验。

## 全新资源的真实签名链

另建全新无网络、非root/只读根/固定预算容器及1,048,583字节普通文件，保留原版本、九字段、用户属性和完整字节摘要。先新建唯一原回收ID并实际Trashed，再以完整原引用/HDD摘要计划两个不同的新恢复ID；新ID与Context.JobId一致且不等于原来源ID。

已实际执行新计划16次、操作16次精确拒绝，含域/方法/时限、有效重算后的完整正文、源与新ID混用及不存在Inspect。随后取消任务Discard/Inspect/迟到Apply/Discard均Aborted，未生成来源claim或恢复源文件（独立取消journal仍按设计写入）；另一任务Apply/Inspect/重放Apply/Discard均Completed，保存唯一来源claim，原inode及字节/属性回到原路径。再次Inspect/迟到Apply/Discard取消任务仍Aborted、RestoredByThisOperation=false、源事实缺席，不冒认另一任务的成功。最后原来源Inspect以原ID报告Restored。

共19条正式探针帧，11个新恢复回执、3个原来源回执；先行独立能力ready/quit另存。每步分别采集新Root、来源claim、原Root和实际HDD元数据/数据；命名空间物理核验与记录投影分离。独立进程在清理前重读并核验所有来源及当前物理事实后，才准确删除本轮已验证文件、空私有目录和本轮容器/目录；清理后再独立检查记录与HDD原字节。清理后不声称还能读取已删除源文件字节。

本轮unit whaledeck-qa-recycle-restore-agent-46d10784、Invocation7f71320d9dc6406fbaec5594b1075c03，准确exited/Result=success/ExecMainStatus=0。清理前后两次独立校验及第三次实际保持快照通过。最终2346保护、11核心与目录Root96保持；原回收Root28→32/HDD31→35仅两轮各一新来源，新独立恢复Root5项为两ID的JSON/lock及唯一来源claim。旧失败来源保持，成功轮自有资源已准确清理；未改数据库、身份或业务Outbox。

## 证据与未验范围

三份证据均已逐文件重算：

| 证据目录（deploy/artifacts/下） | 文件/冻结源码 | EVIDENCE_SHA256.json摘要 |
| --- | --- | --- |
| container-recycle-restore-install-fb197b6eebc74d0ca073dfbd13118e9d | 115/91 | c6ec60fae8bf5fc267bef0f8d0d489e5647ef6e81c7d984bfd0c0fda7b5c4ce5 |
| container-recycle-restore-agent-3ce57573e7da48cd93db3883b0f93014（失败保留） | 249/226 | 5bf7957d2f244e56c2453ac091b6a9310ea7bb0a0c7f30561491da851f946849 |
| container-recycle-restore-agent-779502d89c5c4d2eb1322f20509f3a87（全新通过） | 195/120 | 29a18d7a04845c99721b0715325729168c5e62e60838c97a0c3e6d83d7f3bacb |

本批只签原实例留存inode恢复及写前取消/重放/迟到调用、原引用与claim归属，source/HDD仍同device，不签异盘灾难恢复。执行中取消/退出/撤权/重启、Unknown及跨写入矩阵，旧失败来源恢复清理、永久意图/真实PG/共享写保护/Worker/API/Vue与真人整链、长期清理及其余59项继续；新增测试CI须看推送后的准确终态，不用产品旧CI代替。完整主项0/59、阶段0/6、25%–35%工作量粗估及AI子计划0%不变；17项移出不算完成。

项目图谱whale-deck/container-recycle已按本批稳定eventId同步active，回执确认，本地pending移入synced；阶段未标completed。
