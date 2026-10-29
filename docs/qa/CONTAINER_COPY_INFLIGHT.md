# 容器文件复制的实际部分字节中断门禁

2026-10-29，产品运行时仍为 `7b3f44e`；新增的只有验证工具与文档，不为此重复部署产品。59项继续，17项移出不算完成。

## 已通过：真实 BFF 取消与正常退出

独占无网络、只读容器根、非root 1000:1000 测试容器，唯一可写bind下生成134,218,176字节测试文件。通过正常Authentik登录、Cookie/CSRF/Origin登记预检及独立执行任务。

观察器只接受精确测试目录/容器实例/永久执行记录；执行ID不是预检ID。它以pidfd绑定固定安装的Python apply传输，要求该进程同时持有准确容器root FD与本操作锁FD。只在Root Running、内层Preparing、私有文件有已登记inode且0<已写字节<计划总量、公开目标不存在时SIGSTOP；再次核对停止状态与所有证明。没有安装产品停顿参数，没有SQL/Root状态或时钟修改，也没有验证器SIGKILL。

实际两次部分复制中，明确取消与正常双端退出由产品分别在4.079秒/4.072秒终止原helper。原意图/执行ID保留，执行Job和业务行均Canceled、Root Aborted、目标不存在；Outbox完结，发布保护和租约释放，源内容保留。该验证使用真实HTTP身份/BFF/SQL/Worker/UDS/Root，不冒称进行了可见浏览器上的进行中按钮操作，也不冒称目录树的部分复制故障已验证。

证据 `deploy/artifacts/container-copy-inflight-7630d8d533f74a88b4dfcef54c9c1d43/` 的baseline、partial-cut、永久Root归档、results/verified。日志 `/tmp/wd-copy-inflight.log`。12项Linux-root观察器边界测试通过：实际持双FD但argv不同的子进程仍拒绝、原执行/owner/命令匹配、空/完整文件不算部分切点、已发布目标/Completed不算中断、未登记对象/替换inode/链接/非测试路径拒绝，并包含独立退出时钟与回执原子替换。已接CI，不能用这些测试替代实际切点。

## 重启首轮失败与环境干扰

首轮Worker实际SIGKILL后，产品终止Root helper；新Worker曾保留原Applying、原发布保护与原自然租约，没有提前接管。随后PostgreSQL容器意外以139退出，内核记录libc general protection fault；MongoDB同期也重启。SQL读取失败导致门禁失败，临时身份删除首次500；没有将该轮记通过。另一次重跑在Worker unhealthy前置条件就停止，没有创建新复制任务。

调查发现同期两条对HDD根和NVMe根的递归chown记录；应用根及多处数据库文件变为1000:1000。应用根750不再允许Agent访问，Worker持续PermissionDenied；精确恢复 `/data/WhaleDeck/apps` 为既定997:975后Worker恢复健康。这证明了目录权限故障，但不据此断言PG139的完整根因。4个EDAC控制器当时纠正/不可纠正计数均0，也不能据此证明或诊断硬件故障。

失败测试容器已移除后，原部分副本无法按旧容器绑定恢复，业务进入Unknown。不同临时管理员经真实BFF明确确认原Unknown，读取Acknowledged并释放保护；未编辑数据库、未重签执行、未冒称复制成功。原失败身份以其UUID和业务actor精确匹配后删除。源目录额外出现owner1000变化，未放宽删除门禁；按永久Root父inode匹配将整个准确失败目录移入root私有保留区，未删除文件。原件及业务历史保留，不把处置改记为重启门禁通过。

证据 `deploy/artifacts/container-copy-inflight-01fcf73958e648dbbf3f90f5a2d569a2/` 的partial-cut、自然租约观察、dependency-failure-recovery、source-preserved；被保留内容位置记在私有source-preserved文件。日志 `/tmp/wd-copy-inflight-restarts.log`、`/tmp/wd-copy-inflight-restarts-2.log`。

## 服务目录权限定点修复

仅针对经运行身份/现有部署约定确认的数据库数据/SQL备份/Whale Deck私有备份目录，生成root私有逐inode修改前清单，再修复UID1000漂移：546个对象，没有修改内容、模式或删除数据，没有对整盘递归chown，没有触碰Git项目、用户文件根及未知保留内容。逐组件O_NOFOLLOW打开和原device/inode/模式/uid复核，拒绝链接、跨设备、特殊位和多硬链接，只有明确目录范围内的fchown。

宿主校验重新通过。48个现存可验证HDD文件归档全内容摘要通过，共1,149,239,768字节；另外64个其他状态/非可验证记录没有计入通过，不代表整个历史库已全面验收。修改前清单、修复源码与应用结果、只读归档检查在 `deploy/artifacts/ownership-repair-c5023235d6e64ce7bc12a7e46b436996/`（源码在同级私有artifacts），日志 `/tmp/wd-runtime-ownership-host-validate.log`。

六类数据库临时资源创建、授权、轮换、连接与清理复验均通过，日志 `/tmp/wd-ownership-databases.log`；这不代表所有既有业务数据已做完整一致性检查。6类实际备份与校验也已全部通过；后续现场与范围说明见[属主事故修复](RUNTIME_OWNERSHIP_REPAIR.md)。

## 已通过：实际部分文件复制中的Worker/Agent重启

最终批次 `container-copy-inflight-bb3cccbd923849ff82921c786cc628cd`：准确Worker容器SIGKILL后显式启动，同一CID/镜像未重建，Root helper在0.109秒退出；新Worker保留原意图和执行ID，原90秒自然租约到期前连续观察不接管，终态Failed/CONTAINER_COPY_INTERRUPTED、Root Aborted、目标不存在、屏障/租约/Outbox释放。实际Agent systemd重启时Root helper在0.233秒退出；维护入口PID未变，重启后API仍可访问，业务同样准确中断，没有重做复制。两次源目录与准确CID已严格核验清理，永久历史保留。

`verify_container_copy_inflight.py` 在验证进程结束、临时身份删除后重新只读查询SQL和永久Root原件，复核取消/退出/Worker/Agent共4例：执行身份、部分字节、根回执原件未变、终态与审计/Outbox、源及准确CID缺失；全局复制活动/租约/屏障0，活动任务/终端/路径锁0，临时账号0，活动transient unit0，11核心healthy。两份通过批次各有 `independent-final.json`。该独立检查不创建身份、不改业务状态、不清理资源。

保留两次验证器失败，不追改为成功：`12b73839454345e1becd7025550f975e` 的Worker中断通过，但Agent轮把30秒正常服务drain计入15秒helper退出门限而失败；journal另证Root进程约3秒时已退出，业务最终Failed/Aborted、无目标且资源清理。现改为动作前并发观察原pidfd的绝对15秒门限，单独记录服务动作30.602秒，未放宽中断时间。`48688ff8428146ff9363df8597589607` 在内层回执元数据观察时断言失败，未留当时完整stat，不能断言其确切原因；该轮复制完整Succeeded/Completed，经完整摘要/属性证明后精确清理，不算故障门禁通过。观察器现使用O_NOFOLLOW固定FD读回执，并复核当前路径inode；原子替换/已脱链样本不签收，链接仍存在但权限异常直接拒绝。

后续仍需独立进行目录树故障、Unknown可见页面、进行中可见页面撤销及其余保留功能验收。上述真实HTTP不冒称可见页面点击验收。
