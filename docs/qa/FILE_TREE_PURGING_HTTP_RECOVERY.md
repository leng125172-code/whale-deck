# 目录清理：Worker、HTTP 与第11类管理员接管（2026-10-21）

状态：active、源码候选，尚未部署。接续 [SQL子门禁](FILE_TREE_PURGING_POSTGRES.md)，不是完整76项或文件管理签收。

## 实现

- 注册清理服务/仓储/调度器和独立 hosted Worker；普通 Outbox 消费器明确排除清理消息，通用 Job 取消映射清理专用消息。循环采用有界警告，不记录每秒异常全文或私有载荷。
- 独立管理员/no-store `/api/v1/files/tree-purgings` 提供 Queue、Status、ByKey、Confirm、Cancel、Acknowledge。写入口先拒绝非唯一、非规范或与既有意图ID不符的键；没有直接 Apply 或原始路径删除接口。HTTP只传捕获ID/范围，原权限、CSRF/Origin、签名确认和事务锁仍由既有层负责。
- 在原10类接管后追加 `tree-purge`，保留旧 rank/cursor/用途。迁移只扩展类型约束，非空新类型禁止降级，不改写旧历史。
- Unknown 目录发现、冻结材料、精确执行ID、原Job/lease检查、管理员计划/确认/权限复核、资源保留与事务处置均接入清理。Gateway仅查询或Discard，Discard使用当前管理员审计Context、原owner和原计划绑定，绝不调用Apply，不替换原actor/login。
- 清理已签名终态回执不允许消失或被另一结果替换。接管把同一回执与首次观察时间保留进永久组件账本；未知仍不确定，已建立删除证明不被反向恢复。Root完成且原业务响应丢失时才能协调成Succeeded；Unknown/Aborted显式接受为Acknowledged。
- 原结果、Job/Event、锁释放和当前管理员处置审计处于同一事务；接管进行中，原Worker不得抢回原操作。

## 本机验证与已知失败

- Release零警告/错误；最终2145 Unit /159 Integration通过，6本机原生依赖测试明确跳过。增加材料冻结/回执不可替换、实际内存HTTP/gRPC帧的Inspect/Discard当前actor及独立能力签名、六路由/拒绝写键和迁移检查。
- 首轮仅旧“总类型数=10”的断言失败；新版确为11，修正该计数，原overwrite rank=9/restoring rank=8、不同执行ID和游标断言保留。随后全套通过，没有删测试或放宽旧类型绑定。
- 新原生编排已补两条跨账号接管（完整完成回执协调与未知结果接受），重复预览/取消、抢占阻止、并发确认、审计故障事务撤销、原actor/login/捕获/组件事实保留、最终Outbox与锁释放。尚待实际工作站隔离PG执行，Agent与身份在该SQL用例中仍为明确stub。
- GLM 5.3 Flash只读定位文件：输入312257、缓存278784、输出3656 tokens、157443ms；平台未返回资源包实际扣费。协作代理仅新增独立路由测试，GPT实现并复核权限、永久回执、事务和接管规则。

## 后续

运行当前源码的隔离PG与CI，处理实际失败；之后补清理WebUI、第11类接管页面和真实身份/UDS/重启错误态验收，再按dev流程覆盖发布。当前未升级生产API/Worker/Gateway、未运行生产migration或清理任务；不把源码候选、普通路由反射测试或前序Agent证据代作这一步实测。
