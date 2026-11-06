# 清理1.58安装态签名RPC与HDD分项

2026-11-06，产品 `857e018d9ec73ebac9d8e5257701143f64fe938f`、探针95528cd3、门禁a4f71264。承接[原生探针准备](CONTAINER_FILE_RECYCLING_PURGE_PROBE.md)，本批完成标准宿主安装、一个全新自有来源与两个独立清理操作的真实签名RPC/实际HDD及清理前后独立核验。永久业务/真人页面、执行中故障、长期策略与归档恢复尚未完成。

## 标准发布、安装与范围

产品857e018d准确CI42164005523为completed/success，12项全部成功；安装前实际比较，该提交的src和deploy/host与当时dev的7adc0fd7完全一致，之后仅增加验证/文档。独立detached工作树使用标准publish.sh/install.sh，构建只挂载Git archive公开源码，不包含工作站私有环境。两个自包含程序的SHA256SUMS、build-info与runtime验证通过，NETCore/AspNet/Pkcs均10.0.13。标准validate.sh通过，新purges目录及四模块另按精确源码、root所有者、单链接和权限补验，未用标准脚本当前未覆盖的新模块检查代替实际核对。

publish unit `whaledeck-qa-purge-publish-c135f397a0da` / Invocation `16c68656e5b7495fa5ca3a8b86c8b5f5`；install unit `whaledeck-qa-purge-install-c135f397a0da` / Invocation `57ed4e7cafb5461ebf0132d0ce5f65c8`。均exited/Result success/ExecMainCode1/ExecMainStatus0，未重放发布安装。

Agent PID3687710与Maintenance PID3687706均857e018d、NRestarts0。API/Worker/Gateway/Web及运行checkout保持093f44a9，没有把宿主安装称为四容器覆盖发布。新purges根root:root/0700，安装后最初为空；四新模块root:root/0750、普通单链接，内容与产品提交逐字节一致。后续正向门禁还核对17份实际安装宿主文件。

安装保持允许项严格限定：两个宿主PID/构建身份，及agent sudo规则/resources.json/agent.env的mtime、ctime和既有能力key的ctime，共四固定文件七时间戳向前；其内容SHA、其余设备/inode/权限/所有者/链接/大小字段及其他全部保护/历史保持。开发user NOPASSWD规则原样保持。新purges目录创建与随后操作写入分别记录，没有放宽整棵Root快照。

## 维护入口采样发现

安装与标准验证期间约每2秒采样，共5次：4次HTTP200/no-store，1次ConnectionError，随后再次采样恢复HTTP200，两服务running。失败样本位于相邻成功样本之间约4.008秒窗口内，不能据此量出精确停机时长，更不能签瞬时连续性。原样保留全部样本；维护入口连续性仍为剩余工作，没有因其恢复200而抹掉这次安装重启间断。

## 全新来源、取消与实际清理

门禁a4f71264新增严格回复去重解析和准确命名空间增量校验，3项范围/管道检查及19项独立证明检查通过；扩展回收相关234项Linux root检查零跳过。原生探针继续使用标准构建95528cd3和全量SHA256SUMS，验证器静态闭包单独固定。

先在不创建资源的情况下实际ready/quit核对新1.58能力。然后创建全新固定镜像、无网络、非root/只读根及固定预算容器，写入1,048,583字节、1000:1000/0640及用户属性的本轮文件。新来源计划/Apply/Inspect达到Trashed，原inode进入私有留存，实际HDD历史/数据校验通过。只对这个本轮来源计划两个不同的新清理ID，均绑定新Context.JobId。

18项计划拒绝和26项操作拒绝实际达到精确预期，包含完整合法重算后的请求篡改、域/方法/时限/nonce、新旧ID与job混用和未预留Apply；签名拒绝没有误算为已执行清理。取消ID依次Reserve/Inspect为Reserved，Discard/迟到Apply/Inspect为Aborted，留存inode/完整字节/属性及HDD不变，来源围栏Released。另一新ID重新计划、Reserve/Inspect后实际Apply达到Completed，随后Inspect/重复Apply/Discard仍Completed且不再删除；原件、留存及本操作临时名均缺席，HDD数据身份/字节/属性保持，来源/历史Purged及本操作unlinkConfirmed、Purged围栏全部核对。

共20条正式帧、11个新回执和3个原来源回执，最终来源Inspect独立报告Purged且历史摘要与实际保留HDD一致。每步保存实际Root原字节、围栏、来源、历史与数据快照，物理目录FD检查和记录联结分离。取消时的历史Released围栏没有冒充清理完成后的当前共享围栏；独立live仅重读取消journal自身未变，当前围栏归属已完成的新清理。

独立进程清理前重读完整来源/清理/围栏/实际HDD和原容器配置/实例及空缺名称，通过后仅rmdir已证明为空且inode匹配的本轮私有目录，既有严格fixture清理器移除本轮容器/种子目录。HDD与所有回执保留。之后独立final及第三次实际保持快照通过；清理后不声称读取了已删除原件字节。Unknown和执行中故障须另行门禁，不能用本轮成功链替代。

unit `whaledeck-qa-purge-agent-9c7453f5` / Invocation `6d923d83d6c44771ab064f243d7a7e4f`，exited/Result success/ExecMainCode1/ExecMainStatus0，实机总门禁首次通过。新验证器保留既有Root记录，不以清空失败历史获取通过；本轮没有失败来源需要处置。

## 保持事实与证据

最终2346保护文件与11核心容器保持；36回收/22独立恢复完整业务行及其他Root/HDD保持，活动0。回收Root72→75仅本来源json/lock/purge标记，HDD81→83仅本轮json/data，恢复Root59、目录Root98保持；purges根新增4项为两个新ID各json/lock。原件与HDD同设备2049，不称异盘灾备。没有新增永久业务表、Outbox、真实身份或WebUI验收。

私有封存目录在deploy/artifacts下，逐文件重算通过；目录root:root/0700、文件root:root/0600。发布物在安装后完成封存权限收紧，安装的/opt可执行文件不受影响。

| 本批证据 | 文件数 | EVIDENCE_SHA256.json摘要 |
|---|---:|---|
| container-recycle-purge-install-c135f397a0da | 724 | dd43c0427d880752abdf649d87db268dcca0d991af9897366487061b011c16d9 |
| container-recycle-purge-agent-4e9657e451004463980341d0e6e67c94 | 209，含115验证闭包、5探针/契约来源哈希及17宿主源码 | db3f47c3c98c989490c1404a7366fed7414886439dce0eb37e01cc7f33c93987 |

继续永久意图/真实PG/共享写入保护/Worker/API/Vue/Jobs与真人整链、适用执行中故障矩阵、维护入口连续性、留存策略与清理后的归档恢复及其余59项；本批门禁提交后CI终态另查。整项0/59、阶段0/6、AI功能0%保持，master完整交付尚未完成，17项移出不算完成。
