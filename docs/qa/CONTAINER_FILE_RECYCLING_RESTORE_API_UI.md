# 独立回收恢复管理员API与Vue候选

2026-11-06，候选f6cb18db26f1a194b69fc575e06431ddd8c9fe2a。承接[持久化与真实PG](CONTAINER_FILE_RECYCLING_RESTORE_PERSISTENCE.md)，新增管理员API、可恢复来源目录、Vue独立确认/查询/取消与Unknown处置。未部署生产或执行真实身份/文件/HDD恢复整链；Jobs统一投影/摘要适配、发布与故障矩阵仍待。

## 严格输入与服务端来源

新增容器files/recycle-restores的POST、status/by-key GET、confirm/cancel POST，和独立files/container-recycle-restores/recovery清单/原任务GET/明确acknowledge POST。全部Administrator策略、NoStore、限体积，沿用BFF/Cookie/CSRF/Origin全局边界；每次业务动作仍重查当前权威身份及登录。Controller只转换输入和调用Application，不做SQL或恢复文件。

Queue正文仅sourceIntentId规范非零小写D格式GUID，不接受owner/原计划/路径/摘要/执行ID。专用转换器拒绝字段缺失、重复、额外、大小写别名、类型强转和null；确认/处置须明确confirmed、非负int64 version以及确认token。登记Key必须单个规范GUID，后续修改须等于意图ID，读请求不以新键代替原操作。

sources GET以take1–50及可选after规范GUID游标读取至多take+1，按原意图GUID排序，返回有界摘要和下一游标。仓储只读取同容器已完成/Trashed/已验证保留来源，排除活动恢复或已证明完成恢复的来源；服务再校验全部原业务MAC/资格、重复和容器边界，授权前后均重查。响应不含旧owner、完整计划、属性字节或凭据。目录是历史摘要，实际Root/原实例/HDD仍须预检重新核验。

意图响应新增始终存在的SourceHistoryDigest，供客户端绑定所选来源；size/sha/属性数明确为来源提供的非空摘要，规划身份在预检前仍为null。客户端校验新旧意图/执行/来源ID、位置、摘要、原UID/GID/模式、nullable归属、版本/计划/原事实推进，不把Unknown缺席字段转成false。

## Vue与生命周期

容器文件区增加“恢复已回收文件”，选择原回收来源、翻页并单独生成预检；Ready展示原来源/HDD摘要、新规划与执行引用、警告和期限，原实例变化或计划过期不能确认。确认和取消分开；取消弹窗关闭不发请求，Unknown只查询/转管理员处置，不提供重复写入按钮。文件列表及既有所有writer在恢复对话框/未决任务期间共享本页锁。

浏览器sessionStorage只保存subject+container+原sourceIntentId+原登记key/id，最多4项，不保存确认token、完整计划或属性。丢回执后只查询by-key或显式重试同一登记，不自动换键；会话结束/账号或实例切换/unmount/隐藏页通过generation和AbortController丢弃晚到响应并停计时，恢复可见时只查询原引用。轮询在Unknown/终态停止，Ready只更新到期状态。此行为不替代服务端实时授权。

Files页面新增独立恢复异常处置，先选择原Unknown历史，再明确确认并核验原处置回执；“暂不处置”不发写入。归属保持null，解除业务保护不报告恢复成功，不重做或删除HDD历史。原回收的异常面板保持独立。

## 检查、布局修正与失败保留

35项新增HTTP输入/元数据/直接Controller边界、7项来源目录业务检查、23项新增TS契约/提示检查通过。最终5437 Unit零失败零跳过，741 Integration通过/7原生依赖默认跳过（748总）；前端67文件/1351单元、类型检查/lint/构建及8范围通过。HTTP检查不是实际Cookie/网络请求链，身份/仓储/网关等按各测试明确为替身。

构建产物本机5175预览以合成API协议夹具运行10个实际Chromium交互：320/1440宽且600高、明暗四组合、登记丢回执重载原Key查询、取消弹窗关闭/明确取消、Unknown归属与禁止重做、会话结束后晚到GET丢弃、实例变化禁确认、独立管理员处置保持null。最终10/10通过；四张实际页面PNG及检查日志保留。预览不等于生产或真实Authentik/Agent/PG/文件/HDD整链。

修正记录：默认Playwright headless路径缺失使7例未启动，改用已有/opt/google/chrome/chrome，未安装新浏览器；初始合成来源路径段364字节被255字节检查正确拒绝，夹具改成220字节；桌面600高时默认15vh顶部留白使footer出屏，改为所有宽度top12px并保留正文滚动，未放宽完整viewport检查；Ready取消假服务未转终态，改为Canceled且无执行/回执。一次修正脚本在web目录误用仓库相对路径，未应用即停止，已从根目录修正并停止仅该过时测试进程后重建复验。早期TS Array.at目标兼容及lint断言消息问题已修正，不改编译目标或关闭检查。初稿环境/路径/布局失败及中断截图按原件保留。

## 真实PG目录检查与生产保持

标准冻结Git archive原生dispatch宏另行在两份独立临时PG执行，真实来源目录查询、GUID游标翻译及活动恢复排除已运行；原有持久化/共享保护回归一并通过。unit whaledeck-qa-recycle-restore-catalog-e216810c，Invocationc67d6525b9044008a4b43682f61b38ea，准确exited/Result=success/ExecMainStatus=0；原生1执行1通过零跳过，两数据库和SDK容器清理确认，外部投递0。原生身份/Root回执仍是夹具，不转签真实页面整链。

两次独立生产只读快照逐值等于上一已签基线，2346保护、11核心healthy、回收Root32/HDD35、独立恢复Root5、目录Root96及既有数据库保持。两宿主f333a69a/Agent1.57，四容器和运行checkout仍58b8174；没有生产迁移、新RPC、身份变更或覆盖发布。

证据deploy/artifacts/container-recycle-restore-api-ui-e216810cd8be478c84093fd5eef70d6d，共70文件/23变更源码，EVIDENCE_SHA256.json摘要dec9e29bcb7f627d9a2ac64557f1c2648685864b155bf75fd2c05868493276f1，所有文件逐一重算通过。候选源码固定Git身份，包含预览、原失败、最终完整检查和实际保持回执。

继续Jobs安全投影与结果展示、准确CI、标准部署/生产迁移及真实身份/目录来源/原inode/HDD恢复整链，再完整故障矩阵、长期清理及其余59项。当前完整主项0/59、阶段0/6、25%–35%工作量粗估及AI子计划0%保持；17项移出不算完成。

项目图谱whale-deck/container-recycle已按本批稳定eventId回执同步active，pending移入synced；未标阶段或整项完成。
