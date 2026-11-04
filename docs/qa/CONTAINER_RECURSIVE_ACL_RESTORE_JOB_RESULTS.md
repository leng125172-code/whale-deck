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
