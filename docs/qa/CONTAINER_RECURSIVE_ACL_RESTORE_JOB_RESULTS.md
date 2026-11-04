# 递归ACL恢复任务中心安全结果

2026-11-05，dev候选；承接[真实执行中退出](CONTAINER_RECURSIVE_ACL_RESTORE_INFLIGHT_LOGOUT.md)发现的任务详情空结果。本文记录实现、候选验证及后续实机边界，不将页面夹具或隔离PG代替部署验收。

## 实现与事实边界

应用层新增26字段固定摘要：原意图/任务ID、预检或执行阶段、状态/结果码、ACL类型、来源原计数、本次计划、五类实际计数、原回执及历史/围栏事实。生成前由仓储核验原意图/计划/执行/回执绑定，摘要不含命令、计划、路径、容器实例、UID/GID、源捕获ID/摘要、actor/login、MAC/key或capability。API按原任务类型、确定性执行ID、状态、字段集/长度/深度/数字及回执关系白名单重新投影，拒绝多余/重复/缺失字段和不相符的结果。摘要格式校验不是对原Root实物的独立证明。

结果在原状态事务内写入OperationJob.ResultJson，覆盖Ready/失败/取消、完整完成/Superseded、Partial/Unknown以及明确处置。处置保留原Partial/Unknown回执和计数，只更新处置事实及围栏；无Root回执时计数为null，展示“未证明”，不发明零写。旧规划Job完成后的Ready快照保持历史事实，不随后续确认、取消或到期变成执行结果；旧缺失摘要不回填为成功、不重放恢复。

复用现有ResultJson jsonb字段，无模型/表结构变化，不新增迁移。Jobs list/detail继续Administrator策略；API和Worker不增加宿主或Docker权限。普通任务的失败/取消结果隐藏策略保持，恢复任务仅返回上述摘要。Vue专用组件替代恢复任务的原始JSON回退，分别展示来源计数、本次计划、实际结果，清晰标注当时事实；非法/旧结果只显示缺失提示，不输出原JSON。处置不解释为恢复成功，仍禁止通用重试；Partial/Unknown不提供通用取消。

## 候选检查（冻结前）

- 5071后端Unit全部通过（23新增）；默认Integration660通过、7原生默认跳过，不能算真实PG通过。最后新增原生断言的Integration重新编译零警告/错误，隔离PG尚待执行。
- 49前端定向单测通过（含30新摘要解析/组件用例及Jobs关联）；类型检查与生产构建通过。
- 8项生产构建预览通过：4新恢复、4旧ACL；320/1440及明暗主题，正确计数/Unknown缺回执/Partial/处置/Ready/legacy/异常结果、无私有字段/横向溢出/危险重试或写请求。所有API及身份均明确夹具，不是真实登录或恢复，20新截图仅作UI候选证据。
- 格式、oxlint、eslint和diff通过；CI分片9检查与只收集核验通过，774测试、六片无遗漏/重复，旧749原命令完整保留（新增累计25，本批4）。

新增隔离PG断言覆盖真实仓储和任务接口投影：Ready/完成/取消/失败、无回执Unknown、原Unknown/Partial处置保留、旧Ready历史以及审计失败导致结果JSON与状态同事务回滚。收据和身份仍是测试投影，缺少真实Agent/HDD事实不能记通过。

初轮检查失败保留：C#源摘要属性误用ChangedCount（实际ChangeCount）、测试误给init-only Job属性赋值、原生测试未缓存JsonSerializerOptions触发CA1869；前端Object.hasOwn不在现有目标库，改为hasOwnProperty.call，未升级编译目标。另一次整理夹具命令在web目录仍加web前缀，触发FileNotFoundError，改正确路径后检查通过；未修改产品部署或旧实机原件。

## 继续验收

候选尚未部署，运行仍eeb24dd。需要冻结候选、真实隔离PG、准确CI终态、标准覆盖与新资源真实Authentik→任务页→API/Worker→Agent→实际文件/HDD整链；不能把本页预览算生产。其余撤权/重启等故障矩阵、可恢复删除/长期清理、维护连续性和59项仍继续，17项移出不算完成，不提升主任务完成率。


## 合法拒绝边界补充与首轮真实PG

c166bd8真实隔离PG宏门禁执行1/通过1/零跳过，dispatch-9b31be39507243afa9604ca99f6b4a15状态Passed、cleanupConfirmed=true，源码SHA准确匹配。新增结果持久化/投影/无回执Unknown/处置/审计回滚断言实际执行，但收据及身份仍为测试投影，不替代Root或真人页面。

进一步对照Agent错误映射发现：TARGET_CHANGED既可能是已完成执行的Superseded结果，也可为计划期合法失败。摘要白名单原先过严隐藏了后一类，修正为仅规划Failed允许这一拒绝码，执行状态仍按原回执区分；新增C#/前端及原生PG用例和专属阶段标记。修正后定向24后端、50前端通过，仍须冻结补充修订再跑准确原生门禁；不把c166bd8旧回执转签给修正。


## 处置时补获原执行回执

只读审查发现并经服务/仓储/ValidFacts核实：Unknown此前无回执时，原执行Discard可补获Completed/Partial/Aborted，不应只接受Partial/Unknown。并且UNKNOWN_ACKNOWLEDGED按原Unknown状态记录，即使补获Partial也不应误要求PARTIAL_ACKNOWLEDGED。后端与Vue已同步接受这些合法事实，任务仍为Failed/Acknowledged，不自动解释为成功或重放；原Partial已有回执时仍不能改写既有计数/摘要。新增三态后端及前端投影测试通过，定向27/53；扩展原生PG分别核对四种补获结果，增加专属阶段标记，最终修订仍待准确PG运行。

新生产预览扩展到第11个显式夹具时，十进制10拼在GUID末位形成无效ID，白名单正确隐藏它，四个主题场景失败；已按十六进制单字符生成并保留原失败截图/上下文，接下来用全新预览进程复验。此为夹具失败，不当作真实页面验收；修正前构建、23后端/49前端/首8预览与首PG等原结果按各自冻结保留。


## 最终候选ac90911验证与生产保持

最终实现冻结ac909114d65101639d12f53c5d921edc69c2a89d。真实隔离PG状态dispatch-21c15666dad5456db4bd6dc2cb4d75ce为Passed，executedTests=passedTests=1，cleanupConfirmed=true；实际出现job-facts-privacy-null-counts-audit-rollback及job-facts-late-receipts-acknowledged专属阶段。两自有库、SDK容器和scratch均清理；各阶段断言不另冒充独立测试数量。617a1f3的准确PG也Passed/1/1/cleanupConfirmed，不拿前序回执替代最终候选。

最终5075 Unit/660 Integration通过，默认7原生跳过独立列出；1292前端单测全部通过，类型/生产构建/格式/oxlint/eslint通过。准确干净ac检出重建后，最终8生产构建预览再次通过（4恢复+4旧ACL）；恢复每主题11类数据，包括规划目标变更拒绝、无回执Unknown、原Partial处置、原Unknown处置补获Completed/Partial/Aborted、legacy和私有异常。36张新预览截图保存并人工查看缺回执、部分处置、补获完成与中止计数。身份/API/收据为明确夹具，绝非真实工作站新功能验收。

末次产物检查命令误查本地dist/build-info.json而退出；实际该文件由deploy/gateway/Dockerfile在镜像内生成，Vite目录本来不含它。改按172份公开Vite产物哈希归档，并重新从干净准确检出构建及通过8预览；未把该检查失败当发布失败或重发生产操作。该证据不声称六组件CLI或已安装新版来源已核验。

生产前后只读快照逐字节及九字段保持：1952保护文件、来源Root1570、恢复Root196/HDD132、目录94；普通业务54意图49执行103Job、恢复13意图10执行/活动0，其他活动Job/终端/宿主锁/发布保护均0。11核心healthy、ID/镜像/StartedAt和两宿主eeb24dd/PID1379812与1379807保持，无生产发布、重启、恢复重放或回滚。源/HDD同device旧限制及维护连续性缺口保留。

私有证据deploy/artifacts/recursive-acl-restore-job-results-9314fcd9f635455594c8ee12d3589bf5含原始基线、后置保持、三个准确PG状态/Invocation终态/日志、36候选PNG、夹具GUID失败原件及公开构建哈希。60文件逐项SHA256通过，清单摘要4a8c1157e784d256a59a4606022c3acff747e56bb9e998df3879c3ee90e1701e。三QA unit均在准确回执归档后停止；运行服务未停止。

候选已验证仍非部署验收：继续准确CI终态、标准覆盖，再新资源真实Authentik→Vue任务摘要→API/Worker→Agent→实体/HDD，特别是Unknown/Partial/处置与历史规划的展示；随后撤权/重启等剩余矩阵、可恢复删除和59项目标。不得把本页提升为整项或整阶段完成。


收尾：范围8检查再通过，变更MD的740个本地链接缺失0，diff通过，Git索引1000:1000/664。ac90911准确CI41935193964查询时仍in_progress，617a1f3旧CI41934293674已completed/success，不转签新候选。项目图谱事件whale-deck.container-acl.ac90911.restore-job-summary-candidate已获project/key/event匹配、duplicate=false成功回执，pending移入synced，阶段保持active。
