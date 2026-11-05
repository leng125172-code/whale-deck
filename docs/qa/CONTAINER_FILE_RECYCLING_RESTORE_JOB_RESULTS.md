# 独立回收恢复Jobs安全摘要候选

2026-11-06，候选f08d53e33c2bc96f9fbc9b42d5f9617f92571c65，承接[API/Vue候选](CONTAINER_FILE_RECYCLING_RESTORE_API_UI.md)。已接后台快照/任务服务白名单、前端任务摘要和真实PG投影验收；生产未部署，本批不签真实身份/文件/HDD页面整链。

## 固定白名单与引用归属

新增ContainerRecycleRestoreJobResults的17字段schema1：恢复意图/当前Job、原回收意图/原执行、阶段/状态/代码、原生回执和原来源状态、当前绑定/保留/恢复/本操作恢复归属可空事实、HDD曾验证/回执时可用及保存时保护。原执行ID须等于原回收意图的旧域派生ID；本恢复执行须等于新恢复域派生ID，不得混用。规划Job仍是本意图ID。

不投影actor、login、owner、容器/实例、路径、完整命令/计划/回执、哈希/摘要/确认权限、UID/GID、大小或属性。快照仅在原意图完整绑定有效时保存；任务服务对两个新Job类型明确走专用严格解析，空值/原型JSON、缺失/额外/重复字段、类型/大小写/ID/阶段/代码或事实不匹配时返回null，不能落回Succeeded通用原始JSON透传。

规划Ready/Expired/Canceled/Failed只能有空执行事实，不能宣称文件恢复或保护已开启。执行Completed必须明确证明本操作归属、原来源Restored和完整HDD条件；未claim的Aborted保留null源事实、byThis=false，已验证来源的Aborted才可报告Trashed/retained。Unknown有/无回执分开，缺席始终是“未证明”，不是false或未写入；原来源状态Restored也不等于本操作已恢复。

另明确覆盖迟到证明：原任务因没有回执成为Unknown，处置时Discard可能首次观察到原执行Completed/Aborted。现有业务允许在没有旧原生事实时保存这份新证据；既有Unknown回执的事实仍不可改写。摘要因此允许Acknowledged携带有效迟到事实，但任务保持Failed和“管理员已处置”，不显示为恢复成功，也不再执行Apply。C#/TS及业务回归和真实PG均覆盖这一区别。

## 后台/前端接线

Repository的规划/执行完成、Unknown更新及管理员处置统一保存标准历史快照，OperationService专用投影与数据库结果一致。Jobs页对本两个类型禁通用重试/原始pre，展示本恢复与原回收引用、原来源历史状态、独立归属和HDD/保护的保存时事实。摘要无写入按钮，不修改原回收任务或重新预检。字段不可信时只显示无法核实提示，不渲染被注入的私有路径。

本轮新增26项C#（含25摘要/任务映射与1无回执迟到证明业务）和18项TS/组件检查通过。最终5463 Unit零失败零跳过、741 Integration通过/7原生依赖默认跳过（748总），前端68文件/1369单元、类型/lint/构建、8范围通过。默认跳过未计真实数据库通过。

构建产物本机5175预览用合成任务API执行320/1440宽、600高、明暗四组合；每组依次打开成功/Unknown/已处置/Ready/历史绑定变化/空/损坏七种任务详情，核对null与独立归属、不出现私有路径或通用重试、无写请求及无横向溢出。最终4/4组合通过，四张实际PNG保留；这不是生产、真实Cookie/Authentik或原文件恢复验收。

初稿任务页引用active而实际状态名为selected，类型检查TS2339后修正；原生夹具每次创建JsonSerializerOptions触发CA1869，已复用现有静态Web选项。一次只读测试文件整理脚本找不到预期函数片段，未写入源码；改为明确的新夹具。首轮预览使用文字修订前构建产物，四例因旧的“恢复或恢复”文案失败，修正文案及旧规划用语并重建后全过。初稿诊断/编译失败/预览上下文和截图保留，未关闭检查或弱化断言。

## 真实PG与生产保持

冻结候选的标准dispatch原生宏单独执行真实隔离PG，规划、Unknown、Acknowledged、Canceled、Succeeded及缺回执后迟到Completed处置分别检查持久Job ResultJson与OperationService输出相等、意图/新旧执行ID与facts相等，并确认不含私有字段。原有事务/审计/共享保护与来源目录回归同时执行；计划、身份和Root回执仍是明确夹具，不转签Root或真人页面。

unit whaledeck-qa-recycle-restore-job-pg-35faab06，Invocationd4acf8a22cd84377aa8fb8b2a0f7185c，准确exited/Result=success/ExecMainStatus=0。原生宏1执行1通过零跳过，含本摘要marker；两份临时数据库/SDK容器清理确认，外部投递0。

两次独立生产只读快照均等于前一已签基线：2346保护文件、11核心healthy、回收Root32/HDD35、独立恢复Root5、目录Root96及既有数据库保持；两宿主f333a69a/Agent1.57，四容器/运行checkout仍58b8174，无部署、迁移或新文件RPC。

证据deploy/artifacts/container-recycle-restore-job-results-35faab06b8b14a4289f3642bc10499ed，共44文件/12变更源码，EVIDENCE_SHA256.json摘要796b4994a0fdf39df56082094c1b4ef4e865eeafbbff13f3934c4f50297495d4，所有文件重算通过。原始失败、完整本地检查、实际截图、原生回执及保持快照均保留。

前序API/Vue提交c8661c79准确CI已success，本候选仍须看推送后的准确CI。下一步四镜像/宿主版本与标准部署/生产迁移，再真实身份来源目录、原inode/HDD恢复与任务页整链；完整故障矩阵、长期清理及其他59项继续。当前完整主项0/59、阶段0/6、25%–35%粗估及AI子计划0%保持，17项移出不算完成。

项目图谱whale-deck/container-recycle已按稳定eventId回执同步active，pending移入synced；未标整项或阶段完成。
