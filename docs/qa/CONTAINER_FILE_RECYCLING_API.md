# 容器回收管理员 API 候选

2026-11-05，承接[真实PG持久化候选](CONTAINER_FILE_RECYCLING_PERSISTENCE.md)。API源码 cf78034b，测试身份夹具修正后冻结 c570ac4d。新增管理员接口与真实回环 MVC/Cookie/CSRF 测试；Vue、任务摘要、部署与真实身份/文件整链仍待接入。

接口 `/api/v1/containers/{containerId}/files/recycles` 提供异步登记、原ID/by-key查询、明确确认和取消；`/api/v1/files/container-recycles/recovery` 提供当前管理员的有界Unknown列表、详情和明确处置。全部Administrator策略、no-store及64KiB请求上限，沿用BFF/Origin/CSRF和实时业务授权。控制器仅做传输，不直接调用Agent执行回收或恢复。

规划正文只接受instanceId/path/expectedVersion；客户端不能提交计划、源属性、操作上下文或恢复来源。规划、确认和处置均拒绝重复/未知/缺失字段、类型强制转换及非规范版本；查询仅接受明确的单个规范take（1–50），其余字段拒绝。写操作要求单个规范幂等键，确认/取消/处置必须匹配原ID；丢失登记响应后查询原key，不能据缺席自动重建任务。

Release全量集成705通过、0失败、7默认原生跳过，含45项回收相关（44个新增HTTP用例与原有模型用例）。验证匿名401/普通用户403、CSRF与Origin拒绝、登记202/no-store/Location、同key不重复登记、原登录查询/重登404、实时撤权403、确认仅登记原执行job且不Apply、取消和分页，以及当前另一管理员明确处置。Unknown处置后原Unknown和两个null源事实保持，取消确认不调用Root。

首轮44项通过后，新增另一管理员处置用例在全量回归中收到403：测试主体没有配置到既有Directory.AdditionalAdmin。保留失败TRX，按原夹具授权第二主体后全量复验通过；没有放宽生产授权或普通用户断言。早期目录定位误用frontend，实际前端在web，已纠正。

测试使用真实回环HTTP、MVC、Cookie和CSRF；身份目录、仓储、Agent回执是显式夹具。没有用本批补签真实Authentik、PG、HDD或浏览器。持久化已由前批另行验证。本批未部署，宿主3725aa7/应用34153d2及2346保护、11核心、回收Root4/HDD7按前批基线只读核对保持。

下一步：web/src/services/containerRecyclings.ts、容器文件回收面板、Files恢复处置面板、任务结果安全摘要及对应测试，再准确CI/标准部署和真实整链。现有宿主FileTreeRecyclingPanel不作为容器回收页面复用或验收。恢复仍需独立业务意图；59项、整体25%–35%粗估与酒馆/看板娘0%功能进度保持。

私有证据container-recycle-api-1721bf54c17242c1a9d9979cbdc39741封存14文件，清单摘要21c25a8037a7d64a96142f4cb0c506136d2b1dbe50d69ee0545fceb6cafd525c；含七份源码、首轮/失败/最终TRX、前批基线保持和范围检查。最新CI终态本批未核验。
