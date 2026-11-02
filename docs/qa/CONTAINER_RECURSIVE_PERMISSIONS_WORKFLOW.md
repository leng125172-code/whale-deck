# 递归普通权限：内部业务编排候选与回执丢失实机修复

> 2026-11-02后续：管理员API/Vue候选2056191及验收器f706c86已接并通过49项接口、832前端Unit及44递归+104关联生产预览分批验证，未部署/未真实业务整链。见[页面候选、失败与边界](CONTAINER_RECURSIVE_PERMISSIONS_WEB_UI.md)。下方未接HTTP/Vue等保留为原批次事实。

> 2026-11-02后续：最终cca8ec9已接EF仓储/迁移、八方共享保护及专用Worker候选，3794 Unit/259 Integration（7默认原生跳过）、双隔离真实PG全套1执行/1通过/0跳过及独立准确清理通过；未部署/未接HTTP/Vue/真实身份业务页面。见[持久化候选、失败和证据边界](CONTAINER_RECURSIVE_PERMISSIONS_PERSISTENCE.md)。下方未接仓储等保留为71b6334原批次事实。

2026-11-02。内部业务候选 `71b6334` **尚未接EF仓储/迁移、共享保护、Worker注册、HTTP/Vue或真实身份业务整链，不算永久任务已上线**。本批先覆盖递归修复6da6d22，再扩展同类保护并实际覆盖最终宿主 `e668dbe091a3470272443114b5864823de575067` / Agent协议1.49；API/Worker/Gateway仍为d1fcca8。59主项继续部分实现，17项移出不算完成，master未交付，不自动回滚dev。

## 内部候选已实现

新增独立Domain意图与Application编排：固定Actor/原LoginBinding/幂等键派生意图ID、独立执行Job ID与Root owner；请求、密封整树计划、执行各自使用不同HMAC域，确认令牌另域、原登录/计划/期限绑定并拒绝重复属性/额外字段/非规范JSON和时间。预检不直接扫描或写权限，执行前必须独立确认原计划。

预检与执行前后均经当前IPrivilegedIdentityDirectory复核Authentik管理员，再校验原BFF登录。执行期间每6秒复核取消/授权/租约，90秒租约的持久实现仍由下一步仓储承担；调用总预算335秒配合现有330秒SDK，不继承旧单对象65秒上限。身份服务或会话依赖失联只能延后/终止当前RPC，不伪造权限、不当作已取消或已成功。

已开始标记存在后只Inspect/Discard原execution ID，不再次Apply。已知完整完成按当前绑定区分Succeeded/Superseded；Partial保留实际变更/未开始等计数与保护，Unknown保留未证明范围，二者都不自动续做、不自动回滚，也不在此候选中当作普通Failed。

当前其他管理员可以查询待处置范围，但只有明确确认、取得原Root永久Discard fence、再次复核当前授权，才能申请解除保护；Root请求仍使用原Actor/owner/执行ID，确认人与原执行人分开。RequireCompatibleReceipt拒绝让新的fence覆盖已记录的Root状态/计数/outcomeDigest/验证历史摘要，或把historyMayExist从true抹掉。保存的回执与可见计数字段须一致；Succeeded/Partial/Unknown等业务状态须与原回执对应。

BindingCurrent、HistoryAvailable与Replayed是观测字段，允许后续观测变为不可用；不能为了保留旧true而冒称当前HDD可用。historical historyVerified/摘要及实际变更计数不因此消失。SQL快照只表示最近一次回执观测，未来API/Vue必须区分它与实时依赖检查，ACK需另存观测和审计。仓储须在原事务中再次验证兼容性、版本、租约及保护，不能仅依赖Service先检查。

SDK补明确预检拒绝映射：版本/树变化、受保护路径、实例变化、特殊元数据和条目/清单预算等有限错误可及时结束预检；一般传输或未知错误不冒充安全预检拒绝。没有新增公开API或占位按钮，没有把测试Repository注册为生产实现。

## Root回执丢失缺陷及修复

复核发现旧递归Discard在聚合Root JSON不存在时直接写Aborted，未检查HDD聚合/子项或本地子项状态。若文件已改而聚合回执丢失，这会误报零变更。修复在6da6d22：只有实际可用、已attest的HDD下命名空间/操作对象明确不存在，才可证明没有历史；config/挂载/udev/HDD读取错误直接失败关闭，不把FileNotFound依赖故障当作历史不存在。

Apply与Discard都检查固定HDD namespace的`operation.json`和`operation/`子历史目录，以及本地`operation.entries`。发现任一遗留就创建永久Unknown（全项待证明、historyMayExist=true），不重新chmod；既有完成/未知回执保持原只观察行为。没有恢复旧模式、自动恢复旧回执或回滚dev。

4项新Linux-root回归覆盖：只有聚合JSON丢失、聚合JSON和HDD聚合丢失但本地子项仍在、聚合及全部本地子状态丢失但HDD子历史仍在、HDD配置不可用时不能假定不存在。使用真实临时inode/实际chmod和精确元数据操作，HDD attestation明确为隔离夹具，不冒称这些组合都已在安装态实测。

同类检查确认单对象chmod/chown继承的Discard也在Root回执缺失时直接Aborted。e668dbe为两个History类增加同样的实际HDD可用性/固定私有路径检查，在无Root的Apply和Discard均先封存遗留历史为Unknown；不可用HDD不得创建Aborted。递归EntryHistory显式使用本批`namespace/batch/entries`路径，不能误查普通权限namespace。每类新增3个Linux-root用例：丢失回执后Discard、直接迟到Apply、HDD不可用；包含实际inode与内容和旧历史不变，未模拟为安装态丢失测试。

## 宿主覆盖与真实故障门禁

通过既有固定SDK/git archive宿主流程正常覆盖6da6d22，Agent与MaintenanceHost实际新/proc/PID/exe来源匹配、运行库10.0.13、UDS/权限/HDD/动态入口validate通过。更新unit whaledeck-recursive-host-6da6d22，InvocationID 52f9d205daf34bad8a1da6242d4dd67a；日志正常完成/原journal成功结束，未编造unit清理后空时间戳的耗时。安装按流程重启两宿主；验收期间两PID不变。11核心容器ID/镜像/状态/StartedAt跨发布和验收不变且最后healthy。

新宿主上的常规四RPC回归4操作（3完成1执行前取消）、8预检/3执行绑定拒绝、3聚合/12子项实际HDD和每文件2097159字节/原inode/属主/mtime/属性/重放不变，以及独立只读核验均首次通过；不是复用95c0787的旧回执或重放旧实际操作。

另外用冻结83c1294验收脚本、新自有无网络/只读根/非root容器和实际HDD，先通过真实签名RPC完成一次整树修改（4完成/2实际变更），完整核对源与HDD。随后持有准确原operation.lock，核对UUID/原Root完整计划/owner/同一文件元数据和原件SHA，先将原Completed JSON写入root私有证据并fsync文件/证据目录，再只移除该自有在线聚合条目、fsync状态目录；子项和全部HDD历史不动。不是测试钩子、模拟状态、其他任务的回执或旧版本回滚。

真实Discard返回Unknown/4项待证明，迟到Apply仍Unknown/replayed；原实际树的全部九字段、两个完整文件和用户属性不再变化，新Unknown回执逐字节不变，原HDD聚合/4子项历史完整保留。原Completed JSON、原件SHA与实际完成事实保存在私有证据，新在线Root保持Unknown，**没有把原件塞回去或自动恢复权限**。证明后只清理准确自有源/CID，保留这份合成协议Unknown和全部历史；没有新增SQL业务Unknown或清除业务屏障来通过测试。

独立第二进程从当前安装Root/HDD重读，核对原Completed备份与新Unknown、计划/历史/子项及物理指纹时间线、HDD原件和准确资源不存在；源删除后不声称再读物理字节。它只写验收报告，不写权限/历史。最终所有预检任务/终端/锁/屏障及未结束旧意图为0，11核心healthy，两宿主PID在RPC期间稳定，开发user NOPASSWD:ALL的visudo/sudo -n仍通过且未撤销。

最终e668dbe按相同冻结流程再次正常覆盖，unit whaledeck-recursive-host-e668dbe、InvocationID ea9d56e975dd4d44b9798a06fe9e6bf8。新源的递归4操作、旧chmod6操作、chown6操作共16个真实原生签名操作及各自独立Root/HDD/准确清理通过；不是把6da6d22的结果当新源通过。三类实际回执合计13完成、3执行前中止，递归3聚合/12子项HDD、chmod5份和chown5份HDD均有原件；原普通权限/所有者公开业务协议未放宽。安装态单对象回执丢失组合仍待，不以兼容回归冒充故障验收。

最终源还重复了上述准确自有聚合回执丢失实机：保留原件后移除在线副本，Discard/迟到Apply均Unknown且全树/字节/属性/HDD不变，独立核验通过。两次均为不同自有操作，均保留合成协议Unknown及原Completed，不触及其他任务、不删除历史、不回退宿主版本。最终宿主PIDs在这轮三类RPC及故障验收期间保持，11核心仍未重建，业务层未注册。

## 检查、失败与证据

- 3792项Unit全部通过，其中递归C#专项共207项（既有93、本批新增114），包含独立业务HMAC/确认、原登录、实际计数保留、跨管理员处置、失联/撤权/取消/租约丢失的协作取消、Root回执丢失只观察、不续Apply、事实矛盾拒绝与当前可用性变动；Repository/Gateway/身份明确为内存夹具，不是SQL/实盘/真实人类证据。
- Integration258项通过、7项默认原生依赖跳过；没有新DbSet/EF schema或迁移，没有在运行数据库调用EnsureCreated，不冒称新增业务真实PG已验证。Release全方案0警告/0错误。
- 递归Python37项/扩展后全部容器文件405项Linux-root全执行通过、0跳过；Python -W error、原安装与原生签名门禁、范围/链接/diff检查通过。物理HDD与实际回执丢失使用真实依赖，和合成HDD单测分开。
- 首轮新203专项为202通过1失败：夹具未保存已返回Unknown，后续Discard错误返回Aborted，暴露了ACK可接受矛盾fence的问题。补原终态夹具、生产兼容性拒绝与Root丢失检测，新增矛盾/可用性/历史不倒退回归后全量通过；不把首轮93旧RPC测试误报为新业务专项通过。前期本地生成脚本定位标记失败发生在测试生成前，未进行产品操作。日志保留，不追改为首轮全过。

私有忽略证据：recursive-host-6da6d22/（before/after/final/publish.log/原unit journal）；terminal-probe.kBUROa/（冻结6da6d22探针/SHA）；container-recursive-permission-agent-6e74fd9d4bab4c6390a49de5db218bca/（正常4操作及独立）；container-recursive-orphan-774d22d67a3e4965a453f6d462ec5691/（baseline/rpc、原Completed/新Unknown、HDD、cut/verified/独立）。均位于deploy/artifacts/，不入Git。日志/tmp/wd-recursive-workflow-{second,third,unit-final,integration-final,build-final,python-all,root-recovery-final,host-validate,rpc-regression,rpc-independent,orphan-real,orphan-independent}.log；第二轮203项失败在second.log，最终3792在unit-final.log。扩展后的405项在/tmp/wd-recursive-workflow-all-orphan-guards.log；e668dbe三类协议和独立记录在wd-recursive-workflow-all-{recursive,permission,ownership}{,-independent}.log，宿主校验在all-host-validate.log。

## 下一步：真实持久化门槛，不能跳过

须实现IContainerRecursivePermissionRepository的真实EF仓储与migration/snapshot，两个永久Job/Outbox/审计、90秒租约/锁等待/唯一键、Partial/Unknown不自动Claim/不释放保护、结果与计数/摘要不可变、终态与ACK审计；同事务校验Root兼容性及原三层HMAC。当前Domain对象/Repository接口是为此定义的契约，不等于已持久化。

同一pg_advisory_xact_lock(41307341)下，把递归表与旧七类写入方（host file_path_locks、容器上传、目录、复制、移动、普通权限、所有者）接成八方双向数据库屏障；所有旧仓储的path冲突和全局64/每Actor4预算也须反向纳入递归未完成意图。迁移保留旧触发器/旧Down保护，增加永久意图/不可变请求-计划-执行-已开始/历史计数保护及非空拒绝降级。

同时接专用Worker注册和DI，禁止通用Job重试/通用Worker/Outbox接管，完善任务与通知投影；真实隔离PostgreSQL验证并独立清理后再接管理员API/Vue、当前管理员恢复和真实Authentik→页面→Worker→实际树/HDD整链。完整进行中故障/坏元数据、递归所有者/ACL/可恢复删除/长期清理及其余59项/UI/C#酒馆/统一供应商/AI Live2D仍待。

最终源追加私有证据：deploy/artifacts/recursive-host-e668dbe/、terminal-probe.n8wzm0/；container-recursive-permission-agent-87e84494377845a399fba3af95042b87/、container-permission-agent-eaef352785144199bd8ca9ea8f78748e/、container-ownership-agent-d331b734ee0e47babb677cb8f3622f86/；最终源回执丢失证据为container-recursive-orphan-400ad424a3124f65b5e6babfd4c8cbe6/，日志/tmp/wd-recursive-workflow-final-orphan-{real,independent}.log。均不入Git；原批次仍保留。
