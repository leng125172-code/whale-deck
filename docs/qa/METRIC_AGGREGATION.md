# P0-07 持久指标聚合验收

日期：2026-10-13。状态：源码实现与本机验证阶段，**尚未部署/签收原生数据库**。

## 实现范围

- 原始采样仍 6 秒，原始/聚合保留 14 天 UTC 日分区。
- `metric_rollups` 增加 nullable 统计、有效/观察/Idle/Warmup/Unavailable 计数、first/last 和对应时间、加权分子/分母；没有有效值保持 null。
- 父表 INSERT/UPDATE statement transition trigger 与样本同事务标记脏分钟；时间戳更新同时标记旧/新分钟，不采用每样本日志/业务 RPC。
- 固定 advisory transaction lock、同事务 receipt/有条件 revision ACK/游标保证重启接续与迟到重算；每轮最多两个脏小时+一个回填小时，单条语句 15 秒超时、锁 2 秒超时。整个 14 天逐小时回填，不反复全表扫；写入目标固定分区父表。
- 完整分钟和完整小时持久化，当前未结束小时不写最终小时聚合。磁盘延迟按同资源/设备/时间的完成 IOPS 加权，累计计数器历史取 last，不用平均数。
- 历史查询使用完成 receipt 且未脏的聚合；当前/脏/未回填区间才查询原始样本。实时 6 秒、1h 30 秒（121 点）、24h 300 秒（289 点）、7d 3600 秒（169 点）。前端仍据返回时间轴元数据绘图，不填假零。
- EF migration 先保留旧统计与全部原始数据，新增 metadata 直到回填前不作为已完成数据。手动 down/up 只移除无法用旧 schema 表达的派生小时/全空聚合，原始样本不变；不代表开发发布自动回滚。

## 自动化与真实验收

本机 Release 构建 0 警告/0 错误；380 Unit 与 62 现有 Integration 通过，容量与指标两个原生 Fact 显式跳过。前端 43 单元、类型检查和 lint 通过。后续源码修改需重新验证，不能据此提前签收运行时。

`NativeMetricAggregationTests` 在 `wdv_metrics_<16hex>` 隔离库从旧 migration 升级，验证旧值/原始数据保留、14 天边缘回填、上下文重启、小时/minute、首末时间、非有限值/空值/质量、完成 IOPS 加权、累计计数器 last、当前部分时间桶、7d/24h 查询、派生 fixture 变更证明聚合读取、迟到脏桶原始回退/重算、质量更新、真实并发提交发生在 ACK 前、advisory 锁与手动 down/up。尚未运行，不把测试源码当证据。

远程命令 `python3 deploy/validation/validate_metric_aggregation_postgres.py`：限定随机自有库、固定 SDK image ID、public git archive 源码、非 root/只读/cap-drop/无 Docker/Agent Socket；凭据仅短时 0600 环境文件、不输出私有连接/SQL内容。临时 SDK 容器和数据库精确清理，不执行生产迁移。

生产发布前必须另外抓取冻结截止前的原始样本计数/摘要，发布后比对；派生聚合会合法重算，不能把旧 `row::text`（含新增列）的摘要当不变性断言。实际部署版本、耗时、回填进度、数据库读取计划/预算、核心健康、原始摘要和权限/UI验收在运行完成后补充。

GLM `glm-5.3-flash` 仅只读检查三份公开测试；GPT 负责实际架构、SQL/迁移、安全与复核。路由器记录输入 23359 Tokens（缓存 10624）、输出 673、37026ms，1 次成功/0 重试，费用未返回。
