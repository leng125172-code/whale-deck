# 独立恢复：执行中实时移组与账号停用

2026-11-06后续：另两轮全新来源的Worker强杀/自然租约与Agent重启已分别实机验收，来源未恢复，见[重启记录](CONTAINER_FILE_RECYCLING_RESTORE_RESTARTS.md)。以下保留当时的撤权边界，不混用触发原因或补签其余故障。

2026-11-06，冻结工具 `146ce5f74eeee1a803e958c26c9e229f9d0ee365`，保持实际部署3d80d17。两个场景依次使用不同的新容器、来源、临时真实管理员及独立观察管理员；每轮清理和独立复核完成后才开始下一轮。不重放旧任务，不用任务取消、退出、时钟变更或数据库编辑代替身份撤权。

两轮unit均exited/0，清理前/后独立进程各passed。4项新专项结合身份变更范围、既有撤权/退出/切点/协议/原件检查共87项在Linux root通过、零跳过；8范围和Python/Node语法通过。只签准确Applying写入前的两个身份故障组合，完整故障矩阵与59项交付仍未完成。

## 真实操作与结果

| 场景 | 身份服务确认的唯一变更 | 原会话与页面 | 原pidfd退出观察 |
| --- | --- | --- | --- |
| 移出管理组 | 本测试账号groups由1项变0，is_active保持true | 原任务GET403；auth/me200、同Subject且非管理员；真实/forbidden“没有管理权限” | 2.950876秒 |
| 停用账号 | 本测试账号is_active由true变false，groups保持原1项 | 原任务GET403、auth/me403；真实/login“使用工作站账号登录” | 2.876897秒 |

计时均始于身份PATCH前，包含变更及确认读取，不是只测PATCH响应。用户名匹配生成式测试前缀，Subject/数字PK/原管理组逐一核对；每个对象只允许一次限定PATCH，响应不确定不会重发。撤权PATCH只作用这两个测试所有者，未撤权观察管理员或其他用户。

页面先创建并确认回收全新1 MiB+7字节文件，SQL/原Root/实际留存inode及HDD证明通过后，才新建恢复并确认。固定restore helper由MainPID/Invocation/cgroup、确切argv及容器root/恢复锁/原来源锁三个内核FD绑定，暂停在Applying、来源claim匹配、原件仍留存且目标缺席的位置；父进程另行重开当前HDD数据/原件核对，再执行身份变化。两轮均证明身份变化前与服务端确认后原helper仍存活，执行到期距确认超过10分钟，随后才由原pidfd观察退出。

原用户取消POST0、退出POST0；SQL cancel_requested_at_utc为空、显式cancel-requested审计0。两轮永久意图及执行Job均Canceled、恢复回执Aborted，结果审计正常存在，不能把“取消审计0”误写成所有审计0。Root claimed/historyVerified均true是历史核验事实，当前来源claim已清除；原来源仍Trashed、sourceRetained=true/sourceRestored=false/restoredByThisOperation=false。Outbox0、写入保护释放、租约清空，来源Root/HDD及留存原件不改写为Restored。

## 页面、隔离和清理

各原会话刷新后管理面板与恢复对话框缺席，回收和独立恢复两个sessionStorage提示命名空间均清空。两轮各2张真实320×600明主题PNG，记录失权/登录页和另一管理员查看的原Job；没有模拟身份响应或注入Cookie。

每轮另一名真实管理员正常可见登录，只读RecoveryGet200和准确take=100原执行行，检查17字段独立恢复安全摘要、原来源引用及本操作未恢复，管理写入0，再正常BFF/OIDC双端退出。观察者不同于被撤权主体，不借恢复登录执行确认/取消/ACK。普通用户5 GET/4 POST均403、匿名5 GET均401；每轮三个临时身份均精确移除。

独立进程在清理前重读SQL/审计、原Root、claim缺席、来源Root/HDD元数据/数据以及实际留存inode；确认源路径缺席、字节/属性/UID/GID/模式保持，才精确删除本轮自有trash、空目录和容器。没有产品恢复操作；清理后再独立重读永久历史/HDD并证明资源缺席。所有旧失败和本轮历史保留。

## 失败初稿、保持与边界

初稿Node静态检查发现第二管理员读取结果的response变量与预检变量重名，SyntaxError，尚未发生任何真实身份/业务操作。已保存原MJS与失败摘要，修正命名后再完成87项检查和两轮真实门禁；不把初稿错误藏成首次全绿。私有预检目录为 `restore-revocation-precheck-c51cf432f74345f3abf5f249ae52c98b`。

两个unit：移组 `whaledeck-qa-restore-revoke-group-first` / Invocation `9ffc8063c1354f7bbc2717a4b368518e`；停用 `whaledeck-qa-restore-revoke-disable-first` / Invocation `7dacfd2c9bf34e6a87482676e26a3da8`。2346保护、11核心、应用及两宿主3d80d17保持，无发布或服务重启。每轮其他来源/恢复意图的完整行SHA保持；首轮前20来源/6恢复，次轮前21/7，最终新增2来源/2带执行Canceled恢复、活动0。

递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root40→42→44/HDD43→45→47，独立恢复Root15→17→19，仅两新执行的json/lock新增，当前来源claims均已清除。源与HDD同device2049，不是异盘灾难恢复；本批没有维护连续性采样。

继续Worker/Agent重启与自然租约、写后/Unknown/实例变化/跨写入、长期清理和其余59项。17项移出不算完成；25%–35%仍是粗估，整项0/59、阶段0/6、酒馆/供应商及Live2D功能0%保持。

私有封存均位于deploy/artifacts：移组 `container-recycle-restore-revocation-2b19b075e67548229e7b823434a5d5df`，150文件/109源码，`REVOCATION_SHA256.json` 摘要 `95658aff3c579ce3d6f38b29435321e1ac80ea06780111cf4153c5080e3a8dad`；停用 `container-recycle-restore-revocation-cfd1dc7388c94a038c9b092e076309d1`，150文件/109源码，摘要 `c944ffce551ad5a17c25e62dacd809f022f7ecacc92f6e4e1cbd96102fc37158`。初稿2文件的 `PRECHECK_SHA256.json` 摘要 `57d28ac5fc063c964fb2a19e2ed40205398ed0ac7ec4bf47fa89b051a02b1ce2`。凭据、私有配置和身份快照不入仓库或图谱。
