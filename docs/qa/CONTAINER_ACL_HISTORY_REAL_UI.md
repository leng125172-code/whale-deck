# ACL历史真实常规整链：08b2bb8部署与严格门禁

2026-11-03。**六组件已正常覆盖为`08b2bb8cf8c743dab4259fd34da27a6e8f6c9855`、协议1.52，真实Authentik→Vue→管理员GET→持久PG审计→Agent/HDD的常规读取主验及独立核验通过。** 最终成功验收工具冻结`17590e81477f9981de6151dc928ce7f7dac08863`，与运行版本分列。两轮前置失败完整保留，不能签整体首轮通过；历史恢复、递归ACL和完整故障矩阵仍未完成，59项及各阶段不提升整项完成率。

## 唯一正常覆盖与实际来源

运行checkout从0478262干净快进到08b2bb8，只启动一次标准`whaledeck-dev-publish-08b2bb8.service`，Invocation`91e75020130045d58a072f1c360d3c50`。宿主/镜像标准冻结构建、前向迁移及三应用容器替换成功；最终active/exited、MainPID0、Resultsuccess、ExecMainStatus0后保存原journal/终态回执，仅停止这个已结束unit，没有重复发布或回滚。

实际API/Worker/Gateway/Migrator/Agent/MaintenanceHost均08b2bb8，镜像label/CLI、宿主/proc exe及映射DLL inode同源，宿主PID为Agent684268/维护684263。可信发布账本current=08b2bb8、previous=0478262、pending=null，不以checkout HEAD推断上一版。11核心healthy，8数据库平台容器ID/镜像/StartedAt自发布前保持；运行env/镜像配置、capability key、文件/HDD根及两类sudo文件SHA/owner/mode保持，user NOPASSWD按授权保留。

标准host validator再次通过服务/权限/UDS/固定helper、实际ACL namespace/FD非IDMAP与HDD身份、三动态维护入口。未做物理冷启动或自动回退；没有覆盖整个发布窗口的连续HTTP采样，不声称零瞬断。构建和前后来源/配置仅hash原件位于私有`deploy/artifacts/acl-history-release-08b2bb8/`。

## 两轮失败、只读复核与准确清理

| 原始门禁 | 实际发生与失败原因 | 保留及处理 |
| --- | --- | --- |
| `container-acl-browser-ddb655a0be24438199c41a8d6a8be913`，工具fc381fd | 新独占9 ACL操作/8 HDD、第一份真实历史快照和2条审计已发生；Python把实际jsonb对象当字符串json.loads，TypeError | 原Failed保留；修工具后只读核对第一份快照/2审计、9原操作SQL和8 Root/HDD、全部源字节/身份，才准确删除原测试源；Root/HDD留存 |
| `container-acl-browser-e79af2bd754c420a8aebb3038768bab3`，工具43cb095 | 新独占9操作、2页/8历史快照/10审计已逐轮证明；17新任务详情通过后，旧空摘要任务定位失败 | 实际AppShell并行take=50、JobsView为take=100；监听器误取50条响应。SQL表明旧任务仍在100条表格范围内。保留Failed和17任务/8快照分项，独立核对全部源/Root/HDD及10审计后准确清理；原件不删 |

修正JSONB对象/文本的严格统一解析，保留重复字段/类型/0值拒绝；再将任务页监听绑定同源、GET、唯一take=100，新增两种完成顺序及错误响应回归。普通用户退出在实跑前已修正：先验证/forbidden，再在同一普通用户会话回首页打开真实用户菜单，不能等待禁止页不存在的菜单。

没有改产品或为工具修复重新发布，没有对旧CID/意图/执行ID重放写入。严格轮使用第三套全新、准确归属的资源和临时身份；前两轮实际已做操作不累加到严格轮成功数。前两轮因失败未完成正常原上下文退出，不补签退出或完整业务通过。Git推送曾遭TLS握手中断，正常重试，不降低TLS或改凭据。

## 严格轮真实页面、审计与原件

成功目录`deploy/artifacts/container-acl-browser-b25d575290e84ab8badc2005d6577fc7/`。Chrome通过可见Authentik账号密码流程正常登录，没有route拦截、Cookie注入或SQL/cache写入。新容器无网络、只读容器根、1000:1000和固定预算、唯一准确HDD bind。先真实Vue/持久意图/Worker完成9 ACL场景（8成功/1预检后取消），产生8份原ACL HDD；逐轮2097159字节、inode-owner-mtime、两ACL/普通属性及非目标子项核对，原key/确认重试不重复生成执行。

| 历史读取实际分项 | 严格结果 |
| --- | --- |
| 准确当前CID/启动实例/文件与目录路径 | 2页真实List，5文件+3目录捕获集合准确、完整且不重复；绑定库存version/cursor |
| Vue选择原捕获后Read | 8份原access/default ACL，捕获/摘要/创建时间/原实例/类型/模式/条目数量及完整ACL与原HDD逐字段、逐类型相等；含named UID0/permissions0、空default、mask与NoChange原件 |
| 真实持久PG审计 | 10条（2 list+8 read），原主体/资源/唯一Trace及路径/实例hash、库存version或历史digest准确；无ACL正文/原plan/byte-size/hash，job_id为null |
| 每轮放行前父进程证明 | 全源完整字节/九字段/ACL保持，全部9原操作SQL及8 Root/HDD原字节SHA不变；每份页面响应由私有握手证明后才继续 |
| 管理员实际UI | 320×600暗色文件及1440亮色目录，实际原ACL/摘要、长值换行、正文滚动和完整关闭按钮；历史段10请求全部GET、写请求0 |
| 普通用户 | 同一普通用户正常可见登录，两个历史GET均403，/containers实际/forbidden、UI历史请求0，随后回首页正常菜单退出 |
| 原上下文退出与匿名 | 管理员及普通用户分别原浏览器上下文正常POST退出/到达provider/end-session/BFF401/identity401或403/最终login；普通原上下文退出后两历史GET均401 |

读取后源、Root/HDD保持，不把原ACL当作当前权限。真实9写入场景是创建本轮历史的前置，不等于历史恢复；旧任务回归另外完成17新安全摘要/18真实任务抽屉及旧空摘要不变，不能将其算为历史读取请求。`history-verified.json`中ordinaryDenied/anonymousDenied只对应两历史GET，不与既有ACL权限矩阵累计。

全部数据证明后逐文件unlink/空目录rmdir，精确清理源与准确标记容器，保留SQL审计、Root及HDD历史；临时身份准确删除。源与HDD同device，**不计NVMe到异盘灾备**。36张真实PNG（9 ACL、8历史、18任务、1普通用户）为本轮产品页面，不能与前批89个测试接口预览场景混记。

## 独立复核、自动检查与剩余

另一个只读进程重新验证实际SQL、原Root/attested HDD、25个冻结工具文件来源、页面响应/10审计、捕获分页及正常两上下文退出记录；精确源/CID已不存在、所有临时QA身份不存在、11核心保持且healthy，独立Passed。不声称重读已经删除的源字节；现场全字节由父进程每轮证明。

本轮19项旧/新离线证明测试、2项50/100请求竞争回归、8项范围及Python/Node语法通过。没有新产品源码变更或schema迁移；前批4343 Unit/463 Integration（7原生跳过）、1042前端Unit/89预览保留原来源，不冒充本轮重跑。实际PG在此轮验证了持久读取审计与旧ACL事务，无额外合成数据库替代。

尚未完成查询进行中取消/实时撤权/账号停用/超时/Agent重启/HDD失联、更多实例/路径/库存变化组合；库存扫描统计仍全局，不签多租户隔离。下一步将所选真实capture/digest持久绑定到**新的**恢复计划/确认/意图/Worker及审计，先保存当前ACL原件，不复用旧执行ID或将普通编辑冒充历史恢复；继续ACL递归、可恢复删除/长期清理、维护安装态故障和其余59项/UI/C#酒馆/统一供应商/AI Live2D。开发sudo保留、不自动回滚或重启主机，master未交付。

收尾记录：4个root私有目录共437份静态原件SHA校验通过，目录0700/文件root:root0600；成功和两次失败原始正文均保留。7份变动MD的813条相对链接有效，目标第1节与e83候选原正文逐字保持，范围8项及diff通过。项目仍active，不提升59项整项完成率。
