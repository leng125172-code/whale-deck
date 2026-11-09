# 自然老化 QA 来源准备与独立观察

2026-11-09。准备代码 `c4e14905`，独立观察器 Git 修正 `a0dbf8141e64468cec9966e36855917da489526d`。本批只建立自然等待条件，不是自然到期或 HDD 删除验收。

## 范围与执行

已确认此前无 aging-handoff、无遗留验收进程。此前 c4e14905 短时流程已完成并保留原实盘记录，本轮不重放它；只执行全新 `--prepare-natural-expiry` 分支。该分支为新来源显式设 retainDays=1，不修改旧30天记录、历史时间或系统时钟。

unit `whaledeck-qa-archive-aging-a0dbf8140001`，Invocation `6844f7fcee384be6b0e608192e821826`，success/0、103.364秒。新自有容器固定 `/bin/sleep 172800`，非root、断网、只读根、全部drop capabilities、no-new-privileges、无自动重启，32MiB/0.25CPU/pids16；只挂载本夹具目录。容器实际实例/源码/原始live证据/保护树/Root记录/原claim/HDD及业务均独立核验后，明确保留供老化观察。

新备份最早到期 **2026-11-10 10:00:02.252 +08:00**，notBeforeUnixMs=1794276002252。准备时 captureAgeElapsed=false、retirementExecuted=false、automaticExecution=false。当前仅保留Reserved及其保护，没有定时删除任务；48小时只限制惰性容器进程寿命，不会自动删除文件或备份。

## 验证与修正

18项老化专项和完整771项回收root测试通过，零跳过（83.525秒）；8项范围/14项前端CI合同通过。独立只读复核确认固定寿命、策略、源码清单与原live证据关联，未运行或替代实机测试。

观察器的源码闭包内部还会启动Git子进程，不能只给外围一个命令加 `-c safe.directory`。修正为观察进程内配置该仓库的安全目录及GIT_OPTIONAL_LOCKS=0，不改全局Git设置；新增干净环境中Root读取user所有仓库的真实Git回归，核对索引仍归user所有。此前未完成的测试修正已核实通过，不将失败历史追认成首次通过。

实机独立观察另外使用 `env -i` 干净环境重新启动，成功返回未到期/未执行；不继承准备进程的Git配置。观察器只读重核证据，即使到期也只报告 captureAgeElapsed，不调用apply、recover、reserve或清理。

生产Root202/恢复95/目录102/purge90/archive48、2346保护/11核心/产品f318d9ab、业务全行保持；HDD195→197只增本来源两份历史。来源和HDD同device，不签跨物理盘灾备。夹具仍保留，不能写成已清理。证据 `container-recycle-archive-copies-22c972ee74ca4edd993b3a9d04b67958`，20宿主/132验证源码，195文件另加manifest，SHA256 `224b72be46fc326cd177167ea398e83fc5fb4f87940eeeb7c335f2b51d6c5b2a`，root:root/0700目录、0600单链接文件。

## 续跑规则

到期后先以 `verify_container_recycle_archive_aging.py` 对上述证据名执行只读核验；源码/实例/业务/保护或存储变化一律拒绝，不重写旧基线凑通过。容器过期或实例改变也不能替换重建来冒充原来源。当前观察器冻结要求较严格，期间新增全局HDD历史或修改闭包代码可能触发拒绝，届时应独立证明合法差异，不能直接放宽或覆盖原证据。

自然到期只满足年龄条件，不代表执行成功。后续须单独构造受控正向门禁，重新验证完整副本、原claim、原记录链、存储/数据FD身份及执行授权，再执行本来源退役、生成证书并独立验证Retired、失败/Unknown和精确收尾。尚无标准RPC/安装/永久业务/API/Vue/Jobs或真人整链签收。

修正提交已推dev，准确CI `42548465168` 仍待完整终态，不能沿用前批绿灯。已补发此前3条待同步阶段记录，原eventId保留。自然等待期间继续其余可独立实施的工作，不将等待当作人类输入阻塞；全部59项仍未交付，17项移出不算完成，整项0/59、阶段0/6保持。
