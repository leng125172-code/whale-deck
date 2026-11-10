# 源码退役独立长任务与固定 helper

2026-11-11，接续 a8796811，新增宿主退役独立任务控制。原五个产品空间保持；仅删除新建隔离 QA allocation。本批不代表完整产品退役、P3-18 或59项主任务完成。

## 实现和授权边界

退役采用独立六字段请求、任务目录、systemd 单元前缀和 credential 名，plan/execute 与归档 plan/capture/verify 严格分离。原归档请求不获得删除含义。共用已有的短时 journal 锁、不可变 admission、持久一次派发、精确 cgroup 证明和取消；连接丢失、派发歧义或进程中断均不自动重启同任务。精确停止后，明确的新 task ID 可接续原 retirement engine request。

worker 仅使用 CAP_SYS_ADMIN 与 CAP_DAC_OVERRIDE，删除真实 Runner 用户所有的0700源码子目录；控制进程没有 capabilities。HDD通过 systemd ReadOnlyPaths 强制只读；readonly attestation 保持原设备、UUID、镜像 inode 和完全预留核对。新增显式布尔只读模式，默认归档行为仍要求可写。Landlock允许已核对 NVMe rootId namespace 和退役 journal 的写入，确切 workspace 由固定可信执行代码及请求绑定限制，不能宣称内核逐空间隔离。

release-only seccomp 允许 quota limit 调用及必需读取，拒绝 project tag、权限/所有者/xattr修改、exec与其他 ioctl。seccomp不能检查 SETQUOTA 指针内的限额内容；准确清零、无引用和回读由可信执行内核保证。独立失败码使用 WORKSPACE_STORAGE_RETIREMENT_*，私密路径不进入结果。

标准 install/validate 清单纳入 retirement、retirement-execution、retirement-operations 和 retirement-transport 四模块；独立 root:root/0700 journal，拒绝符号链接和与原 journal/base 同 inode。安装校验检查每模块 root:root/0750、单链接、非符号链接并逐字节比较。

现有 Agent sudo规则原本允许固定 whaledeck-privileged helper 的受控参数，本批未修改规则，Agent账户仍是可信边界。尚无新的 C#签名 RPC、capability 广告、Gateway、当前身份授权或 Worker/API/Vue 删除入口；不能用固定 helper 的成功替代签名或用户授权验收。所有完成回执始终 releasesApplicationBudget=false。

## 真实验证与失败保留

开发空间联合测试213项全部通过、0跳过，包含真实fork seccomp、独立 schema/回执绑定、布尔与整数类型、一次派发、歧义派发、取消、显式恢复和脱敏失败码；quota/cgroup单元夹具明确使用 double。另有14 CI合同、8范围基线与5安装器无副作用预览通过，shell语法通过。

执行 validate_development_archives_native.py --retirement-helper，在专属新建NVMe/HDD各2GiB全额预留ext4中运行真实 systemd/Landlock/seccomp helper。路径重写副本保留完整源码及摘要，CPU限制收紧为5%，并非安装态或签名RPC。除原归档与7个退役内核场景（含5次真实强杀）外，新增2个QA空间：真实65534所有的0700子目录退役；300文件执行中观察/精确取消、同task不重发、新task接续原请求完成。内核限额及用量八项全零、永久围栏、HDD字节/inode保持、晚取消不降级、执行资源精确收尾通过。

首轮证据 development-archives-native-c4809f18ea0e47e8af7066f6342f1cfa 保留：原HDD attestation要求可写，退役只读计划被 WORKSPACE_STORAGE_READ_ONLY 安全拒绝，源码删除未开始。显式只读attestation修复后的 development-archives-native-ba093f563576480aa51876c2ccdbd952 通过；最终失败码修正后重新实盘复验另行封存。未放开HDD写权限。

最后失败码修正后复验 development-archives-native-07d695d94f474de9a51039201b0f2380 又取得真实启动竞态：恢复worker在execution.json之前因观察者短锁退出，随后一直Pending。原件及无执行证明的PID1日志保留，隔离清理通过。新增真实flock确定性回归先取得错误，再将worker读取请求/开始执行的短锁改为2秒有界等待；Observe/Cancel保持非阻塞，取消持锁提交墓碑后，等待worker只返回Stopped，不执行源码工作。取消测试同步验证这一顺序。

本批摘要、原测试输出、生产前后核对和原生清单位于 deploy/artifacts/development-retirement-helper-20261111/。原5空间/18报告/18,384条原记录、HDD和归档journal、120GB与300GB完全预留强制限制、11核心及私密配置保持。用户未提交source helper改动及两个Git index所有权保持。

修复短锁竞态后的完整实盘复验 development-archives-native-a22d75794ec84e6c9bdbd11af93c6c3d 通过，包含原归档、7条内核退役与2条固定helper路径；完整隔离镜像、挂载及执行资源清理通过。排除用户原source改动的准确暂存tree 7e2db6d6258134b4c375bd96b14e35922ac03b4c 也实际执行213测试全部通过、0跳过。

## 发布与接续

a87968110d33c8ca2416960eb0fab4622f85d195 准确CI 42876046498 已13/13 success。本批仍需自身准确提交CI及标准dev覆盖部署，然后接独立签名合同/Agent/Gateway、当前身份授权、持久退役意图及预算墓碑、Worker/API/Vue和新专属空间的真实用户闭环。构建不能代替测试，隔离helper不能代替产品验收。

完整主任务维持0/59，ST272、PET6、供应商/Live2D16继续，17项移出不恢复。项目图谱保留本地pending待同步。
