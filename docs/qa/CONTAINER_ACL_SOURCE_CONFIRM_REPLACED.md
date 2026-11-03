# ACL恢复：Ready后有效来源替换、原确认拒绝实机验收

2026-11-04，仅新增签收**Ready后同一captureId的元数据被另一有效、自洽记录替换，原确认仍绑定原快照并拒绝**。这不是前批文件不存在的404场景，也不是畸形JSON/错误digest。产品仍28062da/协议1.52，无部署、服务启停、SQL/缓存/系统时钟修改或浏览器身份注入。

## 门禁与独立修正

真实门禁冻结`6c5b331fd6e3a5a22a5fe3c267b17d7c6868cb2f`，干净树、运行前87文件Python/MJS闭包及13个安装helper逐字核对。

```sh
sudo -n python3 -u deploy/validation/validate_container_acl_source_confirm_replaced.py 28062da454fd579d6860391a88816dab0bfca143
sudo -n python3 -u deploy/validation/verify_container_acl_source_confirm_replaced.py container-acl-source-confirm-replaced-d13f396efc71436d8fdde1f599a16539
```

实机门禁退出0。**独立首轮失败**：复制验证器时留下`source-missing:`数据前缀，与父门禁实际`source-confirm-replaced:`产生不同字节摘要；拒绝发生在文件数据生成规则检查，不能把这次独立检查说成通过。`06f179b`修正并增加父门禁/独立验证器生成规则的正负一致性回归；冻结修正版87文件后，仅以同一原始证据只读复核，退出0，未重新捕获、替换、确认、取消或新建身份。原失败`independent.log`、修正后的`independent-corrected.log`/回执和`verification-source-corrected.json`分别保留，不称整批首轮全过。

私有证据目录`deploy/artifacts/container-acl-source-confirm-replaced-d13f396efc71436d8fdde1f599a16539/`为root:root/0700，文件0600，不入Git。恢复意图`51d606ea-7072-8bc5-e1c4-0e5af1deea63`；新捕获`ce398bc520b24af454aa23e0e9d9c8a4`，恢复执行ID始终null。

## 精确替换工具与恢复边界

`owned_acl_source_replacement.py`是独立、显式选择的QA库，无CLI、安装或sudo/RPC授权。沿用[新捕获隔离证明](CONTAINER_ACL_SOURCE_QUARANTINE_CANDIDATE.md)，只接受已正常实盘验证且不在捕获前清单的新记录；原隔离工具的“规范名被占用则拒绝还原”规则未放宽。

1. 原记录字节/inode保留在带私有持久日志和目录inode证明的隔离目录；替换记录使用**另一个临时inode**。只把原createdAtUnixMs加1并重算规范historyDigest，operationId、owner、计划/ACL/位置等全部相同；这不是修改主机时钟。
2. 替换文件在同HDD的另一个固定私有暂存目录完整写入并fsync，先持久化其目录/inode/字节SHA证明，再用RENAME_NOREPLACE发布到原规范captureId名称，禁止覆盖未知对象。发布前再次核实原件确在准确隔离目录。
3. 正常、异常或QA进程强杀后的新进程恢复，均先核实原件/替换文件/目录及旧清单。仅撤回准确的QA替换inode，再按原规则还原原件；临时替换字节先归档至私有证据并核实，才删除这一个新建临时文件及空暂存目录。
4. 外来规范文件、替换文件被改、目录被换、额外文件、旧捕获变化或归档冲突时拒绝覆盖/删除，原件与未知对象保留。清理中断后依据持久证明继续。日志未完整落盘的早期暂存目录不自动清理。这不是产品删除/长期清理能力验收，不删除任何原捕获历史或Root记录。

## 真实身份、API与页面事实

新无网络/非root/只读根容器及独占目录、新临时管理员，正常Authentik→Vue完成一次普通access ACL修改产生新来源；完整2097159字节、身份/两ACL/普通属性、SQL/Root Completed/HDD核实后，页面真实选源、恢复预检Ready。普通捕获确实修改了测试目标ACL，后续“保持”均相对该捕获后基线，不称整场零修改。

Ready时核实只有本案活跃，再替换准确新元数据。旧摘要读取实际返回**409 / AGENT_VERSION_CONFLICT**；按新摘要读取实际返回**200**，完整快照仅时间+1和摘要不同，证明记录通过安装态Reader的完整字段、请求/计划、存储和自身digest校验。该场不是用损坏内容或不存在路径制造拒绝，也不从新摘要可读推断新记录已被业务采用。

原登录同key GET仍200，返回原Ready意图与原来源；原Vue确认一次返回**409 / OPERATION_CONFLICT**。完整SQL与原Ready快照完全相同：原ID/版本/期限/源JSON及哈希不变，执行Job/ID、RootStarted、回执和当前恢复HDD前像均无。目标实际完整字节、九字段、两ACL及普通属性未变，普通捕获原Root字节也保持；不以一个null推断执行结果。

原320×600暗色页面完整显示无法核实/查询原任务/未自动重试确认，确认按钮禁用，无水平溢出；`source-replaced-confirm-rejected-mobile-dark.png`已人工复核。精确还原后按原摘要读取200、快照恢复原值；页面仅查询原任务，先“继续等待”验证取消0，再明确取消一次。原任务Canceled但仍无执行，来源/原计划等冻结字段保持，`source-replaced-original-canceled-mobile-dark.png`已人工复核。来源恢复后没有第二次确认。

请求事件恰为恢复登记1→确认1→取消1，内容PUT0；恢复审计恰3条（原主体plan-accepted、plan-result Ready、cancel-requested），没有confirmed/执行result。原上下文正常用户菜单BFF/Authentik双端退出；另一个新普通身份正常HTTP登录对原恢复和原摘要历史读取两路403、匿名两路401，普通身份正常双端退出，不冒称新增普通身份Vue拒绝页面。

## 原件、核验与剩余范围

- 旧141份HDD元数据全部字节及九字段保持；规范目录总142，仅新增本轮普通捕获1份，没有恢复执行前像。原捕获Root/HDD永久保留；临时替换在证据归档后准确清理，不计为新历史。
- 新原件字节、device/inode、mode、uid/gid、link count、size及mtime保持，ctime从`1793739317668413016`变为`1793739344596880116`；目录时间随移动变化，不称本轮新原件九字段全不变。QA源与HDD同device，不算异盘恢复或整机NVMe/HDD布局验收。
- 修正后的独立核验重新读取实际SQL、Root/HDD、归档替换字节和原件，核实替换仅时间/摘要变化、原Ready不变、原件还原、审计/正常退出/2PNG、准确清理。完整源字节由父门禁在清理前实际验证；独立核验不声称读取已经删除的源字节。
- 本轮源/容器及临时身份已准确清理，旧失败源树未动，活动任务/租约/屏障/终端/路径锁归零。六组件28062da、Agent2178169/维护2178164、11核心全部ID/镜像/StartedAt均保持且healthy，7项受保护配置和完整发布账本保持，开发user NOPASSWD保留，产品Agent授权未扩大。

最终16项替换工具、22项原隔离、4项替换快照/生成规则、32项四个缺失边界、28项旧恢复证明、8项范围检查通过，0跳过；严格Python/Node语法通过。早期测试模块导入fixture类会额外收集21项旧测试，已改为模块引用；不把那次36项当成36项新增。前批294060e CI最后查询仍in_progress，新提交另取终态，不冒称全量前后端重跑或CI全绿。

收尾时发现root只读Git status的索引刷新留下root:root/0600索引，普通用户Git操作被拒绝。仅将本仓库`.git/index`恢复为仓库用户1000:1000/0644，修复前后索引字节SHA相等；没有递归chown、重置或回退工作树。新增QA只读Git禁用可选索引锁/刷新的约定和本门禁的`GIT_OPTIONAL_LOCKS=0`，后续root核对也显式设置。文档先前写入保留，补完链接检查后继续提交；该问题不影响已保存的实盘/独立回执，未重放真实操作。

当前只签Ready后这一种有效替换。首次登记/异步预检/首次Apply前替换、其它内容变化、依赖暂不可用/原期限/原三态回执优先、更多跨写入/磁盘组合仍待[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)实机；递归ACL/恢复、删除长期清理、P0-05维护发布连续性及其余59项继续，不计主项或整阶段完成。
