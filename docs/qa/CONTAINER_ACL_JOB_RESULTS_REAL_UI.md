# ACL 任务摘要：612f77c 覆盖与真实页面常规整链签收

后续[ACL已写后Agent重启与两原任务摘要](CONTAINER_ACL_POST_AGENT_RESTART.md)已实机通过；运行保持612f77c/1.51，Agent现3835700、维护3747520不变。以下本批原部署/常规整链、观察器失败及当时待办正文保留。

2026-11-03。实际六组件为`612f77c9293f1f3a78b3cb90b84d2a80b0e4ae76`，Agent协议1.51；真实门禁冻结`8c39d09`的21份依赖与11份安装模块，独立观察器修复版为`64f6d8b`。本批签收**九个新资源常规场景、17份新任务安全摘要、18个真实任务详情及同上下文正常退出**。原始真实写入只执行一遍；独立观察器首轮失败及修复后的只读复核分开记录。59主项保持部分实现，master未交付。

## 正常覆盖与运行身份

部署前全局Job、终端、主机锁及各文件写入屏障/活动/租约均0，11核心健康。运行checkout`/data/GitRepos/whale-deck`由所有者user从干净dev/633a2ca快进至固定612f77c，root没有写开发者Git索引，未更改私密配置。固定维护helper字节核对一致，仅一次标准update，无自动回滚。

`whaledeck-dev-publish-612f77c.service`唯一Invocation`689e10e7f8ba479ba2b0e97de386f33c`，07:50:44–07:55:31 CST，约287秒。终态active/exited、MainPID0、Resultsuccess、ExecMainStatus0；保存回执与完整journal后，只停止已结束单元，随后inactive/dead、MainPID0、Resultsuccess。没有因为模型中断或依赖构建耗时重新启动发布。

API/Worker/Gateway/Migrator与Agent/MaintenanceHost实际CLI/镜像revision和HTTP来源均为612f77c。Agent PID3288068→3747527、维护3122598→3747520，实际exe与映射DLL的device/inode均匹配新安装文件。8个数据库平台容器ID/镜像/StartedAt完全不变；最后11核心healthy，两宿主active。标准migrator实际报告无待应用迁移，本批不增加schema或协议。user NOPASSWD文件root:root/0440、SHA及visudo通过并保留，密码未改；产品Agent授权没有扩大，P0-05旧版本来源缺陷仍单独待办。

发布原件位于root私有`deploy/artifacts/container-acl-job-deploy-612f77c/`，包含前后基线、唯一单元回执、六组件/HTTP/实际进程映射及完整journal。

## 新资源与真实物理事实

使用一套新独占、非root1000:1000、无网络、只读根与唯一自有HDD bind的容器；只变更准确自有路径，保留原original.bin。两个目标文件各2097159字节，固定原mtime、混合属主2100:3100及`user.whaledeck-acl`。按原版本/父对象/实例生成独立计划，正常可见Authentik登录→真实Vue→API/Worker→签名Agent→原inode/ACL与HDD，未注入Cookie、SQL、Root、时钟或缓存。

九场景为access、相同ACL noop、mask调整、named UID0且权限0、去掉扩展条目回到基础ACL、default继承、default-noop、明确清空default及预检完成后取消。8场景Succeeded/Completed、1意图Canceled且未创建执行Job；原计划Job已经成功预检，继续保留Ready摘要，不能将其Succeeded解释成ACL已执行，也不把它改成Canceled来伪造历史。

逐场景在父进程清理前读取完整字节、九字段、两类ACL与普通xattr；除目标ACL及对应mode/ctime外原字节、inode、UID/GID、mtime、另一类ACL和既有子项保持。NoChange两案完整现场不变。8份永久ContainerAclMetadata原件的原计划/操作ID/owner/历史摘要、SQL第一回执/MAC、Root实际完成版本和当前源指纹逐项绑定。元数据保留不是内容备份；本批源与HDD同设备，不计NVMe覆盖或异盘灾备。

实际9意图/17唯一Job/35审计：plan-accepted9、plan-result9、confirmed8、result8、cancel-requested1。页面登记9/正常HTTP重复登记9、确认8/正常HTTP重复确认8，仍各自对应原ID；内容PUT0。任务中心阶段只读，无管理写请求或新任务。匿名7个GET为401，普通身份11项读/写为403，覆盖原ACL及管理员Job列表/计划/执行读取；CSRF缺失400与错误Origin403、缺失权限字段400保持。

## 17份摘要与18个真实任务详情

9份planning/Ready与8份execution/Succeeded摘要，均从实际管理员Job API读取并与同一SQL行逐字段比较。严格15字段、真实原意图/任务ID、阶段、ACL类型、目标对象/mode、NoChange、三态AclConfirmed、原回执和历史验证/版本存在性一致。摘要不含路径、实例、主体/登录绑定、UID/GID/完整条目或计划/回执/历史证明材料；SQL/Root/HDD完整事实另行核验，未用UI JSON替代物理检查。

同一原登录上下文在九案完成后依次打开17个原Job的真实“详情”，按列表原ID定位并核对详情ID、状态标签、所有安全字段、预检与执行文案、NoChange和null/false区别、无取消/通用重试按钮；320×600暗色与1440×600亮色覆盖，标题真实入视口且无横向溢出。再只读查看不同原主体的一条旧Succeeded ACL任务，旧resultJson仍null、整条原SQL前后完全相同，真实页面提示未记录安全摘要，没有补造历史或再次执行旧操作。

27张实际PNG包括9个ACL预检/取消页和18个任务详情；四张代表图已目视复核。正文与所有字段另有实际DOM断言，截图不冒充一次性展示全部滚动内容。最后通过真实用户菜单正常双端退出，provider导航观察到、BFF401、Authentik403、最终/login；不是替换上下文、删除Cookie或删除账号来冒充正常退出。

## 独立复核、失败和准确清理

真实父门禁首轮完成，模型中断后读取既有回执，不重新发布或重做任何ACL写入。首轮独立观察器报FileNotFoundError：它向新的`independent-observed`输出目录寻找原`*-jobs.json`输入。保留首轮失败日志后，64f6d8b显式分开原证据输入与派生观察输出，只重跑只读观察器，通过原SQL、Root、attest HDD、17份API摘要与18详情记录/PNG及旧Job不变检查，未改原件或放宽断言。

独立进程确认8HDD永久保留，准确自有容器/源目录与临时身份均不存在、11核心不变、无活动任务/终端/锁/屏障/租约。被删除的源字节由父进程每案在清理前核对，独立进程不声称事后重新读取已删除源。所有真实原件位于root:root/0700的`deploy/artifacts/container-acl-browser-237e07b5233e4bed994fed8a405efc14/`，JSON/日志/PNG0600，保留来源、原失败/修复日志与SHA目录；四张查看用副本位于忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/installed-612f77c/`。

本批11个门禁边界＋8范围=19通过，Python -W error/Node语法和diff检查通过。候选阶段4285后端单元、432集成（7默认原生跳过）、992前端、双隔离真实PG及8测试接口浏览器的既有证据见[候选记录](CONTAINER_ACL_JOB_RESULTS.md)，本轮没有声称这些全量测试重新执行。

CI运行41547897689已核对612f77c：backend/installer/native-postgres/native-valkey成功，前端依赖/单元/构建及ACL摘要/SSE步骤成功，记录时其余前端关联页面仍运行；不宣称整条CI全绿。CLI两次查询遇到TLS超时/EOF，改用连接的GitHub工具读取真实回执，没有重跑CI。

## 剩余范围

本批只签常规摘要、预检后取消与旧空态兼容。Unknown有/无回执、Superseded、Failed/Aborted、明确处置和绑定不可信的摘要在候选单元/隔离PG有边界证据，但本批九个常规场景不把这些分支计为真实页面签收。继续写后Agent重启、写后/实例Unknown、其他共享写入与撤权组合及相应新摘要；ACL递归/历史恢复、可恢复删除/长期清理、P0-05和其他59项/UI/C#酒馆/供应商/AI Live2D仍待。旧[真实撤权事实](CONTAINER_ACL_POST_REVOCATION.md)保留，不重放或覆盖。

文档收尾：目标第1节与612f77c逐字一致，候选QA原正文逐字保留；8份变动MD的740条相对链接无缺失。项目图谱事件`whale-deck.container-acl.612f77c.job-results-real-ui`已核对projectId/key/eventId成功回执，由pending移至synced，阶段仍active。
