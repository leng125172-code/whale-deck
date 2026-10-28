# 容器复制：固定 root FD 适配层

2026-10-28后续：独立容器计划/永久回执、元数据观察和固定传输候选已通过真实Docker九项（含七次候选子进程强杀），[新回执证据及剩余](CONTAINER_COPY_INTENTS.md)。本页保留适配层历史；当前仍未安装复制RPC或业务页面。

2026-10-28，产品候选 `4654a3b`；首次实际验证 `1004667`；失联回执修正 `000e4ad`，含实际目标消失门禁的最终冻结版本 `b8466a74d14d373647f0def519fb3a0977442259`。实际安装态仍为 `5f3622d` / Agent minor44。**本阶段是内部引擎候选，没有安装、RPC、业务 API 或 Vue 复制操作，不算完整复制功能完成。**

## 实现

新增 `deploy/host/whaledeck-container-copy-scope.py`，复用现有持久普通文件/目录复制引擎，不通过宿主注册路径、shell、Docker Exec 或额外挂载访问内容。

- 只从可信 ContainerRoot 的固定 root FD 以 openat2 解析已绑定源和目标。普通宿主 roots/directory/staging/namespace_lock 不参与路径解析或状态管理。
- 绑定容器 ID、实例、namespace/mount proof和内核派生 fsuid/fsgid；每次目标打开及原子发布前复核。禁止用户选择UID/GID，禁止覆盖、源删除及特权模式。
- 单独容器复制 state/private 目录和非阻塞 namespace 锁；不复用宿主文件引擎的全局状态路径。永久元数据锁不要求容器存活。
- 规划先验证容器域 ETag，再转换为内部宿主引擎元数据版本。内部版本不能当作容器 ETag 返回；后续外层协议必须另有容器计划/回执域。
- 只读源可以复制至允许写入的位置；只读目标、挂载根、祖先重叠、符号链接、硬链接、特殊对象、保护路径和特殊权限位拒绝。目录内部继续使用禁止跨挂载的递归解析。
- 保留普通模式及纳秒mtime，目标所有者来自容器实际内核映射。ACL/xattr和特殊权限位的完整保真尚未实现，不声称已支持。
- 同一适配器固定目标父inode，恢复观察也不可进入替换后的父目录。后续新进程恢复必须从已封存外层计划传入原 `targetParent`，不得重新采信当前父目录。
- 重试复用永久回执，恢复只观察；不自动重放复制/发布。非明确结果保留 Unknown及私有证据。

## 自动化证据

`sudo -n python3 -m unittest -v test_container_copy_scope`：19项全部执行通过，0跳过。使用真实隔离 Linux 文件系统，但 ContainerRoot/Docker存活范围为合成，不能替代实机证据。

包含2MiB+7普通文件与中文目录、空目录、全量字节/权限/mtime/内核所有者/同inode重试，宿主注册表禁止调用负控制、只读源/目标、保护后代、实例/挂载/所有者改变、源版本改变、目标抢占、父目录交换、发布前失效、私有部分树保留及不重复复制。

关联容器读取/暂存/备份/发布/历史/恢复/目录/传输/冻结，以及宿主普通复制/目录复制/树清单联合执行 **292项、0失败、0跳过**。它是列明测试集的结果，不冒称全部后端或所有容器测试。CI原有 `test_container_file*.py` 集另外以 `ResourceWarning` 为错误执行294项，0失败/0跳过；新专项19项使用同样规则通过。

新增父目录交换用例首次失败揭示：旧引擎恢复会在替换后的新父目录创建空暂存目录。适配器固定父inode后，创建前即拒绝；原私有内容保留，新父目录无新增内容。未修改宿主既有引擎。

发布前容器失联的新增回归修前实际失败：读取层Rejected与宿主引擎Rejected不是同一异常类型，回执会停在DestinationApplying。适配层现在保留原错误码并转换为引擎可处理的类型，修后明确Unknown/私有内容保留，重复原操作不重新发布。该用例在19专项及292关联集中重新执行通过；新专项已加入CI的Linux-root步骤，不依赖旧文件名通配符偶然覆盖。

## 实际 Docker 证据

`deploy/validation/validate_container_copy_scope.py b8466a74d14d373647f0def519fb3a0977442259` 已通过，原始证据：

`deploy/artifacts/container-copy-scope-058205673b0b40e2b2258d06de813ca5/`

验证器从冻结 Git 源码创建 root所有临时可信模块，使用实际 ContainerRoot、原生 Docker Inspect/实例/pidfd/namespace/mount proof；一只既有不可变镜像、无网络、1000:1000、只读根和自有可写 bind 的容器。

- 2MiB+7普通文件和含同样完整载荷/中文/空子目录的树分别实际复制成功；内容、模式、纳秒mtime、1000:1000及永久Root回执的目标inode逐项匹配。
- 重复同一操作返回同一结果和inode，原文件不变；实际只读根和挂载根拒绝。
- 成功轮逐项冻结准确文件/目录指纹，核对后只删除已枚举的合成对象；目录清理后链接数自然变化，最终rmdir校验inode/type/mode/owner及为空。
- 第三项在DestinationApplying的真实私有副本完整持久后，使用夹具绑定的准确CID移除容器，产品自然回执Unknown；私有完整2MiB+7及记录inode属性匹配，重复请求保持原Root字节且不重新发布。不是手工写入Unknown，也不是签名RPC。
- 两个自有测试容器与源已准确清理；第三项已证明的合成私有副本由夹具按准确指纹清理，Root历史仍为Unknown并保留为证据，不冒报业务任务成功。独立 `independent-final.json` 再核对冻结模块SHA、两准确CID/源不存在、三份Root终态和11核心healthy。

这不是安装态签名 Agent RPC，也没有人类授权/共享数据库屏障/BFF/UI证据。

此前1004667成功门禁 `container-copy-scope-d7261c491ba44293b87497aa587ddd00/` 继续保留；它验证了修前适配层的常规路径，不能替代000e4ad修复后的重新实机验证；000e4ad常规验证也保留在 `container-copy-scope-3d085452d3d04debb223278c0101fe54/`，最终b8466a7追加真实目标消失切点重新完整执行三项通过。

## 验证器失败与保留

- `container-copy-scope-80859498fa424c8ca3a66c9364fbf26d/`：文件和树已复制，清理子目录后错误要求父目录链接数保持原值，整轮失败。
- `container-copy-scope-760d6a61f4d54a9481f2cc7d4683551c/`：两个复制完成且输出清理，但挂载根用例同时是源的祖先，被更早的Overlap拒绝，错误断言导致整轮失败。重验改用普通下层镜像来源，将挂载规则与重叠规则分开检查。
- 两轮容器都已准确移除。未完整冻结原inode的残留未删除：逐项核对无链接/确定性完整内容、精确原路径及CID不存在后，整体移到 `/data/WhaleDeck/validation-evidence/container-copy-scope-<对应批次>`，根目录0700。原始清单、当前位置和“dataDeleted=false”保存在各自 `failed-observer-preserved.json`。不把转存称为已删除，不把失败轮计为通过。

## 后续完整实现门槛

继续新增容器独立计划/永久回执、进程丢失和SIGKILL恢复、签名RPC及Agent版本协商、共享发布屏障/数据库任务/Worker/API，最后真实身份桌面与手机页面。外层必须冻结原目标父、容器proof、原客户端版本与目标内核所有者；未知目标只观察，严禁把内部宿主版本或路径当作容器协议。移动/覆盖/删除仍需HDD保护及独立授权，不通过此复制适配器绕开。

仍按59项保留目标推进，17项移出不算完成。
