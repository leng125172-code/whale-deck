# 系统日志进程列两行布局

## 最终安装态签收：b666042

2026-10-19实际运行时为`b666042019aeff6d22896f30eaf360f70410196b`；以下“候选/待签收”为当时记录，不覆盖本节。仅完成系统日志进程列的本子范围，P0-08和完整76项目标仍进行中。

- 固定源码[CI39183928285](https://github.com/leng125172-code/whale-deck/actions/runs/39183928285)四作业全部成功：265前端单元、219生产浏览器（18.3m）以及后端Release/测试/依赖审计、installer与实际CI PostgreSQL。新增6项浏览器与既有额外10项本地布局/搜索证据分别计数，详见下节。
- 正式覆盖只调度一次。systemd实际09:29:28–09:32:49 CST执行，InvocationID `e5f141aa5ae24136b438f8d502200b0b`；最终inactive/dead、MainPID0及manager记录`Deactivated successfully`，wall3min21.492s，不以调度/启动代替终态。
- 六实际组件程序集/不可变镜像来源及回环API/Gateway HTTP均核实完整b666042，产品版本0.1.0、RPC1.29未增加新能力。基础数据库依赖容器保持原实例、未重建，不改变网络、sudo或KDE。host validate通过systemd/权限/UDS/受限helper/HDD/动态入口，宿主.NET/ASP.NET/PKCS保持10.0.13。
- 真实临时Authentik管理员Cookie登录实际网页；当前身份为管理员、实际API构建提交等于本次完整SHA。页面重新查询真实批准journald资源，逐一核对前20条实际API返回的process/processId与DOM第一行完整title/名称、第二行PID一致。没有Playwright route mock，不输出或保存日志消息正文。
- 实际320/1440明暗主题中，名称单行nowrap/ellipsis、第二行在首行下方、12px主题辅助色、整页无横向溢出均通过；页面错误0、管理写请求0。截图只截一个进程/PID单元格，不截图整张真实日志表，已目测1440明/320暗；长名称和缺失PID由新6项合成浏览器严格覆盖，不冒称实机恰好出现这些边界。
- Cookie只经隐藏SSH/进程内存/stdin，不进入argv/日志/文件；验证身份最终独立核对4前缀均0。六类未结束文件任务0、路径锁0、未关闭终端0、自有正文搜索旧夹具0，11核心healthy；不清除任务/审计/回执。
- 原真实OIDC、Origin/CSRF、面板和Authentik同时退出并跳回/login，以及8个HTTP/活动Gateway资源字节一致复验通过。
- ignored `journal-process-validation`助手SHA256：channel.py `557bc491db299681904d52e47a8f633033f2db006e1dd498b8d92ca36a702588`；browser.mjs `2f259acd8e6403d8f590f7d960ba7470653e277d05595180efc17e9dfcedd95f`；bridge.ps1 `69c680617416e78338224eb1fb9d1df5ea9a682de7e4e44249d7ca1af33a2bcc`。这些私有IPC帮助器后续单独运行，不冒称已包含在原始CI源码中。

## 源码与范围

运行时候选 `b666042019aeff6d22896f30eaf360f70410196b`。本批只修改系统日志WebUI与浏览器回归/CI清单，不改变API、日志采集、权限、数据库模型或日志原文。

- 进程列最小宽度130→170px，第一行保留原字体/颜色展示进程名称；单行省略、完整原名保留在title和DOM文本中，不因名称长度自动多行。
- 第二行独立显示`PID <id>`，12px和Element Plus主题辅助文字色；进程或PID缺失显示`—`，不伪造PID。列内不再拼接`[PID]`。
- 不把小屏表格的内部横向滚动改为整页溢出；其余列和既有消息完整提示不变。

## 当前证据

- lint、格式、Vue类型检查和生产构建通过。
- 初轮同时运行构建和默认并发Vitest时，3个既有测试触及5000ms超时：HostProcessesView、NotificationPrivateEditor、NotificationShell各1项，另262通过。保留失败事实；构建结束后限制Vitest为2worker，34文件/265测试全部通过（87.52s），未修改默认超时、断言或产品代码来掩盖失败。
- 新增6项生产预览浏览器通过（55.5s）：320/1045/1440、light/dark，实际DOM几何验证第一行nowrap/ellipsis、第二行在其下、12px辅助色、长中英文名称确实溢出后省略、缺名称/PID占位、四种名称长度行高一致、整页无横向溢出。该证据使用明确的UI fixture，不冒称实际journald或真实身份。
- 已目测1440明主题合成截图，进程名与PID均可读，长名称没有挤出第三行。实际日志消息不应保存进截图或测试输出；安装态验证只截图进程/PID单元格。
- 工作站冻结同源码实际PostgreSQL报告`dispatch-c5adc7c1359f4f879819f723dc9a157e.json`：2026-10-19 01:11:33.389957–01:14:07.803062 UTC，执行1/通过1、清理确认；SDK image ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。没有生产数据变更或外部投递；不把SQL套件内Root double当作本次真实日志读取证明。
- [CI39183928285](https://github.com/leng125172-code/whale-deck/actions/runs/39183928285)固定上述提交，四作业全部completed/success；265前端单元/219浏览器（18.3m）、Release后端及原生PostgreSQL、installer全部通过。
- 额外10项本地旧布局/搜索回归通过（2.5m），包括1920/1045/320的明暗主题跨页面切换、空态/503和搜索对话框长文本；与新6项生产预览证据分开计数，不冒称全部是实机。
- 安装态与真实管理员网页仍待签收。上次运行时仍5e131e9/RPC1.29；09:29:28 CST新维护任务启动后确认active/running，InvocationID `e5f141aa5ae24136b438f8d502200b0b`。不以任务已启动宣称页面上线，也没有重复调度。

GLM/glm-5.3-flash仅只读检查指定3个公开文件，82050输入/51968缓存/1692输出、74161ms、1成功/0重试，费用未返回。其建议中的字符串unknown PID与真实number契约不符，未采用；GPT独立实现新回归并使用0→缺失占位，负责实际修改/验证/部署。完整76项P0–P5继续active，不由单列样式签完P0-08。
