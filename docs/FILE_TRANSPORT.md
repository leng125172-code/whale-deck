# 受控文件传输与文件管理实现

P1-11/P2-03/P2-04共同基础，2026-10-16。完整目标仍为76项，不以本轮Agent基础替代浏览器文件管理、容器文件传输或团队隔离。

## 契约与部署边界

协议1.20、`files.registered-streams`，专用 `FileTransferService.ListRoots/List/Inspect/Download/Upload/GetUploadStatus`。UDS peer、每方法capability和单次nonce校验继续生效；Upload为client streaming，不复用任意命令/路径RPC。首帧绑定登记根ID、相对路径、操作UUID、登录作用域不可逆owner、声明大小/校验和/预期版本及主体上下文；后续只有顺序32KiB数据和最终commit，必须结束请求流，不允许二次header/超量/换目标。文件正文不进入OperationStore、日志或PostgreSQL。

独立 `/etc/whaledeck/file-roots.json` 为root所有、无组/其他写权限；初装创建，升级保留已有配置。默认登记共享、应用、网站、开发空间文件目录；仅代表文件根，不代表网站/开发空间功能已完成。当前允许的根位于 `/data/WhaleDeck` 命名空间，根目录和后代分别校验；核心数据库、Docker数据、系统配置和两配置仓库不注册。`.env`、Compose/manifest、密钥、secrets、Git内部目录及内部暂存目录等不能绕过专用配置/密钥流程读取或覆盖。新文件UID/GID由root配置决定，默认1000:1000；原文件保留普通权限及所有者，不保留setuid/setgid。

## 文件系统规则

宿主固定helper只支持六个枚举动作，正文走私有管道；root transient unit限制目录写入、无TCP、128MiB/8进程/1GiB文件/620秒，随Agent停止，停止须匹配InvocationID。Agent自身的sudo范围/能力bounding未扩大，API/Worker没有Docker或宿主文件系统挂载。文件驱动单独显式使用 `RestrictSUIDSGID=no`，因为该systemd seccomp开关会完全禁止openat2，不能与必须的安全路径解析并用；不是内核缺少openat2，也不回退为字符串路径。该固定驱动不执行用户代码，openat2仅允许只读打开，创建0600文件，复制权限只保留0777、剥除SUID/SGID，NoNewPrivileges/只读系统/受限写目录/其余沙箱保留；其他Agent与终端设置不变。[systemd官方限制说明](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml)

路径解析使用目录FD和Linux `openat2`，拒绝相对穿越、符号链接/magic link及登记根以下跨挂载（包括同盘bind），没有realpath字符串回退。不支持该内核调用时失败关闭。原子发布采用Linux `renameat2`，新建使用NOREPLACE，覆盖使用EXCHANGE保留被换出的完整对象。依据：[openat2](https://man7.org/linux/man-pages/man2/openat2.2.html)、[renameat2](https://man7.org/linux/man-pages/man2/rename.2.html)。

只传普通单链接文件，不读FIFO/设备等特殊对象；目录列表展示链接/受保护条目标记而不追随目标。列表稳定按名称游标分页，每页至多100；扫描最多25万名称/5秒，内存仅保留候选页，超预算报错而不伪装完整。下载先匹配版本，再流式传字节及最终SHA-256/大小；读取期间变化不发送成功end，不能把已收到部分字节当成功下载。

上传先在目标目录的root-only `.whaledeck-files` 暂存，长度/顺序/摘要及commit+EOF全部通过后才发布。四个root共享传输槽、每文件1GiB、10分钟，剩余空间预留5%或512MiB再加四份最大并发预算；历史回执达1万/总文件数达预算则拒绝新上传。历史副本不会自动清掉或声称已进入HDD备份策略；HDD备份前编辑、回收站和保留清理仍须后续接入。

## 结果与恢复

root-only原子回执与独占操作锁持久记录 `Receiving/Applying/Completed/Aborted/Unknown`，不存文件内容。相同UUID绑定全部元数据；完成的操作重发只校验/消费正文并返回历史结果，不重新写入，当前目标变更时bindingCurrent=false。查询活跃操作不抢先做恢复；进程已结束后按受控暂存名、设备/inode和摘要确认提交完成或未发布。无法解析根、外部并发或不能证明结果时标Unknown并保留数据，不自动重试/重写。

覆盖前检查原文件版本，覆盖后核对换出的原对象元数据和摘要。非协作外部写入竞态不能承诺文件系统CAS：若临界窗口发生变化，新文件可能已发布，但被换出的外部新数据会保留在root私有目录，返回Unknown，必须人工处理；不把这种情况称作“失败且目标绝无改变”。后续UI须明确展示、禁止自动重试。root私有前序副本不是已有可点击的回收站/恢复功能。

## 验证清单与当前结果

- 初版3efdeab本地Release 0警告/0错误，699 Unit/100 Integration通过；6原生PG在Windows明确跳过。修复版最新验证见下节。
- 工作站root所有的自有TemporaryDirectory实际运行21项Linux测试通过：路径/配置/权限、Unicode分页、symlink/hardlink/FIFO、同盘bind跨挂载、二进制下载/变动、顺序/摘要/EOF/尾随拒绝、覆盖前序数据、容量/4槽、真正fork进程退出于接收/发布前/发布后、活跃查询竞争、根消失及外部写入Unknown。临时挂载卸载和目录清理均由精确fixture完成，没有生产数据/网络变更。
- CI38893007384四作业全绿，包含上述21项Linux、原15项容器策略、699 Unit/100 Integration、实际非超级用户PG，以及176前端/35既有终端浏览器回归。既有前端测试不证明尚未生成的文件管理UI。
- 新增同源码原生UDS探针 `TerminalProbe files`，验证2MiB+7随机二进制、完整上传/下载摘要、幂等/覆盖前序/并发版本、错误主体/方法/重放/核心/路径拒绝和断线清理。实机首轮发现RestrictSUIDSGID拦截openat2；普通root诊断通过、同沙箱诊断失败，固定helper实际返回FILES_KERNEL_UNSUPPORTED。修复该冲突后，安装态固定helper的2MiB+7上传/校验/清理通过，UDS探针推进到覆盖并发阶段，又发现接收端拒绝时StreamWriter清理刷新缓冲掩盖原始错误。已改为底层管道先关闭、取消并等待写泵，再丢弃缓冲而非刷新，并补单元测试；后续完整探针签收待覆盖部署后记录，不能提前称通过。
- 修复后本机700 Unit/100 Integration；新增真实Linux特殊位剥除和openat2不能用于创建，22项在独立临时目录通过。`validate_file_driver.py`调用实际安装的fixed helper（不是导入函数）覆盖sandbox，`validate_file_kernel.py`提供无路径/正文输出的只读对照诊断，回执保留防重放，测试文件按独立名称和摘要验证后清理。

GLM Flash只读定位4个前端文件，一次成功、0重试；输入276335、缓存231296、输出2770 Tokens，97610ms，费用未返回。GPT复核结果并决定安全边界、实际实现/验证；外援未接触凭据、私有配置或高权判断。

## 最终安装态Agent签收：6bf4222

- 原始两次失败保留在上节：3efdeab部署本身成功不代表传输可用；先修systemd/openat2冲突，再修拒绝时清理刷新掩盖原始RPC的问题，没有放宽版本冲突/摘要/断线断言。
- 修复提交 `6bf4222eb0e96f6b5ba7d12b40af57919913f5eb`。Release全方案0警告/0错误，700 Unit/100 Integration通过；Windows6原生明确跳过。真实Linux22文件用例通过（包含同盘bind、真正fork退出、特殊位剥除）；[CI38896550960](https://github.com/leng125172-code/whale-deck/actions/runs/38896550960) 四作业全部成功，包含15容器策略、22文件Linux、非超级用户真实PostgreSQL实际执行1/通过1、176前端单元和35既有终端/授权浏览器检查。既有前端测试不证明文件UI。
- 同源码工作站冻结PG：`dispatch-580d9970bf734c859789a4b7525c3405.json`，2026-10-16 11:53:28.485801–11:55:08.687941 UTC，sourceSha匹配6bf4222，executedTests=passedTests=1、state=Passed、cleanupConfirmed=true；锁定SDK image ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。不把本机跳过当原生通过。
- 实际平台更新unit **2026-10-16 19:54:10 CST** 已Deactivated successfully，Result=success/ExecMainStatus=0；调度Job成功不是本条证据的替代。
- 同源码冻结发布探针 `terminal-probe.gsm3bU/TerminalProbe files` 实际通过：UDS peer/缺失和错误方法capability/nonce重放、未注册核心根和.env/穿越、2MiB+7随机二进制上传/列表/inspect/下载完整SHA/长度、完整旧对象保留、过期版本Aborted、不同owner不可查询、历史重发不覆盖新目标、非法offset拒绝、断线后Aborted/未创建且目标不变。独立调用实际安装helper的2MiB+7上传校验也通过；测试自身只删除名称+摘要匹配的文件，root回执保留防重放。随后文件根无wdv测试文件、file transient unit为0；没有触碰生产数据库数据/数据卷。
- 覆盖后真实Authentik→BFF→Gateway→UDS宿主与容器终端回归、普通/匿名权限、Origin/CSRF、持久关闭/撤销与通知管理回归通过；临时终端容器/会话清理通过，8个前端HTTP资源与实际Gateway字节一致。没有新文件页面或生产文件UI浏览器验收，也没有外部通知发送。
- 最终宿主validate通过：Agent/维护入口active，UDS/固定helper/登记配置权限/动态入口正常，自包含.NET/ASP.NET/PKCS均10.0.13；11核心容器healthy。开发sudo保持用户授权原状；Agent最小固定helper范围未扩大。

本轮GLM Flash机械只读侦察5个前端文件，provider=glm/model=glm-5.3-flash，一次成功、0重试，input=122034/cached=93440/output=2445 Tokens、72336ms，费用未返回。没有模型切换/凭据外发；GPT复核、实际修改与安全/上线判断。

## 尚缺（不关闭P1-11/P2-03/P2-04）

API/Application持久上传意图、当前管理员/登录绑定与二次确认、任务/Outbox/人类主体审计、未知结果人工处理、HTTP限流/CSRF/可取消传输、Swagger和文件管理/终端传输WebUI；容器文件适配、目录创建/移动/复制/编辑/权限/搜索/压缩与解压、回收站/HDD备份、团队隔离和长期清理都仍待继续。当前Agent只向可信API/Worker暴露内部能力，不注册未实现的浏览器按钮或空API。
