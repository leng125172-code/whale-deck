# 2026-10-29 工作站服务目录属主事故

用户确认上午改属主导致部分容器故障。此前现场sudo journal已记录对HDD根及NVMe数据根的两次递归chown，Agent应用根和部分数据文件变为1000:1000。此文只记录已核实范围，不将容器healthy或临时资源测试扩大为所有历史数据无损证明。

## 已修复与独立复核

- `/data/WhaleDeck/apps` 的750目录恢复既定997:975；Agent原先PermissionDenied，Worker因此采集/心跳失败。修复后Worker自然恢复健康。
- 对11处已确认运行约定的服务数据/备份根，先保存root私有逐device/inode/uid/gid/mode清单，仅修复其中UID1000漂移的546个对象。逐组件O_NOFOLLOW打开、同设备及原inode/mode验证；不删除内容、不改模式、不对整盘递归处理，不触碰项目/个人文件或未知失败证据。
- PostgreSQL/MariaDB为999:999、SQL Server为10001:0、既有Valkey进程为0:0，Whale Deck私有备份为0:0；MongoDB自动启动已恢复的数据所有者保留。宿主用户名映射可能显示dnsmasq/systemd-journal，不能凭名称改回user，容器使用数值UID/GID。
- 修复后再次按原inode清单只读复核546个对象，属主/组/模式无再漂移、原inode缺失0；Docker数据根为`/var/lib/docker`，不位于本次两处递归范围。
- 6类数据库实际临时资源创建、授权、轮换、连接与清理全部通过；48份现存可验证HDD文件归档全内容摘要通过，共1,149,239,768字节。另64条其他/不可验证状态未计通过。
- 宿主validate再次通过：systemd、UDS、受限helper及动态网卡入口。现场11核心healthy；正在执行的验证任务结束后，复制独立检查确认全局活动/租约/屏障0、临时账号0。

证据：`deploy/artifacts/ownership-repair-c5023235d6e64ce7bc12a7e46b436996/` 的plan/applied/backup-verify/post-repair-owner-check；操作器源码位于同级私有artifacts，仅用于本次已登记inode的处理，没有安装产品通用root chown入口。数据库日志 `/tmp/wd-ownership-databases.log`，宿主复验 `/tmp/wd-ownership-final-host-validation.log`。

## 有限故障结论

PostgreSQL曾exit139、内核libc general protection fault，MongoDB同期重启；数据目录属主事故已证实，但不能仅凭此认定139的全部根因。现场4个EDAC控制器纠正/不可纠正计数均0，不代表硬件已全面验证。后续若复发，应保留崩溃证据另查。

期间某个复制故障验收因数据库读取中断而失败，原任务随后Unknown；经不同管理员正常BFF明确Acknowledged释放保护，未改SQL或Root状态。该测试来源还出现owner变更，严格删除门禁拒绝清理；整个准确来源按原Root父inode移入root私有保留区，未删除文件。详见[复制中断失败记录](CONTAINER_COPY_INFLIGHT.md)。

HDD的普通数据库备份根1000:1000不一概改动：既有PG/MariaDB等脚本在宿主侧写重定向输出；SQL Server原生BACKUP写入容器挂载，单独使用10001:0。六类实际备份生成、SHA256记录及后续独立verify任务均Succeeded，日志 `/tmp/wd-ownership-backups.log`，私有 `six-engine-backups-verified.json` 已登记。SQL Server只创建并删除本批临时库，其余备份既有实例；没有执行数据覆盖或恢复演练，不能由备份校验推断全部历史数据无损。

## PostgreSQL健康探针错误身份

有界最近10分钟日志中60次Peer authentication failed来自旧root身份本地pg_isready。实例明确auth-local=peer；手工以gosu postgres执行同一探针成功。数据库平台提交4a4c2df只改healthcheck为CMD/gosu/postgres/pg_isready，无密码和shell。使用相同不可变镜像、环境、挂载、资源/重启策略及保护标签重建单个PG容器，比较数据库system_identifier与原数据库OID/名称/owner目录摘要；其他10容器的ID/镜像/StartedAt要求完全不变。实际单容器覆盖后，连续5次Docker健康检查Exit0，超过一分钟有界日志观察Peer认证失败0。探针重建与日志观测在私有postgres-health-probe.json；该观察不证明未来永不再发，不把就绪探针当作读写或全库完整性检查。
