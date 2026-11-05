# 容器普通文件回收：安装态签名 RPC 与 HDD 分项验收

2026-11-05，承接[协议候选](CONTAINER_FILE_RECYCLING_RPC.md)及[原生探针](CONTAINER_FILE_RECYCLING_PROBE.md)。产品源码 `3725aa752316ba933a850376786119e528248815`，探针 `b7fc8e334c59d4d55860f8bc8382a47d989a1870`，安装态父门禁与独立校验冻结 `c4eae2d4d3bdea501e9aafbf88af516ad72a4b0c`。本批完成标准宿主安装及两个新操作的实际签名 RPC/文件/HDD 分项，未完成永久业务与页面整链。

## 安装与保持

准确产品提交的 CI `41997949781` 已 12 路 success；运行目录从 `34153d2` fast-forward 到 `3725aa7`，通过标准 `publish-and-install.sh` 从 Git archive 和固定 SDK digest 发布两个自包含宿主，发布包 `host-publish.GBBpQP` 的完整 SHA256SUMS 校验通过。单次安装 unit `whaledeck-qa-recycle-install-8638432b08ed`、Invocation `dd3c5c9ce70b41fe92b807d7780bd1d9` 已 exited/0；未重放安装。

Agent/MaintenanceHost 实际 `/proc` 可执行文件、DLL 映射与磁盘文件一致，均为 `3725aa7`；安装清单验证通过，59 个已安装 helper 内容与该提交一致。API/Worker/Gateway 的实际程序集/静态元数据与 HTTP 仍为 `34153d2`，这是本批明确的宿主升级范围，不是所有组件同版本交付。没有执行生产数据库迁移。

安装前后 11 个核心容器 ID/镜像/StartedAt/健康保持；2346 个保护文件内容不变，仅安装清单允许的 sudoers/resources/agent.env/能力 key 元数据变化，key 除 ctime 外身份不变。开发用户 NOPASSWD:ALL 保持，生产 Agent 授权未扩大为任意命令。来源 Root 1964、恢复 Root 257/HDD 169、目录 Root 94 与普通递归 ACL 62 意图/56 执行/118 Job、恢复 24 意图/19 执行/活动 0 保持；旧回收 HDD 五文件也逐字节保持。

维护入口以两秒间隔实际采样 71 次，均 HTTP 200，观察约 140.5 秒。此采样不证明瞬时零中断，亦不代替外部网卡/跨设备连续性测试。安装 unit 的 `RuntimeMaxSec` 被 systemd 明确提示对 oneshot 无效，原日志保留；本次实际成功退出，不据此声称安装超时保护有效。后续新门禁使用 `TimeoutStartSec=600`。

## 新资源签名 RPC

只创建一个受限、自有的无网络容器及固定 `work/recycle-rpc.bin`，普通单链接文件 1,048,583 字节、1000:1000/0640、固定 mtime 与一个原始字节 user 属性。没有操作旧回收记录或业务目录。原生探针经实际 UDS/能力响应/签名调用到安装态 Agent 和固定 helper；规划 17 项与操作 14 项拒绝均通过，未生成被拒绝操作的 Root JSON。

| 新操作 | 实际调用顺序 | 每步回执与独立证明 |
| --- | --- | --- |
| 写入前取消 | Discard → Inspect → Apply → Discard | 四次 Aborted，源文件完全不变，私有目录与此操作的 HDD 历史均不存在；迟到 Apply 不执行回收 |
| 正常回收/恢复 | Apply → Inspect → Apply → Discard → Restore → Restore → Inspect → Apply → Discard | 前四次 Trashed、后五次 Restored；同状态重放回执、Root/HDD 及实际文件保持，Restore 使用唯一且不同于原操作的新 job |

合计 13 份操作回执、17 帧探针输出，正常退出、stderr 为空。Trashed 时原路径缺席、同 inode 位于 root:root/0700 私有目录；Restored 时原路径的 inode/权限/所有者/mtime/字节/属性与原件一致，允许重命名导致的 ctime 增长。每步保存原始 Root/HDD JSON，并实际读取 HDD 二进制原件的完整大小与 SHA256；不能把覆盖备份当作 Trash。

HDD 经过实际挂载/设备证明，但本文件 `differentSourceDevice=false`：不签 NVMe/HDD 异盘隔离或灾备效果。Root 从零增加到四文件（两个操作的 JSON/锁），回收 HDD 从五增加到七文件（新正常操作的 metadata/data，既有 history.lock 保持）。已验证历史保留，没有删除历史来获得干净结果。

独立进程在清理前重新读取规范 Root/HDD 原件及当前源文件，并核对容器身份/挂载；通过后才精确删除本次测试文件、已证明为空的私有目录和自有容器。清理后独立进程重读保留历史、核对原基线和测试资源缺席。后置证明明确不声称还能重读已删除源文件，源字节证明来自清理前步骤。核心/既有原件/业务计数与宿主身份全过程保持。

## 校验修正与证据

安装前发现通用快照会把 `.data` 当 JSON，已改为有界二进制读取和完整元数据/摘要校验，新增八项临时 Linux 文件/保留边界测试，与既有十四项共 22 项通过、零跳过；另八项范围检查通过。临时文件测试不是安装态证据，实机结果由上述独立门禁提供。

安装后首次后置校验错误地要求所有 helper 都是 0750，实际 `whaledeck-host-terminal.py` 按既有安装清单为 0755。保存原校验源码/失败位置后，仅修正这一明确例外并重核，未重新安装或放宽新回收 helper 的 0750 要求。旧封存初次核验误将 `{sizeBytes,sha256}` 条目视为字符串，按原格式修正后原 23 文件清单摘要及内容均保持。 文档收尾时元数据遗漏固定范围句“17项移出不算完成”，范围检查明确失败；恢复该约束后重新检查，不调整范围测试。

私有封存：

| 范围 | 目录 | 文件数 | EVIDENCE_SHA256.json 摘要 |
| --- | --- | ---: | --- |
| 标准安装/原失败/维护观测/实际版本与保持 | `container-recycle-install-6ea7ab8e17c84e36a9eb968f58d9554d` | 25 | `a44ae278adc92855ff305c4d99d558ed3c4bc0b74d6b953a85151358cf522f11` |
| 新资源 RPC/原件/清理前后独立证明/冻结源码/测试 | `container-recycle-agent-e6e8d72f03914e6cbce4066ae3d5040f` | 141 | `a906967834a199223f816eb67e5d62907809fa305f619343e62fe3ac20d836b2` |

## 未签范围与下一步

本批取消是写入前 Discard，不是正在运行 helper 的精确停止；旧域的完整签名 RPC 兼容、实际中断/撤权/恢复进程重启、Unknown 原因矩阵、跨业务共享写入保护仍需另验。当前只支持普通文件；ACL/安全属性/目录、保留原 inode 丢失后的 HDD 物化恢复、异路径/覆盖恢复、长期保留和彻底清理没有由本批实现或签收。

下一步接永久回收/恢复意图、授权和状态机、PG/Outbox/租约与共享写入保护，再 Worker、管理员 API/Vue 及真实 Authentik→页面→文件/HDD 整链。P1/P2 对应主任务继续“部分实现”，不增加 59 项完整签收数；整体 25%–35% 仍是粗估，酒馆/统一供应商 P4-08 与 Live2D 看板娘 P5-05 功能进度仍为 0%。
