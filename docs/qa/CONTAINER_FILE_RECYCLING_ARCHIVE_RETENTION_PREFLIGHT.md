# HDD 归档留存到期只读预检候选

2026-11-07，候选 `73ef4f9ff3d8657368fa64a1fc1e0c68010d0b1e`。新增内部 `ContainerRecycleArchiveRetentionPreflight`，没有安装、CLI/RPC、永久业务、策略配置页面、调度、删除、claim 释放或 stage 清理入口。

## 明确的期限与资格语义

输入为准确归档请求、显式 `retainDays` 整数 1–3650 和固定 `mode=ArchiveData`。没有默认期限或请求自带的当前时间。期限从原 HDD 捕获的 `createdAtUnixMs` 起算，每天为 86,400,000 毫秒，不代表清理完成后或复制成功后重新计时。该时间属于已验证的历史摘要并同时绑定原 purge 计划，不能用文件 mtime 或重算一份历史哈希替代。

预检沿原 archive→purge→source→HDD 锁顺序，只打开既有 Root 锁，不创建缺席锁，不调用 inspect/reconcile 或补写 claim。要求：

- 归档 Root 为 Completed；原清理 Completed/unlinkConfirmed、原 source/marker 为 Purged，全部记录摘要对应归档计划。
- surviving claim 精确匹配本归档 ID/owner/plan；丢失或冲突直接拒绝，不能在预检中修复或覆盖。
- 当前配置/UUID/目录与归档 HDD 身份保持；完整历史 JSON 和 `.data` 大小/SHA、原 inode/ctime 等身份通过，在验证前后重新证明存储。
- 当前容器/session/资源保护仍有效；实际读取复制目标验证完整字节、属性和发布身份。目标文件缺席或被替换时，历史 Completed 保留，但 `bindingCurrent=false`，不具备退役资格。容器/session/挂载本身失效则拒绝预检，不签容器离线可清理。

Root 当前时钟早于捕获时间则拒绝；未到期返回 `retentionElapsed=false`，诊断资格为 `retirementEligible=retentionElapsed && bindingCurrent`。这个字段不是删除授权。结构校验约束精确类型、日期/期限与资格逻辑；其未加密指纹不能抵御重算哈希，未来消费方必须使用 `revalidate()` 返回的实时结果，并另补确认、策略授权、来源围栏和可恢复退役日志。年龄跨越到期边界或目标绑定变化会使旧计划失效。

## 已执行的自动化检查

16 项新增临时 Linux 实盘文件/Root/HDD 读取测试全部通过，其中 15 项验证预检，另 1 项验证二进制命名空间采集。命名空间、硬件证明及正向到期时钟为显式替身。覆盖到期前一毫秒/恰好到期、时钟早于捕获、来源/清理/marker/claim 不一致、历史回拨与重算摘要、同内容 HDD 换 inode/损坏、缺席/不安全/忙锁、session/存储失效、目标消失和旧计划失效、精确输入及重算指纹后的非 bool/float/逻辑欺骗。没有将时钟替身当成真实生产到期证明。

最终完整回收相关 Linux root 测试 **669 项通过、零跳过，45.981 秒**；8 范围、14 前端 CI 合同、4 文件编译和 Git diff 检查通过。初版 14 专项/667 全套及复核后的 15 专项/668 全套通过；实机验证器发生以下采集失败后补第 16 项二进制/空目录回归，再跑完整最终版本，没有放宽安全或来源边界。

## 首轮实机失败保留

`73ef4f9f` 的全新资源门禁已实际完成复制及同源拒绝，但新预检的前置状态采集误用了 Root JSON 日志快照来读容器二进制负载，在 `recursive_acl_restore_agent_io.py:47` 断言失败，unit 退出 1、68.173 秒；该批尚未调用新预检，没有通过留存整链。原 Root/claim/HDD 和失败源码保留，不追认。

修正只涉及 QA：以二进制快照读容器文件，同时单独记录 Root/0700/空目录身份，拒绝符号链接和非空暂存。内核预检保持 `73ef4f9f`。失败批次另经独立完整记录/当前文件/HDD核验后，仅清除准确自有惰性容器、原 QA 文件、新副本和空 stage；Root/claim/HDD 保留，未调用任何 Apply/ACK 或伪装成 RPC。收尾工具首次缺冻结依赖、第二次固定 RPC 探针文件名与本低层夹具不符，均在写入前拒绝；补齐冻结证明并只绑定实际固定 QA 路径后通过。所有来源/claim/HDD 断言保留，最终独立收尾复核通过。

## 修正后的全新资源实机分项

验证修正 `b90fe893916c5f110e79e6d7e2bad32dbdc60aab`，unit `whaledeck-qa-archive-retention-plan-b90fe8930001` / Invocation `68dbce7597dd4bd79a140ad2054477ae`，退出 success/0，80.719 秒。冻结 17 宿主模块和 119 验证闭包文件；全新自有惰性容器、新来源/清理/归档 ID 和本轮私有 Root 注册表，未替换生产注册表。

实际回收并仅清理本轮原件后，从当前 HDD 复制 1,048,583 字节为新 inode，完整字节、UID/GID/mode/mtime/单链接与 `user.*` 属性对应原件。新预检在副本存活时计划及重核相等：Root 真实时钟前后均早于捕获后 30 天期限，`bindingCurrent=true`、`retentionElapsed=false`、`retirementEligible=false`。该 30 天只用于本 QA 显式输入，没有修改生产默认或配置策略。

存活副本和 HDD 先经独立进程实读，才准确清除本夹具的新副本及空 stage。随后新预检仍只读：历史 Completed/发布身份保持、`bindingCurrent=false`，未到期及无退役资格。两阶段 Root/claim/原 source/purge/marker/HDD 和目录内容的前后快照分别一致；旧计划换新 ID 及新来源计划在收尾前后均拒绝。最终独立核验与第三次当前 HDD 重读通过，夹具准确移除。

只签真实时钟的未到期分项及当前副本失效后的保留分项；正向到期年龄仅有临时测试时钟替身，不签真实生产到期或实际退役。当前副本核验也不是其未来耐久性的承诺，不能据诊断回执直接删除备份。本批没有真人身份/Vue、签名 RPC、安装或永久留存业务验收。

原业务回收 78 / 恢复 29 / 清理 65 / 归档 46，活动 0。补充核对生产 archive Root 48 与前次签收基线完全一致，原 44 归档业务全行摘要保持，另外 2 行与前次签收原件完整 SQL 行一致。其他 Root 202/恢复95/目录102/purge90、2346 保护/11 核心/21 安装模块及产品 `f318d9ab` 保持。HDD 本轮失败后 181→183、成功后 183→185，仅各增本轮两份历史；连同前一留存批次，相对于上次真实页面签收 179 条恰好只增加这三个新来源的六份历史。源与 HDD 同 device，不签跨物理盘灾备。

封存目录均为 Root:Root/0700、文件 0600，以下数量均另加 manifest：

- 失败 `container-recycle-archive-copies-b0c254cf558a4fe8b0ddf7db969f08f4`：156 文件，SHA256 `dc9d1c1f2a71555686cd52ab915a51337f356874075123e9a0cd27f8b3165152`。
- 独立收尾 `archive-retention-preflight-failed-disposal-57b7defd70454d3089110323d2634467`：97 文件，SHA256 `32f343f5b6823a43a7ac926fc0412cd1c6489431534cfe5339fbe507fe16a688`。
- 全新成功 `container-recycle-archive-copies-002dda1704af49659e62a655d27f161c`：170 文件，SHA256 `f277a9bb73c6fe2ff2407b46fd2769c4b9cddc719fc4d43aa12f1e701807624d`。
- 补充保护 `archive-retention-preservation-fa1435b9db7d4707a3fd682b2b9bbba2`：1 文件，SHA256 `309b0f4393757ab22254f8676004f963354986c4f1f65857f01157754abad92f`，独立保存生产 archive Root、完整行保持与六份 HDD 增量核对。

## CI 与续跑

前批 `7ce9839d` / CI `42333250826` 和 `0a7ac405` / CI `42333610930` 已分别核对 12 项 success。本轮结束前又核对内核 `73ef4f9f` / CI `42334368674` 全部 12 项 success；验证修正 `b90fe893` / CI `42335125756` 的 5 路基础检查成功、6 浏览器分片仍运行，尚待准确完整终态，不能沿用旧绿灯。

继续实现独立 HDD 退役确认、来源围栏、持久化墓碑/中断恢复及兼容读取，再补签名 RPC、安装态、永久策略/API/Vue、真实到期与故障矩阵；stage 安全退役和来源复用、维护连续性、大文件及完整 59 项继续。17项移出不算完成；整项 0/59、阶段 0/6、工程粗估 25%–35%、酒馆/Live2D 功能 0% 保持。
