# 容器文件/目录移动的内部FD候选

2026-10-29，候选 `d48aaae233839b1b50cc371f1be38c14c0229ecf`。当前产品运行时仍为`7b3f44e`/Agent minor45；没有安装移动入口、修改注册表或重启核心服务。59项继续，17项移出不算完成。

## 已实现

新增`whaledeck-container-move-scope.py`，工厂基于现有固定ContainerRoot复制范围适配器生成独立移动范围。源/目标逻辑根、私有原件区均与复制分离；仅复用显式FD原语，不调用宿主全局目录/注册表。源和目标都必须可写，源父/目标父inode、容器实例/配置/内核所有者持续复核；操作ID必须准确绑定，计划不创建私有区。

普通文件复用已存在的持久FileOperations移动引擎：目标完整复制并NOREPLACE发布后，源原inode隔离到本操作私有区，验证内容/属性再记Completed。目录复用FileTreeMoves：目标树验证后逐文件保留原inode，只移除已证明的空源目录；原件留存不等同HDD备份。故障只观察，不恢复执行副作用。内部宿主版本不成为公开容器ETag；未来外层必须用容器域封存计划/回执，不能把本适配器直接暴露为RPC。

所有重命名只允许：准确操作的暂存目标发布、准确源文件到私有原件、同一源子目录到对应保留子目录，以及独立root状态目录中的准确操作manifest。保留子目录的proc FD路径只作候选，必须从已固定源根openat2重开并比对内核inode，再核对目标保留树对应相对目录；路径前缀不能授权。拒绝覆盖、其他操作ID、外来FD/父目录、链接/特殊文件/特殊权限、只读源和其他复制私有域。

## Linux文件系统测试

17项专项全部执行通过，包含完整文件/目录/空目录/中文路径、普通元数据与原inode、重复观察、取消先于Apply、两端写权限、目标冲突、源父替换、错误操作和外来FD、目录子内容变化、部分保留后异常Unknown与不继续移动、符号/硬链接和FIFO。368项`test_container_*.py`关联Linux-root测试全部执行通过，0跳过，日志`/tmp/wd-container-move-scope.log`和`/tmp/wd-container-move-regression.log`。新专项与语法检查已接CI。异常测试是实际文件系统中的测试异常注入，未冒称真实进程SIGKILL。

## 冻结候选的真实Docker跨文件系统验证

`validate_container_move_scope.py`严格从指定Git提交导出源码并与工作树比较。独占无网络、只读根、UID1000:1000、限CPU/内存/PID、移除capability的sleep容器；一个HDD自有bind和一个明确命名/标签的NVMe Docker卷。两个实际`st_dev`不同，不以目录名称推断磁盘。

实际完成3次：HDD→NVMe普通文件、HDD→NVMe目录（含空目录）、NVMe→HDD普通文件返回。每文件2,097,159字节，全内容相等，目标1000:1000/0640/纳秒mtime匹配，私有源原件inode保持，重复请求不重复移动。原宿主和复制私有域未使用。所有Root状态/manifest留在私有证据；读取者将实际容器ETag只作验证数据，未称为产品公开回执。

实际验证前后11核心容器快照完全相等。先验证准确目录/卷中的完整白名单、全部文件内容及逐inode属性，再移除本批明确CID与命名卷及源目录；未验证数据策略仍为保留，不做递归盲删。独立只读再核对3个永久引擎Completed/原件与目标inode、准确CID/卷/源缺失、当前11核心存在。

证据`deploy/artifacts/container-move-scope-1d1b6e3c8e774abb9b5246c6185e8312/`：baseline、results、root-state、filesystem-proofs、verified、independent-final。日志`/tmp/wd-container-move-native.log`。

## 未完成

容器移动外层独立封存请求/计划/永久回执、固定helper/签名RPC、Agent版本与能力协商、永久意图/迁移/Job/Outbox/共享写入屏障、当前管理员/登录复核/审计、Worker、API、Vue和真实人类页面整链均未实现。还需真实进程中断、跨登录Unknown处置、原件长期保留与清理策略等。本批不签P1-11/P2-03或完整容器移动功能，不重复构建部署没有运行时差异的产品。
