# 开发空间归档计划：安装态 RPC 与真实身份启停

2026-11-10，接续[归档签名 RPC](DEVELOPMENT_ARCHIVE_RPC.md)。`88f2df862bde30206feae35b09949dbba5f444cf` 的准确 [CI 42818320693](https://github.com/leng125172-code/whale-deck/actions/runs/42818320693) **12/12 全部成功**后，已标准覆盖发布 dev，并完成真实安装 Agent 1.77 的 Plan/Observe/Cancel 验收。未完成产品归档与空间退役，不增加完整主任务、ST/PET 或供应商/Live2D 子门槛的完成数。

## 实现与部署

新增显式启用的安装态 QA 模式：`DevelopmentArchiveRpcProbe --installed-plan`。直接连接固定已安装 Agent socket，先核对 major=1、minor≥77 与精确能力；固定 plan 阶段、已登记 QA 空间、NVMe root 和 8192 MiB 预算，不签发 Capture/Verify、源码删除或配额释放请求。密钥仅在内存读取并最终清零，不写 stdout、证据、命令参数或 Git。探针复制到 root 私有证据目录，二进制及脚本前后 SHA256 核对。失联或异常使用原请求/任务 ID、新 nonce 和独立清理预算精确 Cancel/Observe，不重建任务。

真实 UI 生命周期 QA 增加停用后检查与异常恢复：当前 epoch 和原计划摘要必须与真实 UI 提交匹配。异常只允许恢复本轮成功停用、epoch 仍匹配的空间；原停用回执不确定时，先沿原幂等键/操作查询。恢复通过原页面、确认、BFF/CSRF/Origin、Worker 和 Agent；不直接改数据库或宿主 marker。

QA 代码先以 `2955c416`、`e8deaa50` 提交，随后加入受控异常实机验证；QA 自身新提交仍需准确 CI。生产六组件均来自 `88f2df86`，不把本地验证脚本版本冒充生产服务版本。

正式仓库从 `01206806` 干净快进至 `88f2df86`，固定维护入口执行标准 update，没有回滚。发布单元最终 `Result=success`、`ExecMainStatus=0`、`MainPID=0`，之后仅本批自己的 oneshot 已停止。独立核对六组件真实镜像/二进制/HTTP、两个宿主活进程 inode、23 个宿主模块和标准 `validate.sh` 均通过；11 核心健康，原 8 数据库依赖身份不变。部署账本 current=88f2df86、previous=01206806、pending=null。

## 安装态实际验收

- 两轮分别创建一个临时 Authentik 管理员，经可见登录进入真实 BFF。未创建新空间、导入或运行任务。正常轮通过 320px 暗色页面停用→计划检查→恢复→正常退出；临时账号已核对删除。
- Active 空间的真实签名 Plan 任务终态为 `Rejected / WORKSPACE_STORAGE_ARCHIVE_NOT_STOPPED`；同一空间 Stopped epoch 的 Plan 成功扫描 **38 条、4132 字节**，完成真实 NVMe 源码与配置 HDD 的存储证明、canonical 请求及计划摘要校验。调用的是安装 Agent 和真实固定 helper，没有替代服务、挂载替换或存储 double。
- 每轮 Active/Stopped 两次探针均验证 6 类签名拒绝：无签名、旧 method-only、跨方法、重复 header、签后 actor 变化与 nonce 重放。重复 Start 保留原结果、晚 Cancel 保留终态；预取消后迟到 Start/Observe 均为原 Stopped。
- 第二轮在真实 Stopped Plan 成功后，主动制造 **QA 脚本异常**，原浏览器进程按预期退出 1，`state.passed=false` 原件保留。实际恢复分支通过同一真实会话取得新计划、UI 确认和 Worker/Agent，将空间恢复 Active，并正常退出。外层只在原失败、恢复、源码/配额及账号删除全部符合预期时接受此受控异常用例；没有把产品响应替换成成功。
- 两轮共四次真实启停，epoch 从 7→11；原 5 空间源码/inode/配额保持。8 个 QA 归档任务的原执行 cgroup 均不存在，业务活动任务和路径锁均为 0；任务日志保留，不手工删除证据。
- HDD 内容前后相同，只有原目录，没有创建归档载荷。120,000,000,000 字节运行盘与 300,000,000,000 字节留存盘的定长、完整预留镜像、文件系统容量和原配置保持；user NOPASSWD 与私密配置保持。
- 发布前 **18,191** 条记录摘要在发布后保持。启停后 **18,186** 条原非空间记录及另外 4 个空间行保持，目标空间行仅按实际生命周期推进。原 **8401** 条审计、**18** 份报告全部保持，新增 8 条启停审计。没有 SQL 删除或直接清理生产目录。

本地 Release 探针构建零 warning/error，55 项归档回归、14 项 CI 工作流检查、3 项入口保护均通过；安装态证据不以编译或这些单测替代。上述异常恢复只覆盖停用后 QA 异常；未声称已在安装态注入 RPC 断网或覆盖产品持久归档故障矩阵。

## 维护入口：明确保留未通过

新 HTTP 观察器在发布启动前已运行并覆盖到发布结束后：共 **5292** 个四端点采样，5254 个 200、34 个 503 维护 HTML，以及同一轮四端点各一次 `ECONNREFUSED/errno=111`。状态码、受限正文、单调时间、errno 与原始记录均保留；不能把连接错误解释为 503。另有 286 个真实浏览器观察，8081 七次实际看到维护页，保留 320px 截图；此浏览器窗口晚于首次服务重启，不用于追认该次监听连续性。

2026-11-10T13:08:49.786Z 的四个拒连与同秒 MaintenanceHost stop/start 日志对应。安装器重启直接监听 8080/8081 的进程，Kestrel 尚未恢复监听时，应用层无法返回维护 HTML。当前健康不代表 P0-05 已通过，也不推测精确中断持续时间。下一批需让监听在宿主版本切换期间独立持有，再真实标准发布复验；仅重排 restart 或保留旧进程不能满足连续性和六组件同源。

## 原始证据与剩余边界

私有证据：`deploy/artifacts/development-archive-installed-88f2df86-20261110/`，含准确 CI API 原件、发布前后快照、完整发布 journal、HTTP/浏览器窗口、固定探针源码与二进制摘要、两轮真实身份/任务回执、物理检查、账号删除和最终资源检查。汇总清单独立封存，不回写前批封存目录。

保留并定位了两次 GitHub EOF 观察失败、QA 浏览器初版相对导入路径错误、root PATH 无 dotnet 的保护检查初次失败，以及最终核对脚本把 JSON list 与 Python tuple 直接比较的错误；修正后重验，原件未删。未放宽实际来源、数据或任务绑定门禁。用户原未提交 source diff SHA256 持续为 `e56a10d30f5cb49bf771ae6fc9bd35a3564bfc586b4ca3338899940b84c0eca2`。

Authentik/UI/BFF 证明的是启停与恢复；Plan RPC 是单独的可信宿主 QA 调用，其 actor 不是浏览器归档业务授权。仍无持久归档意图、归档 Worker/BFF/Vue、当前授权与共享入场事务围栏，也没有 HDD Capture→Verify→载荷外防复活→精确退役→内核 quota/claim 归零→最后释放预算的用户闭环。报告下载前批证据保持，本批未重新签完整开发空间交付。59 项、ST272、PET6、16 子门槛及 master 总目标继续；17 项移出不恢复。项目图谱待同步。
