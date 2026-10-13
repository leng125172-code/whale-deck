# P0-07 持久指标聚合验收

日期：2026-10-13。状态：`6c165e5` 已覆盖发布，原始数据/迁移/健康验证通过；生产 7d 回填期查询发生 503，性能修复 `fd77fa3` 已完成，`acb48fe` 百万级原生负载验收通过、准备覆盖发布。**尚未签收最终生产历史接口/回填**。

## 实现范围

- 原始采样仍 6 秒，原始/聚合保留 14 天 UTC 日分区。
- `metric_rollups` 增加 nullable 统计、有效/观察/Idle/Warmup/Unavailable 计数、first/last 和对应时间、加权分子/分母；没有有效值保持 null。
- 父表 INSERT/UPDATE statement transition trigger 与样本同事务标记脏分钟；时间戳更新同时标记旧/新分钟，不采用每样本日志/业务 RPC。
- 固定 advisory transaction lock、同事务 receipt/有条件 revision ACK/游标保证重启接续与迟到重算；每轮最多两个脏小时+一个回填小时，单条语句 15 秒超时、锁 2 秒超时。整个 14 天逐小时回填，不反复全表扫；写入目标固定分区父表。
- 完整分钟和完整小时持久化，当前未结束小时不写最终小时聚合。磁盘延迟按同资源/设备/时间的完成 IOPS 加权，累计计数器历史取 last，不用平均数。
- 历史延迟点额外携带 `aggregationWeight`（匹配有效样本的完成 IOPS 总权重），前端跨磁盘合并用该权重，不用各盘平均 IOPS 造成不同有效样本数失真。缺失权重保持缺口，纯 Idle 为零权重；实时与旧后端仍兼容原 IOPS 对齐路径。
- 历史查询使用完成 receipt 且未脏的聚合；当前/脏/未回填区间才查询原始样本。实时 6 秒、1h 30 秒（121 点）、24h 300 秒（289 点）、7d 3600 秒（169 点）。前端仍据返回时间轴元数据绘图，不填假零。
- EF migration 先保留旧统计与全部原始数据，新增 metadata 直到回填前不作为已完成数据。手动 down/up 只移除无法用旧 schema 表达的派生小时/全空聚合，原始样本不变；不代表开发发布自动回滚。

## 自动化与真实验收

源码 `6c165e5`：本机 Release 构建 0 警告/0 错误；380 Unit 与 62 现有 Integration 通过，容量与指标两个原生 Fact 在本机显式跳过。前端 44 单元、类型检查、lint 与生产构建通过。新增跨盘有效采样权重/权重缺失测试。后续源码修改需重新验证，不能据此提前签收运行时。

`NativeMetricAggregationTests` 已在真实 PostgreSQL 18 的 `wdv_metrics_<16hex>` 隔离库通过，两轮分别验证 `1b0303a` 与最终 `6c165e5`。从旧 migration 升级，验证旧值/原始数据保留、14 天边缘回填、上下文重启、小时/minute、首末时间、非有限值/空值/质量、完成 IOPS 加权和权重字段、累计计数器 last、当前部分时间桶、7d/24h 查询、派生 fixture 变更证明聚合读取、迟到脏桶原始回退/重算、质量更新、真实同一分钟并发提交发生在 ACK 前且未丢 revision、advisory 锁与手动 down/up。原生 Fact 未跳过，临时库/SDK 容器/凭据文件清理。测试数据是合成时间序列，但 SQL/迁移/并发来自真实 PostgreSQL，不伪称生产负载压测。

浏览器 `telemetry.spec.ts` 最终 7 项通过：320/1045/1440px 明暗主题、1h/24h/7d 切换、IOPS/延迟/总盘筛选、真实 6 秒前端刷新、迟到响应/失败/权限失效，截图检查无横向溢出。浏览器测试使用显式 fixture，仅证明界面，不证明生产 API；已独立查看手机暗色/桌面亮色截图。首轮脚本参数错误启动全套且测试浏览器未可用，全部启动失败，不计验收；修正为明确 Chrome 可执行文件及专用测试命令后 7 项通过。TypeScript 的 Object.hasOwn 目标库不兼容已改为 hasOwnProperty.call，不扩大编译目标来绕过。

远程命令 `python3 deploy/validation/validate_metric_aggregation_postgres.py`：限定随机自有库、固定 SDK image ID、public git archive 源码、非 root/只读/cap-drop/无 Docker/Agent Socket；凭据仅短时 0600 环境文件、不输出私有连接/SQL内容。临时 SDK 容器和数据库精确清理，不执行生产迁移。

生产发布前已只读抓取 13 天内、2 分钟前冻结窗口的 **4,133,024 条原始样本** 计数/摘要，保存在主机私有 `deploy/artifacts/metric-aggregation-6c165e5.before.json`（0600），第一轮发布后比对通过。派生聚合会合法重算，不能把旧 `row::text`（含新增列）的摘要当不变性断言。覆盖发布 job `7d0a5c6c-b098-424e-81b5-e9dfe3ffe559` 先完成调度，随后实际服务结束；调度成功不能独立证明更新完成。下面记录生产结果及失败继续点。

## 生产发布与已发现问题

- 第一轮覆盖已实际结束：`whaledeck-platform-update.service` inactive/dead、Result=success、ExecMainStatus=0；11 核心容器健康，Agent/Maintenance active，user sudo 授权保留。4,133,024 条冻结原始记录计数/摘要相同，transition trigger 的 invoker/owner/statement-level 验证通过。
- 后台进度从 16 增至 155 完成小时，仍处于空历史日期回填，`backfillComplete=false`。不能用 Native 边缘数据测试代替生产全部 14 天回填签收。
- 生产 Authentik Cookie 管理员通过 live/1h/24h 的 30 个真实 series/边界与独立数据库对照；7d 在未回填完时返回 **503**，本轮完整脚本失败，临时用户清理，不伪称全部历史通过。匿名读取 401。
- 修复 `fd77fa3`：把 missing buckets 合并成连续 range 后读原始数据，减少逐桶范围嵌套扫描；原始样本及 IOPS join 同时提供固定 UTC 起止，分区可以预裁剪。不减少 7d 时间范围/采样要求，不等待回填来掩盖问题。
- `06a9ce7` 新增隔离 PostgreSQL 2,764,800 条合成六日、多设备、多指标序列的冷 7d 查询；fixture 加载允许 3 分钟，但产品查询仍 30 秒超时、负载验收要求小于 25 秒。该轮 SDK 退出 0、清理通过，但 console normal 格式不满足旧 summary regex，不能确认执行/跳过计数，验收脚本失败，不计签收。
- `acb48fe` 已重跑完整原生迁移/并发/历史/负载测试并通过。TRX 的 total/executed/passed=1、未执行/失败=0，真实 result outcome=Passed；276 万条无 receipt 的冷 7d 查询 **12491ms**，唯一 benchmark 行从 TRX 提取；临时数据库/SDK/私有文件清理。该结果证明隔离数据库负载，不等同生产 413 万样本的查询耗时。源码 Release 仍 380 Unit/62 Integration（2 原生本机跳过）通过；性能修复尚须覆盖部署及生产完整历史复验。

GLM `glm-5.3-flash` 仅只读检查三份公开测试；GPT 负责实际架构、SQL/迁移、安全与复核。路由器记录输入 23359 Tokens（缓存 10624）、输出 673、37026ms，1 次成功/0 重试，费用未返回。
