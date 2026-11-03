# ACL来源故障工具候选：仅隔离本轮新捕获与精确还原

2026-11-04，接续已部署的28062da和[来源UI实机签收](CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)。本批实现验收工具候选，**没有新增真实来源丢失/替换验收，没有发布、账号创建、Worker/Agent重启或生产来源隔离**。59项及六阶段的完整验收状态不升。

后续c3dca1f已使用新捕获完成[Ready后来源丢失确认拒绝实机验收](CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)，包含精确还原和独立只读核验。下文仍保留此前候选批次的原始范围，不能把后续一场推广为全部来源故障签收。

## 已实现的范围

`deploy/validation/owned_acl_source_quarantine.py`无CLI，不安装到`/usr/local/libexec`，不增加sudo、RPC或产品Agent权限。后续真实门禁必须在新自有容器内通过正常Authentik/Vue捕获ACL，沿用已有实际文件、SQL、Root、HDD验证，再调用本工具；不能以本工具的夹具输入代替这些证明。

1. 捕获前保存HDD规范目录的有界清单，逐文件记录字节SHA256和九字段：device、inode、mode、uid、gid、link count、size、mtime ns、ctime ns。旧捕获不能作为隔离目标。清单上限10000，不把atime变化误判为内容变更。
2. `bind_capture`要求新普通access捕获已完成、无租约/保护屏障/待发消息，subject、CID、instance、准确路径、owner、原执行ID、SQL计划、Root Completed及两份原始字节摘要一致，重算HDD历史digest；恢复任务、Unknown和旧捕获都拒绝。
3. `installed_namespace`只从安装态FileBackups固定`container-acl-v1`目录获取FD。每次修改前重新核实规范目录inode及真实挂载/UUID/HDD/可写证明；不能把“同device目录存在”当安装态存储证明。
4. 只把准确的新`captureId.json`移入同一HDD内固定命名的私有目录，使用Linux `renameat2(RENAME_NOREPLACE)`，禁止覆盖。隔离前fsync根私有证据日志及新隔离目录身份证明；隔离后核实原inode/字节仍在、规范文件名已不存在，全部旧捕获不变。不编辑、复制伪造或删除元数据。
5. 正常结束或Python异常后精确还原；QA进程被强杀后，新进程可凭已落盘日志核实同一隔离目录和原件，再还原同一inode。规范目标被占用、原件被改、隔离目录被替换、旧捕获变化时拒绝覆盖/清理，保留双方供核实。同名空目录也不能无证明删除。

工具只恢复这次QA隔离的元数据，不恢复旧ACL、不重试产品操作、不回滚dev。调用者仍须在动作前核实全局空闲、准确自有容器/捕获和原件证明，并冻结自身全部依赖；该调用链尚待新增真实门禁接入。

## 本批验证与边界

命令：

```sh
sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p test_owned_acl_source_quarantine.py -v
python3 -W error -m py_compile deploy/validation/owned_acl_source_quarantine.py deploy/validation/test_owned_acl_source_quarantine.py
```

最新结果：**22项通过，0跳过**。用临时隔离目录中的实际文件/syscall验证同inode还原、异常还原、准确子进程SIGKILL后新进程恢复、NOREPLACE、目标占用保留、旧捕获/SQL/原件摘要拒绝、符号链接/硬链接/FIFO/权限/目录替换等。存储/SQL/Root数据是显式夹具，崩溃进程是测试子进程；这不是生产HDD离线、Worker强杀或真实身份/UI验收。CI的installer检查已接入这22项和语法检查，远端CI终态另行取得。

附带范围检查8项、CI分片离线回归9项通过；CI YAML无重复键、1755个仓库相对Markdown链接无缺失，目标第1节逐字保持。一次CI测试收集误用`deploy/validation`目录返回0项，未计通过；改为实际`tests`目录后执行9项通过。没有重跑全量前后端或浏览器并冒称本批证据。

复核补入隔离目录inode证明时，首轮22项有1项异常：安全拒绝已发生，但测试误期待最初的FileExistsError，实际finally恢复因缺少目录证明产生FileNotFoundError，原异常仍在`__context__`。已修正为同时断言恢复拒绝和原异常，重跑22项全部通过；没有为通过测试放宽目录清理。

真实安装态仅执行**只读预检**：通过实际可写HDD证明、固定规范目录FD绑定和连续两次清单读取；137份既有元数据字节及九字段与前批记录全部相等，没有调用arm/hold/restore。六组件仍28062da，11核心healthy、两宿主PID2178169/2178164、7项受保护配置、完整发布账本及空闲状态保持。此结果只证明故障工具的安装态入口预检，不能签收故障注入。

首轮汇总探针因复用旧脚本的`baseline`局部名覆盖外层变量而KeyError；先前只读137项比较已成功，未执行隔离。保留失败记录及脚本，使用独立变量重新完成只读核对。没有重放已签收的真实恢复。

私有证据目录：`deploy/artifacts/acl-source-quarantine-candidate-20261104/`（目录root:root/0700、文件0600）。保存22项测试日志、安装态只读预检、运行完整边界、两版探针和失败说明；不进入Git，不含登录凭据。

## 后续验收不可省略

- 每场用新自有资源，正常页面捕获、真实SQL/Root/HDD核实，捕获前清单不能补造；旧137份及旧失败源树保持。
- 登记、异步预检、确认、首次Apply前的来源缺失分别取证。现有成功路径脚本强制历史读取200/登记202，不能直接套用作故障门禁，也不能把SourceChanged当普通502/503后自动重试。
- 真实Vue错误/取消/正常退出，原计划/执行ID、原期限和冻结来源、审计、无Apply及无新当前ACL前像HDD、实际目标字节/两类ACL/属性不变，均需按切点证明，并独立只读复核。
- rename必然改变本轮来源ctime及目录时间戳；还原只承诺该原件的字节、inode及其余八字段，不声称本轮来源九字段完全未变。旧捕获仍要求完整九字段不变。
- 强杀恢复只覆盖已保存完整日志和目录身份证明后的隔离状态；若在新目录创建与该证明落盘之间退出，保留未证明目录，不能自动删除。未证明的目标或原件必须保留，不能为清理而放宽断言。
- 本工具没有实现元数据替换注入、整个HDD/Agent不可用、来源三态回执优先的真实故障验收；[来源故障矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)、递归ACL/恢复、删除长期清理及P0-05维护入口连续性继续待办。
