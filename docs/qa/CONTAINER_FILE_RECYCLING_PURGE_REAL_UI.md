# 留存原件清理：常规真实身份/页面/文件/HDD整链

2026-11-06，运行产品保持9694af1c473fe9a62b786532f7bd6a3bd2107e1a，验证源码11bf68fd49d24250abd677f2cd506ad6e3da2775。承接[标准覆盖与生产迁移](CONTAINER_FILE_RECYCLING_PURGE_RELEASE.md)，本批在全新自有容器和临时真实Authentik身份中完成页面回收、清理Ready取消、新意图明确不可逆确认、实际清理和Jobs，以及准确清理测试资源后的HDD再次读取。未重新发布产品或重启Worker/Agent。

## 验证修正与门禁

复核实际内核发现，清理成功会将HDD元数据captureState由Trashed更新为Purged。先前尚未实机运行的retained_history候选要求所有元数据完全相等，会误拒绝合法结果；本批增加显式purged=True，只允许这一个字段按已证明结果前进，禁止倒退，其他历史字段/digest和归档字节/inode/属性/存储身份必须保持。没有修改产品来迁就验证器。

14个纯合同检查和5个门禁范围检查包含于97关联清理Linux root测试，全过零跳过。覆盖计划UInt64/纳秒、来源与执行域、空值与历史规划、合法Purged变化与其他变化拒绝、Ready无执行记录/无Root证据、替换文件/摘要/存储拒绝、旧清理行完整SHA范围，以及临时root-owned命名空间内实际只读FD/完整字节读取。此处临时SQL/namespace/存储attest有显式夹具，不能代替下述工作站实机。

首轮范围单元1失败/5子例错误来自测试源缺receipt占位和临时HDD元数据0644，修正夹具为必需字段及0600后通过，未放宽私有记录权限或产生生产操作；失败说明另封存。另14 CI合同/8范围、Python/Node语法通过。120份源码依赖闭包在创建身份/测试资源前按Git固定并封存，18个已安装helper字节对照产品提交通过。

父门禁分源回收、Ready取消、准备清理、最终确认、成功五阶段。Node通过真实登录/DOM操作产品；没有路由替身、注入会话、直接SQL修改或直接Native Apply/Discard。父进程先重新证明Ready、保留inode、原Root/HDD/计划摘要和旧业务/系统保持，再允许打开最终确认；页面先“暂不清理”，随后在最后一次“确认删除留存原件”前再次由父进程重核相同ID/版本/来源和原件/HDD，才放行。未知或未决结果不会清除现场。

## 全新真实常规链

unit whaledeck-qa-purge-browser-11bf68fd，Invocation 8de7ec8da4ba4a8485e8edc5a289a982，最终exited/Result success/ExecMainCode1/ExecMainStatus0。全新无网络自有容器/目录内1048583字节普通文件，原1000:1000、0640及user属性/完整字节快照已保存；实际Chromium页面正常登录临时Authentik管理员，页面创建唯一回收来源并确认，父进程独立SQL/Root/保留原inode/HDD通过后才进入清理。

320宽、600高下先明色Ready取消：确认取消前先选择继续等待、无取消写入，然后明确请求取消。该清理意图Canceled，没有执行Job、预留/Apply入口、回执或该确定性执行ID的Native json/lock；原源SQL、实际留存文件和HDD保持，规划Job仍保存历史Ready。它不冒充Native预留后的取消验收。

随后在暗色选择同一来源生成新的清理意图，登记正文仅sourceIntentId、两登记键不同，同键显式重复仅返回原ID；重载后按原引用查询，不自动创建新任务。最终不可逆确认包含HDD保留及当前无归档恢复入口的明确提示，暂不清理不发确认；父进程最终核验后仅一次明确确认。结果Succeeded/Root Completed，本操作预留与Apply入口已记录、purgeApplied/sourceFenced/ownedFence为true，保护/租约/待处理消息清空，唯一接受/确认审计成立。

实际SourceRoot推进Purged且actualIdentity为空、源保护marker为Purged、原件及trash/purging名字缺席，Native unlinkConfirmed为true。原回收SQL完整意图及Job保持，不能把它的历史Trashed/留存事实改写成当前文件事实。HDD仅captureState合法推进为Purged；完整metadata/digest、归档inode/字节/属性和存储身份/hash保持。

真实Jobs页精确绑定take=100列表及对应详情，打开取消意图的历史规划Job和成功执行Job，16字段原来源/执行/预留/Apply/三态事实与SQL逐项一致，实际留存清理专属区域可见。共6张真实PNG：两Ready/两结果和两Jobs；抽看320暗色成功页及桌面成功Job，未见横向溢出或错误成功提示。这里是实际产品API/页面，并非构建预览。

## 身份、独立重读与准确清理

两次正常BFF/OIDC双端退出；首次退出后原接口401，同一Subject重新正常登录，旧两ID/两key及原来源共5个原所有者引用均404，再正常退出。真实普通身份5GET/4POST共9拒绝，匿名5GET均401；普通请求包含登记、确认、取消和专用处置，均403。CSRF/Origin错误拒绝、无浏览器文件PUT及页面登记/确认/取消数量核对通过。

独立进程先在测试容器仍存在时重新打开Root/marker、HDD和当前物理命名空间，重新证明原容器实例与原件消失。通过后仅删除本门禁已验证inode的空private目录，交由自有fixture机制清理该新容器/目录；没有删除HDD文件或旧记录。

准确清理后父进程和独立最终进程再次从当前read-only storage.target打开HDD namespace FD、前后attest，并重新读取完整元数据和.data SHA/size/普通属性/xattrs/存储身份，与删除前观测仅允许captureState前进。第三次独立最终重读也通过；这不是只检查historyRetained布尔字段。源/HDD设备号均2049，未签独立物理盘灾备或跨盘组合。

## 保持、封存及剩余范围

旧36回收/22独立恢复及原清理行的所有列to_jsonb文本SHA逐项保持，保护文件/11核心/已运行9694af1c均保持，无覆盖/产品进程信号或旧任务重放。最终总37回收/22独立恢复/2清理（1 Ready取消、1成功），活动0；Root/HDD78/85、恢复Root59、目录Root98、清理Root6，只增加本门禁准确来源与成功执行及HDD记录。临时身份上下文已收尾，自有源容器/目录缺席。

私有实机证据deploy/artifacts/container-recycle-purge-browser-492a58ba03ff40a7b5f61f961d8f3f87，166文件/120源码/6真实PNG，EVIDENCE_SHA256.json摘要ce584827a33f38c711572bdb1605408958e5e39e78391e27c9e3b5c1b66d77ef。准备期9文件/6源码另封存于container-recycle-purge-gate-candidate-9dd5f18eeefe4866aa0985fb7bd0bd66，摘要94a0efdb037dba0f5e48d57cd98275a44ccd9202f6a42d382ff20008c9c8193f。原件权限不放宽；仅将两张复核PNG逐字复制到另一个本任务私有可读目录供查看，原件和SHA保持。

只签常规链、Ready取消、正常退出与权限边界、最终按钮前双重核验、清理后归档重读；进行中取消/退出/撤权/强杀与自然租约、Unknown/跨管理员处置、来源变化、跨容器写入等完整故障矩阵尚未完成。继续留存策略、归档恢复、维护连续性及其余59项；17项移出不算完成，正式整项0/59、阶段0/6、工作量粗估25%–35%及酒馆/Live2D功能0%保持。

收尾普通用户读取root私有current指针被权限拒绝，仅改为sudo更新该进度指针；未放宽权限、改变原证据所有者或重跑实机动作。
