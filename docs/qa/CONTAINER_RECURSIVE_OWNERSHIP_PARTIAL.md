# 递归所有者：真实进行中取消、正常退出与Partial跨管理员处置

2026-11-02。产品保持`b1fafedddfa1767d2d573bd94d2748f3cb5e7b44`/Agent1.50，本批不发布、不回滚。冻结验收源`51e0ec3f6b576eb88cda73e4f489b398c5fa8efc`及21份依赖，以两套新的独占资源完成首项实际fchown后的页面取消、同原账号/实例保持时正常双端退出。两例均首次实际执行通过，原任务Partial、另一管理员真实页面处置、迟到不续做及独立准确清理通过。

这是新增故障组合签收，不代表全部递归所有者/文件模块或59项完成。上一批六场景及其最后退出观察失败、另身份补证仍按[原证据](CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)保留；没有重做那六次操作，也没有把旧Failed改成Passed。

## 精确切点与物理不变量

每例一个新建无网络、非root1000:1000、只读容器根、限CPU/内存/PID、唯一准确自有HDD bind的容器。树含16文件+2目录，每文件完整2097159字节；混合原UID2100..2117/GID3100..3117、文件0640/目录0750、固定纳秒mtime与user.whaledeck-recursive属性。页面明确目标2200:3200，预检18项均预计变更；数字属主与Authentik管理员Subject、容器执行身份分开。

在无其他任务/终端/锁/屏障时，仅短暂停止原Worker以登记原Pending执行，随后启动同一容器/镜像布置观察器，不修改SQL、租约、时钟、缓存、Root回执或产品故障钩子。宿主Agent/MaintenanceHost未重启。

切点只接受原SQL execution ID/计划，准确容器instance PID对应root FD与原操作lock FD两份内核身份；固定root apply transport的完整cmdline、root UID、对应cgroup和pidfd均核对，信号前后再验证。第一项真正fchown到2200:3200且子Root Completed后，用pidfd SIGSTOP保留现场，聚合仍Applying、根目录九字段仍原样。不按进程名/定时猜测或任意PID发信号。

两例均实际证明完整字节、inode、普通权限、mtime、普通xattr与未修改对象九字段不变；仅完成对象的显式UID/GID及必要ctime变化。产品停止helper后不续做，不自动恢复属主。HDD是元数据证明，不是内容备份；源/HDD设备均2049，同设备不计异盘灾备。

## 两个真实页面组合

| 场景 | 可见操作与身份事实 | 产品结果 |
| --- | --- | --- |
| cancel | 320×600暗色先“继续等待”（取消POST0），再“确认请求取消”（原取消POST1）；原上下文随后正常双端退出 | 5.012秒由产品结束准确helper，Partial保留保护 |
| logout | 首项已改时直接关闭详情并从原用户菜单正常双端退出；原账号仍启用、PK/UUID/组完全不变，原容器CID/PID/StartedAt不变，原正权限快照尚余7157.847秒；取消POST0 | 4.339秒由产品结束准确helper，旧任务401、旧region/引用清空，Partial保留保护 |

两例实际五计数相同：1完成核对（包含1实际修改）、1中止、16未开始、0待证明；总对象18，不重复累计Changed。SQL/Root/页面计数一致，原planning Job Succeeded、execution Job Running、原Started/执行ID/计划/历史保留、租约清空但屏障不释放。验收器没有强杀helper；仅pidfd暂停，真正结束由产品取消/登录存活检查触发。

此logout场景发生在原操作上下文内，使用真实BFF与Authentik全导航及最终会话状态验证；不靠账号删除/停用、移组或权限自然过期实现退出。原临时账号是在退出与身份不变证明完成后才按正常finally删除。取消场景的正常双端退出也在原上下文核验，不借用上一批auth-only证明。

## 另一管理员处置与永久证据

原账号准确删除后，新的不同Authentik管理员正常可见登录文件页；320×600明色逐dt/dd检查五类实际计数、HDD历史与最近观察。先“暂不处置”，父进程验证原SQL、实际源、Root完全不变，再允许唯一一次“确认处置并释放保护”。服务仍使用原执行/owner/计划取得永久Discard fence，原Job Failed/Acknowledged；原Root仍Partial，五计数/历史/outcomeDigest、原主体不改，确认人另记。该管理员也完成同上下文正常双端退出。

每例保留1份HDD聚合和1份已完成子项历史；聚合包含18项原计划，Root子项包括完成与中止，未开始项没有伪造子回执。父进程核对清单、原子计划/历史、实际版本与outcome摘要，另一个只读进程重读原SQL/Root、attest HDD与私有原件并重算。明确不声称清理后重新读取已删除源字节。

对同原operation ID另调用一次固定受限helper Apply，返回原Partial/Replayed，源树和原Root字节均不改；这项迟到探针不是签名RPC，不冒称所有传输重放故障已验。原业务登记/确认各一次，文件内容PUT0。

取消例6条哈希审计，退出例5条；原操作仍为原Subject，唯一partial-acknowledged为不同确认人，Detail只有intent/container ID与可重算instance/path哈希，无路径、令牌或密码。每例5张真实320×600 PNG，SHA/尺寸已核对，已目视查看处置确认页，确认/暂不处置按钮完整可见。

## 回执、独立清理与自动化

| 场景 | 私有证据目录（deploy/artifacts下） | 实际终态 |
| --- | --- | --- |
| cancel | `container-recursive-ownership-faults-166cbb1d98794141915f29c072d2832e` | 主门禁Passed，独立只读Passed，5 PNG/2 HDD元数据文件/6审计 |
| logout | `container-recursive-ownership-faults-3cdb333bfd6a4730b8392fec5f3a60f8` | 主门禁Passed，独立只读Passed，5 PNG/2 HDD元数据文件/5审计 |

两套准确来源/CID与临时身份均清理；全部活动任务/终端/路径锁/八类容器意图/租约/屏障及递归所有者transient unit为0。11核心healthy，只有同一Worker容器按预检停启，ID/镜像不变；8数据库平台及两宿主PID/源码不变。没有重复发布、自动回滚或改动开发sudo。

本地新/旧故障与范围联合42项通过；随后补齐CI入口，`python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_recursive_*_probe_scope.py'`实际55项通过，另8项范围通过，Python -W error与Node语法通过。CI仅执行离线边界/语法，绝不自动运行真实SIGSTOP/取消/退出/Root写入门禁。首次适配测试中有一处仍改mode导致1项离线失败，改为UID/GID后全部通过，失败未发生真实操作。

实际主门禁和首次独立观察使用51e0ec3；后续只读观察增强冻结源SHA、cancel精确网络计数/目标UID-GID和第5张计数截图检查，保留independent-first.json后重读两例通过，未重做真实任务。日志在`/tmp/wd-recursive-owner-{partial-first,partial-independent,partial-independent-final,logout-first,logout-independent,logout-independent-final}.log`；证据目录root0700、原始JSON/日志/PNG0600，凭据仅在内存和子进程环境，不进Git/日志、不截图登录凭据。

下一步继续递归所有者Worker强杀/原自然租约、Agent重启/维护入口、实时移组/停用、目标消失Unknown/跨写入保护等组合，再ACL、可恢复删除、长期清理与其余59项/UI/C#酒馆/统一供应商/AI Live2D。工具目前只启用cancel/logout两类真实门禁，不把保留的辅助函数或旧chmod结果算作这些剩余组合已通过。

文档检查：215份Markdown/1304处相对链接无缺失，git diff检查通过；前批真实页面及失败正文逐字保留，只前置后续说明。
