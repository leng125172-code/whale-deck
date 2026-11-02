# 容器所有者：真实组撤权、账号停用与正常授权回归

日期：2026-11-02。六组件已正常覆盖d1fcca891830b7a43da0b253c3abcad67a66c18e/Agent1.48，所有者实时权威身份不再只依赖2小时正快照。以下两类真实撤权与同版本常规7场景、独立核验通过；不代表其他模块即时撤权、所有BFF票据销毁或完整P0-02/所有者/文件模块完成。

## 一次覆盖与运行来源

运行checkout从本地干净dev快进到已通过3585 Unit/258 Integration（7默认原生跳过）及Release的d1fcca8。唯一发布单元whaledeck-dev-publish-d1fcca8.service，InvocationID 8d16470c4d7446a39c20a1f56a7cf01c，同次journal成功终态、Result=success/0；没有重复发布或自动回滚。

六组件实际build-info/镜像revision、API/Gateway HTTP与宿主MainPID的exe device/inode及DLL映射一致；所有者表/迁移仍在。11核心healthy，数据库平台各容器ID/镜像/状态/StartedAt与覆盖前一致；API/Worker无宿主端口、无Docker Socket、非privileged。宿主/UDS/固定helper/HDD策略和动态入口检查通过。user NOPASSWD:ALL按授权保留，sudoers校验通过，产品Agent未扩大授权。

第一次只读来源检查早于原发布结束，正确拒绝BUILD_IMAGE_REVISION_MISMATCH；等待原单元结束后完整核验通过。原早期错误与说明保留，不将它说成发布失败，也没有重开单元。

## 严格限定的真实身份变更

原管理员实际Chrome/Authentik表单登录，在真实Vue文件页填写2200:3200并明确确认。Worker在Pending后恢复，观察器以原执行/计划、原inode、容器根FD及操作锁FD绑定已安装helper，仅在Preparing且HDD已验证、原文件未变时暂停它；随后由产品结束helper，验证器不发送SIGKILL。

新增QA身份变更工具只接受本轮temporary_user产生的用户名格式，逐一核对Authentik用户名、原Subject UUID、数字PK、is_active及原管理员组。只向该精确账号发送一次groups=[]或is_active=false，前后GET确认。禁止任意用户名/路径/动作，身份改变或丢回复不允许再次PATCH；服务凭据只在内存，不写Git/日志，未修改生产用户或组定义。

两例都确认原权限快照还有一小时以上有效期。没有编辑缓存、SQL、时钟或应用角色映射，也没有用原用户取消/退出代替真实撤权。

| 场景 | 真实源变化及页面 | 最终helper退出观察 |
| --- | --- | --- |
| 管理员组移除 | 临时账号groups变空、账号仍active；原接口401/403拒绝，auth/me为200且非管理员；重新加载后403“没有管理权限”页，原任务提示清除 | 5.221秒 |
| 账号停用 | 临时账号is_active=false、组保持；原接口及auth/me拒绝（me为403）；加载完成后显示登录页，原任务提示清除 | 5.177秒 |

每例页面登记/确认各1，取消POST和内容PUT均0；SQL cancel_requested_at_utc仍null，证明不是按钮取消。原意图与执行Job为Canceled，Root Aborted/ownerConfirmed=false；HDD已验证、publishedVersion为空，屏障/租约清理。结果审计唯一且保持原主体。原2097159字节和九字段指纹（含原UID/GID、mode、mtime/ctime）全部不变，准确源/CID与临时身份清理，永久SQL/Root/HDD历史保留。

两个父命令及两个独立只读进程均退出0，重新读取SQL、Root/HDD原件与摘要、原执行审计，验证身份变更只属于原Subject及准确资源不存在；每次最终活动/屏障/租约0、11核心healthy。独立进程不声称清理后再读取了物理文件。

这是本批实际响应观察，不是所有网络条件下的5秒保证。6秒为巡检节奏，权威检查和RPC/helper仍受既有有界取消预算约束。停用用户的旧页面和所有者接口已拒绝，不据此声称全部BFF会话或其他模块都已即时注销。

## 新授权路径的正常实机回归

同一d1fcca8实际运行版本完整重跑常规7场景，6 Succeeded/1 Ready执行前取消：文件2200:3200、无变化、显式0:0、恢复1000:1000，目录2200:3200及恢复2100:3100，以及取消。7次原登记/6次原确认重试、刷新接续、权限/CSRF/Origin/缺UID拒绝、正常双端退出通过，页面内容PUT为0。

SQL/6 Root/6份物理HDD元数据、完整文件及子文件、原inode/mode/mtime/最终恢复属主和非递归子项保持通过；NoChange完整指纹不变。父命令和独立只读复核均退出0，准确清理、临时身份/活动保护0。HDD为元数据留存，不冒称内容备份或本批NVMe/异盘业务验证。此回归用于排除实时授权误拦截合法管理员，不能由撤权负例替代。

## UI证据补强与原记录保留

首轮组移除/账号停用的源变更、接口拒绝、helper退出、SQL/Root/HDD和独立核验已通过（分别5.154/5.046秒），但重新加载后截图太早，仅拍到最少3秒的BootScreen，区域不存在断言也可能在启动中满足；因此不作为最终完整页面签收。

9ea4bff补等待启动画面消失，并正向断言403标题或登录标题、对应URL、原任务区域与sessionStorage提示消失。以新临时身份完整重验两例及独立核验通过，上表采用补强后数字。最终两张页面截图已目视复核，明确为403和登录页；首轮原件、早期截图及核验保留，不改写或重复累计。没有为适配验收器修改产品。

## 自动检查与私有证据

新增12项身份变更边界测试通过：精确用户名/UUID/PK、原管理员/active、唯一PATCH、身份交换、未知动作、失联不可重发和非布尔active等；无真实HTTP。原16项Linux-root观察器零跳过，Python -W error/JS语法通过并接CI。d1fcca8的3585 Unit/258 Integration与Release证据属于前批同一产品候选，本批没有重复全跑；GitHub CI尚无全绿终态，不签全绿。

最终实际验收源码9ea4bff；初版cdfaecc及各源SHA保留在validation-source.json。运行版本与验收源码严格区分。

- 发布：deploy/artifacts/container-ownership-live-auth-deploy-d1fcca8/，before/provenance/verified及原Invocation/unit journal、verification-notes。
- 最终组移除：container-ownership-faults-be9a9d625fcd4642a0b2e7d7ba4b79ad/；最终停用：container-ownership-faults-718fab0da2fe4c56a5e2d2223d7eca59/。含源前后确认、原SQL/Root/HDD、完整文件证明、截图与SHA、independent-final。
- 首轮UI不完整记录：container-ownership-faults-91098a28c77b4cbaab00eb61913630dd/、container-ownership-faults-83a17e802653465c968123027aab59c4/。
- 正常7场景：container-ownership-browser-7f35a66479b7491d9237a3b7dd1f453c/，原业务与6份Root/HDD、7张PNG和independent-final。
- 日志：/tmp/wd-owner-authority-{group,disable}-{first,second}.log及对应independent日志、wd-owner-authority-positive-{browser,independent}.log、wd-owner-live-auth-deploy-verification{,-final}.log、wd-owner-authority-host-final.log。以上私有证据不入Git。

继续fchown之后/回执丢失、目录/实例重建/跨盘等其余故障，递归权限、ACL、可恢复删除/长期清理及其余59项。17项移出不算完成，master仍未正式交付。

记录收尾检查：8项范围和12项身份变更边界通过，6份修改MD的504个本地链接无缺失，git diff --check通过。

2026-11-02后续：[实际变更后取消/Worker回执中断](CONTAINER_OWNERSHIP_POST_CHANGE.md)已由原操作完成实机对账及独立核验；其余组合、递归/ACL/删除仍待。本页源撤权证明不由此改写。
