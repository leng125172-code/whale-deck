# 递归 ACL 安装态签名 RPC 与 HDD 分项验收

2026-11-04，宿主源码 `7cb3935bfff3c53fa1d0682b05ebf16ff12951d8`，探针及验收源码 `31c2249b7bc066dbe7dda75877eb315e6692b375`。本批实际完成标准宿主安装、真实签名 UDS 四方法、Root/HDD 与独立只读核验；承接[协议候选](CONTAINER_RECURSIVE_ACL_RPC.md)，未重放前批六个原生内核操作。**未完成永久业务、Worker/API/Vue/Authentik 真人整链，不提升59项主任务或阶段为完成。**

## 来源、依赖与安装

准确产品 CI `41733525746` 的 head 为7cb3935，12个job全部 completed/success（5个基础检查、6个浏览器分片及最终frontend门禁）。最初两次查询遇到网络EOF，随后取得成功终态再安装；没有绕过门禁。

标准 `deploy/host/publish.sh` 从冻结 Git archive、固定digest SDK构建两宿主；SHA256SUMS全量、实际两个CLI --build-info、.NET/ASP.NET/PKCS 10.0.13核对通过。没有把工作区、私有环境或凭据挂入构建。标准探针发布同样只构建31c2249跟踪源码，校验和及其启动时assembly revision绑定核对。宿主、探针和验收源码版本分别记录，不能用探针回显代替实际运行进程来源。

从7cb3935冻结的安装源码调用标准install.sh，安装unit退出0；Agent与MaintenanceHost实际进程来源均为7cb3935，Agent能力minor1.53。Agent PID由2178169变为2799620，维护宿主由2178164变为2799617。四产品容器与发布账本仍为28062da；这是宿主更新，不能写成六组件整体覆盖。未重启主机、未回滚dev。

新root状态目录0700、15个关联宿主模块root:root/0750及逐字来源核对通过。固定helper的4cap、写路径/方法与隔离策略按安装源码核对，实机调用经该helper完成；未在本批另行采集每个瞬时进程的/proc能力位。没有新增CHOWN/SYS_ADMIN，user NOPASSWD:ALL实际仍可用。

安装前后7个受保护配置及完整发布账本的内容hash/权限/所有者保持，11核心容器ID/镜像/StartedAt保持且healthy；原145普通ACL和原6聚合/17子项递归候选HDD元数据字节及九字段身份保持，Git索引所有者1000:1000且安装窗口未改变。新业务任务/租约/路径锁等活动为0。

构建和安装unit完成后已卸载，没有及时取得其InvocationID，journal未恢复到ID；记录保留空值，不补造。RPC验收和兼容验收的实际InvocationID已在运行中取得并保存。构建/安装退出结果、产物与安装后来源另有直接证据。没有采集本次安装全窗口入口探测，因此不能补签P0-05零中断；前次159样本含1次连接失败的原记录保留。

## 新协议八场景

独占新容器：固定已有镜像、无网络、1000:1000、只读root、唯一自有/work可写bind。树含两个目录、两个文件，每文件完整 `2 MiB + 7` 字节；所有节点设置原始owner/mtime及普通属性。default先执行，使后续access操作实际证明保留已有default ACL。

| 场景 | 真实结果 | 实际变更节点 |
| --- | --- | ---: |
| default目录写入 | Completed，文件原access ACL只验证 | 2 |
| access分文件/目录写入 | Completed | 4 |
| access无变化 | Completed，全部身份保持 | 0 |
| access文件mask修改 | Completed，目录不变 | 2 |
| named UID0/权限0 | Completed，inode所有者不变 | 4 |
| default无变化 | Completed，全部身份保持 | 0 |
| 显式空default目录列表 | Completed，清除default | 2 |
| 写入前Discard | Aborted，无当前HDD捕获 | 0（原计划4） |

Plan实际拒绝18项：无签名、普通ACL域、递归owner域、过期/未来、方法串用、文件ID/目录权限篡改、缺两个wrapper、缺named ID/permissions、缺mask、重复entry、default带file wrapper、核心目标、只读目标及nonce重放。操作阶段8项拒绝包含manifest/目标列表/actor篡改、普通ACL域、错误方法、缺Job和未登记Inspect；原operation无Root记录、源树保持后才允许正常Apply。

七个Completed保留7聚合/28子项HDD历史；取消项只保留Aborted Root，无子目录或HDD历史。八项均继续真实Inspect、迟到同ID Apply和Discard；原Root字节、原outcome与整树保持，取消不续做。每次RPC/重放后读取完整文件字节及ACL，核对inode、owner、mtime、普通属性和非目标ACL；仅预期模式/目标ACL/ctime可以变化，无变化项九字段精确保持。确认源树后准确删除；独占容器和源目录实际缺席，Root/HDD证据保留。

第二进程只读核对真实Root及副本、真实attested HDD及副本、计划/manifest/child ID/历史/结果散列域、43条原始响应顺序、全部计数/目标/版本、原inode与ACL快照、源配方及准确清理。父级与独立核验均退出0。已删除字节不声称再次读取；完整字节比较发生在父级清理前。源和HDD同为device2049，未证明异盘恢复或完整NVMe布局。

## 既有 ACL 兼容与验证工具

在同一已安装minor1.53上另建独占资源，旧单项ACL九场景（access/noop/mask/UID0/base/default/default-noop/clear/cancel）8完成/1取消、8份新HDD元数据，13规划拒绝/4执行拒绝、真实迟到签名RPC不重写、独立只读核验和准确清理均通过。不是旧二进制全量回归，也没有补签旧递归chmod/chown或全部历史读取/恢复场景。普通ACL元数据总数153，原145份保持；递归namespace共13聚合/45子项，包括前批6/17及本批7/28。

新原生探针使用固定UDS、短时完整消息签名、内存密钥清零、有界输入/输出/时间预算。新增独立转换拒绝丢失presence、bool/float冒充整数、64位截断、乱序/重复/缺mask与default文件wrapper；独立完整字节配方防止复制旧前缀造成误签。17个新离线检查及全部递归probe范围73项通过，8范围基线通过，Release探针构建0警告/0错误，Python语法通过。曾误用tests目录的test_goal_scope.py模式取得0测试，未将其计通过，随后执行正确test_scope_baseline.py取得8通过；一次从root私有目录作为普通用户cwd启动被拒，改从仓库进入后核实产物，没有重复构建/安装/RPC。前批4500/490/506属于协议候选证据，本批未重复全量后端或前端测试。

私有证据（不入Git）：

- `deploy/artifacts/recursive-acl-host-7cb3935-0332debd8b50/`：标准构建/安装请求、日志、准确CI、前后保护快照、来源/校验和/运行时证明及RPC/兼容unit回执。
- `deploy/artifacts/recursive-acl-probe-31c2249-01faa2396d7c/`及`terminal-probe.ca9spT/`：冻结探针标准构建与产物证明。
- `deploy/artifacts/container-recursive-acl-agent-299621fab1804729a275879bbd8b1857/`：新协议baseline/逐项Root/HDD副本、43条响应、物理快照、父级与独立结论。
- `deploy/artifacts/container-acl-agent-58ee8471ed084c53a75912288395ab8e/`：兼容九场景与独立结论。

## 剩余门槛

后续内部编排候选0168d5b/4b01148已实现并通过离线专项及后端回归，见[内部业务候选与持久化边界](CONTAINER_RECURSIVE_ACL_BUSINESS.md)。该进展不倒填本页实机业务证据，真实Repository/共享屏障/Worker/API/Vue仍未完成。

后续先接永久意图/Job/outbox与共享目录保护、迁移/数据库检查、Worker/API/Vue，再部署取得真实Authentik身份和页面整链证据。仍需安装态Partial/Unknown/取消/强杀/重启/实时撤权/跨写入/能力兼容/准确stop与EOF等完整故障矩阵、递归ACL历史恢复、可恢复删除和长期清理。当前Agent探针直接持有root私有签名能力，Actor是固定探针标记，不是真实人类身份，不能当作BFF/CSRF/Origin/资源授权或页面验收。其它59项、酒馆/Live2D与统一UI计划继续，不用本批17个操作增加主任务完成数。
