# 恢复任务安全结果：标准覆盖与安装核验

2026-11-05，运行已更新为 `ac909114d65101639d12f53c5d921edc69c2a89d`。承接[结果候选与测试](CONTAINER_RECURSIVE_ACL_RESTORE_JOB_RESULTS.md)。本批签标准发布及技术核验，未执行新真实身份/文件/HDD任务摘要整链，不能签完整功能或59项完成。

## 准确来源与一次标准更新

[CI41935193964](https://github.com/leng125172-code/whale-deck/actions/runs/41935193964) 的head完整匹配ac90911；backend、native-postgres、native-valkey、installer、frontend_checks、六个浏览器分片及frontend聚合共12路均completed/success，名称集合和提交SHA逐项核对。没有用旧617a1f3成功或仍在运行的文档提交替代候选CI。

运行检出 `/data/GitRepos/whale-deck` 为干净dev，由user从eeb24dd向ac90911快进；对象从已推送的本地仓库取得，无reset/强推/回退。私有.env/.images.env保持user:0600，未读入日志或Docker源码。维护观察器首样本200后，仅启动一次已安装 `/usr/local/libexec/whaledeck-platform-maintenance update`，完整发布安装两宿主、冻结四镜像构建、Migrator及Compose三应用force-recreate/healthy；无跳过迁移或自动回滚。

- 发布unit：whaledeck-dev-publish-ac90911-c43850b6，Invocation 9608e6bafc9f4993bcf1e0525386e898。
- 维护观察器：whaledeck-qa-job-results-observer-c43850b6，Invocation 7c8ed024b04b415bad41ad566060f4ab。
- 只读后置核验：whaledeck-qa-job-results-postflight-c43850b6，明确After/Requires绑定原发布unit，未重发更新。
- 三者均准确MainPID=0、SubState=exited、Result=success、ExecMainStatus=0；回执和日志归档后才停止本批QA unit。未把运行中默认success当成功。

## 安装态与生产保护

API/Worker/Gateway/Migrator及Agent/MaintenanceHost六组件的实际build-info和镜像标签均为ac90911，API/Gateway HTTP来源一致。宿主进程exe和运行DLL映射device/inode与安装文件逐项一致；Agent PID2038743、维护PID2038753。安装验证通过systemd、权限、UDS gRPC、受限helper、Docker访问和动态发现入口；自包含.NET与PKCS均10.0.13。实际三个现有网络入口的8080/8081校验通过，没有改写主机网络、重启主机或扩大Agent权限。

11核心healthy；8个数据库平台容器ID/镜像/StartedAt保持，API/Worker/Gateway按标准更新正常替换。旧1952保护记录集合保持：只有部署账本内容变化；4项宿主配置按正常安装更新元数据但字节/mode/owner/group/size保持，capability key仅ctime变化、原inode/mtime及字节保持，无密钥轮换。

来源Root1570、恢复Root196/HDD132、目录Root94的原字节及九字段保持。普通业务54意图/49执行/103Job、恢复13意图/10执行/活动0均保持，无恢复重放、旧Job摘要回填或模拟生产任务。迁移20261104182233仍1次，18 CHECK/12发布fence/1guard实查保持。本功能复用ResultJson，无新迁移。其他活动任务、终端、宿主锁及各类发布保护均0。

账本currentRevision=ac90911、previousRevision=eeb24dd、pending=null。user NOPASSWD:ALL固定文件root:root/0440及单文件/全量sudoers校验通过，按授权保留；开发与运行两个Git索引均1000:1000/664。实际匿名Jobs与恢复异常清单GET均401，只签匿名拒绝，不冒充管理员/普通用户登录验收。

## 维护入口与未验范围

维护观察覆盖479123ms、240个两秒样本：239次200、1次ConnectionError（elapsedMs138223/status0），当前恢复healthy。保留原失败，不签本批或整个P0-05全窗口连续，不推断瞬时零中断。源/HDD同device2049的旧限制继续保留，不称物理NVMe/HDD分离已完成。

私有证据 `deploy/artifacts/restore-job-results-release-ac90911-c43850b6b19945ad857aeb059a863592/` 保存准确CI、发布请求/Invocation、前后原始快照、账本、维护原始采样、六组件/HTTP来源、实际exe/DLL映射、安装日志和SQL保护证明。27份文件逐项SHA256通过，清单摘要 `51fc1d2ea965f771e6da691de8110af2b3ba984d7089e2f1b1c8b01a9526b0e3`。

下一步使用全新来源/容器/真实Authentik身份，核对任务页26字段摘要与API/双Job/原审计、实际文件及Root/HDD，覆盖正常/取消、缺回执Unknown、Partial/处置与规划历史；验证普通/匿名拒绝和原上下文退出，再准确清理。旧13恢复意图不重放以补签。其余撤权/重启等矩阵、可恢复删除/长期清理、维护连续性及59项继续，17项移出不算完成，不提升主任务完成率。


收尾：范围8项通过，变更MD的744个本地链接缺失0，diff检查通过，开发/运行索引保持1000:1000/664。项目图谱事件whale-deck.container-acl.ac90911.restore-job-summary-release已取得project/key/event匹配、duplicate=false回执，pending移入synced，阶段保持active。
