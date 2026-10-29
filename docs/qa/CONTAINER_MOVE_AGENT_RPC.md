# 容器移动 Agent 1.46：安装态 RPC 与跨盘固定 helper

2026-10-29，宿主安装版本 `53701f85e9f3d52866699585fe561b90202f800e`。本阶段是容器移动的传输/宿主子门禁；永久业务 SQL、Worker/API/Vue 尚未交付，不签完 P1-11/P2-03 或全部59项。17项移出范围不计完成。

## 实现与安装

- 独立 `ContainerMoveService` Plan/Apply/Inspect/Discard、主版本/次版本46和 `docker.files.move-intents` 能力门禁。
- 全 protobuf 内容及描述符绑定、独立签名域、一次性 nonce；旧复制/目录域签名不能授权移动。
- 固定枚举 helper 与准确 InvocationID 停止；私有 stdin、有界输出和 systemd 沙箱。没有 Agent TCP 或 API/Worker Docker Socket。
- 发布、源移除、原件保留分别使用 nullable bool；未知不能转换为 false。Completed 必须三项 true；Aborted 必须三项 false。
- 宿主产物冻结源码构建，没有构建或重启 WD 应用镜像。707个安装文件与构建产物逐字节摘要匹配；Agent/MaintenanceHost 为53701f8，API/Worker/Gateway仍7b3f44e。
- 安装后 `deploy/host/validate.sh` 通过 systemd、UDS、固定 helper、HDD 固定策略、权限与动态发现入口检查。运行库及 PKCS 均10.0.13。

## 已执行证据

1. `validate_container_move_agent.py` 与冻结同源 TerminalProbe：真实签名 UDS 两次完整2097159字节文件/目录移动，原计划查询与执行重放、永久取消后迟到Apply拒绝。匿名、错误签名域、请求体篡改、nonce重放、核心容器/只读目标/保护路径及伪造UID拒绝。普通属性、混合UID/GID、原文件inode、独立Root回执与准确清理匹配。
2. `validate_container_copy_agent.py`：原有签名复制 RPC 两类完整数据、永久取消与原件/属性回归通过，准确资源清理。
3. `validate_container_move_scope.py <e839a43> --installed-helper`：真实非root、只读根、无网络独占容器；HDD bind与显式NVMe命名卷，device分别2049/66308。通过**已安装固定 systemd helper**完成HDD→NVMe文件、HDD→NVMe混合所有者目录、NVMe→HDD文件三次移动。完整内容、原文件inode、发布目录普通属性、重复调用和永久回执不变均核对；准确CID/卷/源已清理。
4. 另行读取三个永久外层/内层Root回执，独立规范化JSON SHA256与原 `innerReceiptHash`、原计划/操作ID/owner/三项事实均吻合。再次核对准确容器/卷/源不存在，11核心ID/镜像/运行状态/StartedAt与安装前一致。

签名UDS验证使用常规HDD夹具；跨盘验证直接调用固定helper。两者分别证明各自范围，不声称已验证“跨盘人类身份→业务SQL→Worker→签名RPC→页面”整链。

本轮代码验证：Release零警告错误；3091 Unit、205 Integration通过（7项原生依赖测试默认跳过），399容器Linux-root测试零跳过；宿主元数据12项、scope8项通过。这些是53701f8传输源码结果，不自动代表后续业务候选或全部CI成功。

## 失败及修正

- 调试客户端发布脚本权限0600，直接执行返回Permission denied；改用明确 `sudo bash`，没有修改脚本权限或产物内容。
- 首次安装在替换程序前被HDD策略挡住：`/data/DockerData/whaledeck`仍为1000:1000/0700，而其下仅两个root:root/0700的备份命名空间。保存device/inode/owner/mode，固定父FD/无符号链接验证后，只对这一个命名目录fchown至root:root，模式/inode不变。未递归修改数据或共享父目录。再次安装及验证通过。
- 首个跨盘调用使用错误提交参数，在创建夹具前拒绝；改为直接读取完整Git HEAD。
- 首轮有效跨盘验证已完成文件及目录，但观察器错误要求**私有留存目录**沿用原目录inode。实际引擎按既有协议新建root私有目录、迁移原文件inode。修正为发布目录保留原普通属性、私有目录root/0700、原文件inode保持，未削弱原文件断言。失败现场的源/卷保留，准确测试容器移除；不清扫不明数据。修正后以新独占夹具完整重验三次通过。
- 独立摘要观察器首轮把磁盘原始JSON/默认ASCII转义当作外层协议摘要；协议明确使用ensure_ascii=false规范化JSON。按协议独立重算后吻合。没有改动产品回执或摘要。

## 私有证据位置

位于忽略的 `deploy/artifacts/`：

- `move-rpc-install-53701f8/`：安装前核心、单目录属主修复与707文件独立匹配。
- `container-move-agent-8f1905fbae8947d49ca844730fd4da8a/`：签名UDS、Root与独立文件核验。
- `container-copy-agent-0bbc5e22494f41e1bb66c88498ddac14/`：既有复制安装态回归。
- `container-move-scope-21d1c0cdd0be4e2e9998f282656fef78/`：最终三次跨盘及独立回执/清理检查。
- `container-move-scope-6d0c725b007f43368fb341767f78e241/`：失败观察器与保留清单；未删现场。

## 后续

移动必须接独立永久请求/计划/执行签名、SQL/Outbox/发布互斥、当前权限复核、取消/重启后只观察或封闭、三态事实不丢失、Unknown管理员处置，以及完整API/Vue/实际身份验收。不得借用复制的业务ID、回执或保护屏障绕过移动的源移除语义。
