# BFF 登录存活与注销续期竞态

2026-10-26，单工作站59项范围。f5a0029已覆盖部署；六组件/HTTP来源一致、11核心健康，真实双登录/注销/Worker取消已通过。浏览器交互、进行中发布撤销和容器写入完整业务链仍未签收。

## 安装与真实依赖发现

固定维护更新unit已正常结束（Result=success、ExecMainStatus=0），API、Worker、Gateway、Agent、MaintenanceHost和Migrator的实际来源均为f5a00299a111050e09e3cf6cce513881d14f0577。发布前文件活动任务/路径锁/接收发布均为0；迁移器确认无需SQL变更，未自动回滚。

首次真实OIDC回调失败，API抛RedisServerException：会话脚本第1行的TIME被生产Valkey ACL拒绝。原生夹具此前的`+@all -@dangerous`覆盖过宽，未证明生产类别权限；不能据旧原生通过宣称实际登录可用。已仅为专用whaledeck身份补`+time`并ACL SAVE持久化，不改密码、不清缓存、不放开配置/ACL/清库权限。database-platform引导和只读ACL预检同步修正；独立原生夹具及CI改为read/write/connection/scripting加TIME的生产类别组合，补CONFIG和ACL拒绝断言。

修后`validate_bff_login_liveness.py`真实临时管理员双次OIDC登录：同主体第二登录仍为管理员，另一个登录无法读取第一个登录的上传；正常退出原登录后18次旧Cookie请求均401，未显式取消任务。已部署Worker根据原登录撤销将自有AwaitingUpload意图和Job置Canceled/FILES_CANCELED，实际root保留Aborted/canceledBeforeReceive证据，目标从未写入，锁和待处理消息为0且结果审计恰好1条。没有直接改SQL/缓存/权限、伪造终态或删除取消标记；临时身份由外层精确清理。此用例不等同于发布进行中撤销或人类网页验收。

f5a0029冻结原生门禁执行1/通过1/0跳过、要求新构建和清理通过，源码SHA256为a81bdb08291b9f67b22997f954000bd0ea0e3f1fe5001c5290fd492d4546aadf；该次仍使用旧宽夹具，保留事实但不作为生产ACL通过依据。生产ACL组合的新冻结复验仍待执行。

## 实现与边界

- 原ServerSideTicketStore的Renew无条件Set，可在Remove之后重新创建旧引用。改为单个带TTL的Valkey hash：已加密ticket、Subject域哈希、绝对到期时间、revision；API与Worker共用存储，Worker仅通过ILoginSessionRegistry查询Subject+LoginBinding存活，不获取或解密票据，不把Token放入SQL/Outbox/审计。
- 新键域`whaledeck:auth:session:v2:<SHA256(reference)>`，随机原引用仅在HttpOnly加密cookie引用票据和API内存中；不保存第二份用户资料。新建使用随机引用、原子不存在检查；续期只能在同主体的有效现存记录上完成，禁止缩短到期或复活缺失/过期记录。注销原子删除，和续期任意先后顺序均不能复活原登录；其他登录不受影响。
- API解密失败或票据主体/到期元数据不一致时，按读到的revision条件删除，避免旧损坏读取删除并发的新有效续期。票据有效期最长8小时，角色权限仍使用既有最长2小时Authentik复核，二者不是同一概念。类型/长度/TTL/整数/原始Unicode校验失败关闭；Redis脚本先验证完整旧元数据再修改，避免脚本报错前产生部分续期。
- 开发内存实现仅在同一进程内共享，另一个Worker没有本地登录时拒绝。生产OIDC要求Valkey和持久DataProtection目录；现有Compose已提供两项。没有目录查询故障时的本地管理员回退。
- 宿主上传Worker发布前与发布期间每6秒检查原登录存活；存储故障取消/观察唯一在途调用并延后，不签新发布、不冒称取消/成功。已经由Root证明完成的发布仍据实对账，不因后来注销改成失败或重复写入。HTTP接收完成后再校验登录/当前权限，才写Ready。

上线v2后旧v1服务器票据不导入，旧浏览器引用需要重新登录一次；不清空共享Valkey、不修改Authentik用户/组或2小时权限策略。取消标记与任务迁移不属于本缓存变更。

## 验证

最新Release零警告错误；2446 Unit全部执行通过，174 Integration通过，7项原生夹具明确跳过。14项新API/内存生命周期用例覆盖两实例/两种顺序、64轮并发续期注销、其他登录/主体、过期/旧续期、损坏旧读与并发有效续期、错误主体/大小/期限/Unicode和payload复制。宿主上传新增注销/缓存失败前后及已完成事实对账检查；原12项登录/终端专项保持通过。

真实Valkey独立门禁已在工作副本执行1/通过1/0跳过：两个独立连接及两个API ticket store，经私有Unix Socket、无网络/无TCP、非root只读根、单一自有绑定、noeviction测试服务验证加密存储/原生TTL、64轮续期注销、错误主体/旧期限、revision条件删除、无TTL/缺票据/分数及前导零revision/坏到期/错误类型与真实到期不复活。ACL只允许该会话键域且去掉dangerous类别，域外访问明确拒绝。只删除逐个生成的已知自有键，不SCAN/FLUSH；自有容器/ACL及socket目录精确清理。它不是运行API/Worker或人类身份的实机证据。

CI新增固定摘要的native-valkey独立job，必须1执行/1通过/0跳过，不将默认总测试里的skip算作原生通过。验证器要求干净已提交源码，先以checkout所有者进行受限并行新构建，失败不进入no-build测试，记录同一Git revision及源码hash。

一次MSBuild子节点提前退出，未找到其报告的failure文件；原因尚未证实。之后no-build测试使用了旧产物，结果不作为最终证据。改用`-m:2 -nr:false`重建成功，验证器强制成功的新构建后再测试；最终2446/174计数及受限ACL原生门禁为重建后的结果。未降低任何业务测试断言。

## 仍需完成

生产ACL组合的冻结原生复验、完整CI，以及完整Authentik双端退出、跨页/进行中上传撤销、普通用户隔离和真实页面验收。已完成的安装/真实双登录Worker门禁见上文，不冒称完整发布中撤销。随后接容器上传永久实体/迁移、Job/Outbox/租约/宿主与容器共享写屏障、当前权限/审计、BFF上传编辑和历史恢复/Unknown接管。当前没有新容器写HTTP或页面，不签完整文件模块或59项交付。
