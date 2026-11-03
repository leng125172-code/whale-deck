# 容器 ACL 任务安全摘要候选与验收边界

后续612f77c已正常覆盖，并取得[九场景/任务安全摘要真实页面签收](CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)；下文候选阶段、当时未发布和未验收范围原文保留。

2026-11-03。后端候选提交2491add，页面按钮/证据路径补充9b419c2；运行仍633a2ca/Agent1.51。本批未发布、没有生产schema或Agent协议变化，不改原Root/HDD或历史任务来补造缺失结果。59主项仍部分实现，未交付master。

## 实现与安全边界

原问题已由上批真实页面发现：ACL任务终态Succeeded却无resultJson，并显示“任务成功后显示安全结果。”。新仓储在原事务内同步保存任务/事件与15字段摘要，覆盖Ready预检、成功/实例变化完成、取消/失败、Unknown有/无回执与管理员明确处置。预检Job保留当时Ready快照，后续执行有独立Job；旧任务无摘要时明确提示未记录，不迁移补造事实。

字段仅schemaVersion、intentId、stage、state、code、aclType、kind、targetMode、noChange、aclConfirmed、receiptState、bindingCurrent、historyMayExist、historyVerified、hasPublishedVersion。没有路径、容器实例、UID/GID或完整ACL条目、主体/登录绑定、计划/请求/回执MAC、指纹/历史摘要、授权票据或凭据。写入前验证现有完整MAC绑定；不可信的Unknown分支留空，不让摘要生成妨碍保护状态保存。当前管理员任务API保留现有实时权限验证，不扩大原用户读取或执行权限。

API严格校验4096字符预算/深度4/版本/完整字段集合及拒绝重复或未知字段，核对planning原意图ID或execution派生ID与当前Job ID、阶段和任务状态；仅ACL计划/执行允许返回受限的非成功结果，其他任务原有成功才返回结果的规则不变。错误或旧摘要不原样透传。Vue再次按字段白名单和类型/范围读取，文本插值显示，拒绝未知/错误JSON，不使用v-html。

页面分别说明“预检完成，尚未修改ACL”“结果未知，仍保留写入保护”“管理员已明确处置未知结果”；处置不表示ACL已恢复或重新执行。null为未证明，false为否，0000/不变更false不丢失；绑定有效仅指回执对象，不能推定原人类权限仍有效。旧Succeeded/Failed/Canceled/RolledBack无结果显示“任务已结束，未记录可展示的安全结果。”，进行中显示尚未结束。终态SSE后原任务即使离开最近100条，也只读原ID更新详情；关闭或切换任务后丢弃迟到响应。

## 本地回归与真实依赖

- 全量后端单元4285通过（最终摘要ID绑定修复后重跑），最终前端单元992通过；专项43后端/原25前端已通过，后续加2个ACL入口否定用例。
- 集成432通过、7默认原生跳过；其中54项ACL/模型/API集成通过。此处不把默认跳过记作真实依赖通过。
- Release构建0警告0错误，Vue类型/生产构建、lint、格式及diff检查通过。
- 4组320×600/1440×600×明暗生产预览中逐组查看成功、Unknown、Acknowledged、Ready、旧空结果和未知字段结果，原ID/三态/0000/处置边界与零写请求/无横向溢出通过；另4组实际本地HTTP/EventSource续传关联通过。预览用测试身份和接口，没有真实Authentik、Agent、生产PostgreSQL或磁盘写入，不能当作真人业务整链。
- 双隔离真实PostgreSQL冻结2491add实测1执行/1通过/0跳过，完整源含新的Job摘要断言：Ready快照不被后续执行改写，成功/Unknown有回执/Unknown无回执/明确处置/取消的受限摘要在第二数据库连接和任务查询投影读取，原回执/MAC保持、敏感字段排除。原生状态dispatch-278bf800dd784b58b038e7c81f3a1767为Passed且cleanupConfirmed=true，临时数据库及SDK容器准确清理，真实外部投递0。其中Root回执为构造数据，不能代替实际ACL/文件/HDD证据。

截图复核发现旧Unknown通用取消/已处置通用重试入口不适用，9b419c2已隐藏Unknown取消（含旧无摘要AwaitingContainerAclReceipt）及所有ACL通用重试，并引导到当前对象新预检/文件ACL恢复入口；后端既有禁止通用ACL重试规则保持。最终4+4组同批8浏览器回归全部通过，零管理写请求。CI新增同一任务摘要浏览器与SSE关联门禁。本轮未声明远端CI全绿。

## 失败与修复保留

首轮新增后端测试未设置OperationJob必填ActorSubject/IdempotencyKey、误改init-only类型以及重复JsonSerializerOptions被分析器拒绝，均已修复再通过；产品编译未放宽分析规则。前端Object.hasOwn超出现有lib配置，改用兼容的hasOwnProperty.call，未扩大目标lib。只读复核发现摘要与Job ID关联缺失，已补原意图/派生执行ID校验和否定测试。

预览首轮默认Chromium缺失，未下载或安装新依赖，改用已有/usr/bin/google-chrome；随后缺少生产预览auth/me测试接口、关闭按钮名称与现有英文aria-label不符的脚本尝试已停止并保留日志，修复后4组通过。两次写测试文件误加web前缀因工作目录已在web下而未落盘，随后用准确路径保存，未发生产品操作。关联SSE测试会清空test-results，摘要截图路径已改至独立ui-candidates目录，重新只读运行保存图像，不计作额外真实业务验收。

日志：/tmp/wd-acl-summary-unit.log（首轮编译失败）、unit-final/all-unit-final、integration/all-integration、web-unit/all-web-unit、web-build、web-lint-final、web-format、release-final、browser/browser-chrome/browser-auth/browser-final/browser-artifacts/browser-events、native，均以wd-acl-summary-为前缀并以.log结尾。预览截图保存deploy/artifacts/ui-candidates/acl-job-summary/；只含测试接口内容。旧实机证据见[写后实时撤权](CONTAINER_ACL_POST_REVOCATION.md)，不重复原变更。

## 尚未签收

需正常覆盖候选并以新独占资源验证真实Authentik→Vue→管理员任务API→永久Job/Worker→Agent→实际ACL/文件/HDD摘要与终态一致，包含旧任务空态兼容、撤权后拒绝和不同当前管理员读取。此候选不代表写后Agent/实例Unknown/共享写入剩余组合、ACL递归/历史恢复、可恢复删除/长期清理、P0-05或其他59项/UI/C#酒馆/供应商/AI Live2D完成。

最终证据：deploy/artifacts/ui-candidates/acl-job-summary/verification.json冻结后端2491add和UI9b419c2、16张PNG的SHA-256及日志。11核心容器实际healthy、Agent3288068与维护3122598均active，未发布候选或重启生产进程。

目标第1节与36bf1af逐字相同，上批写后撤权QA原正文逐字保留，7份本批变动MD的相对链接均可解析，8范围测试与diff检查通过。项目图谱事件`whale-deck.container-acl.2491add.job-results`已核对projectId/key/eventId回执并由pending移至synced，阶段保持active。
