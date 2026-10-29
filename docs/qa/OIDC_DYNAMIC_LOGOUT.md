# 动态入口的 OIDC 完整退出

2026-10-29，运行时 `7c3facd` 的实际回环入口页面验证发现退出故障，修复候选尚待覆盖。59 项继续，17项移出不算完成。

## 实际失败与独立证据

真实 Authentik 浏览器登录、桌面/手机明暗四次文件及目录复制、Ready 明确取消均执行完成。独立 SQL/双任务/审计/Root 原件/完整 2 MiB+7 内容、UID/GID、权限、纳秒时间及版本核对通过；取消无执行 ID、无 Root 开始、目标不存在。
随后另一标签的正常退出停在 Authentik `/application/o/whaledeck-external/end-session/` 的 Bad Request，WD 为401、Authentik仍200，不能签完整退出或整轮成功。独立短时身份再次复现，无复制动作。

现有 API 登录的 ID token 来自固定 Authority 的后端端点，而退出地址改写为浏览器当前入口。安装版 Authentik `EndSessionView.validate` 要求 ID token issuer 匹配当前请求虚拟主机；动态IP、回环或mDNS入口可与固定 issuer 不同。不能用放宽签名/issuer检查、删除提示令牌或只退出WD来掩盖。

第一轮私有证据 `deploy/artifacts/container-copy-browser-69511db3c9b7477f9bf98c27a9c2bb13/`：原始失败、4份 Root 归档、实际页面截图和 `independent-copies-only.json`。失败时准确测试容器已移除，未证实的测试数据按策略保留；不记清理通过。临时身份由原wrapper准确删除。

## 有界修复候选

MaintenanceHost 只对 authentik 固定上游、GET、精确的两个 WD `end-session/` 地址，恢复提示令牌 issuer 的虚拟 Host；同时一致设置 X-Forwarded-Host。字节、URI、nonce/state、回调参数及固定网络目的地均不改动。

提示令牌仍是不可信输入，issuer 必须与维护服务中批准的完整 `Maintenance:Oidc:InternalIssuer/ExternalIssuer` 逐字相同，不能自选信任锚；缺失配置失败关闭。默认值与API现有固定Authority一致，二者是稳定身份issuer，不是监听地址或网卡绑定。若调整API Authority，须同时由管理员配置对应Maintenance issuer（systemd drop-in环境变量 `Maintenance__Oidc__InternalIssuer`/`Maintenance__Oidc__ExternalIssuer`）；不随请求Host自动增加信任。

其余边界：上限32KiB，payload16KiB/深度8；要求三段规范编码、唯一字符串 issuer、同协议/8081、相同 provider 路径、IP或已批准mDNS主机，拒绝用户信息/查询/片段。它仅提供虚拟主机，不参与授权；Authentik继续完整校验签名、audience与issuer。无有效输入时保持原转发行为；不读取POST正文，不增加外连目的地，不修改登录/流执行/其他应用请求。

最终26项针对性测试通过，含真实Kestrel/YARP回环：固定本地目的地收到正确Host与转发Host，路径/query原样，其他API仍保持浏览器Host；覆盖不同provider、IPv4/IPv6/mDNS、错误端口/域名/协议/重复issuer/重复参数/格式/预算/POST。首轮23通过1失败揭示默认转发器在自定义处理后覆盖X-Forwarded-Host；显式先加标准转发器再覆盖后全24通过，随后增加批准issuer不匹配与缺配置门禁，最终26项通过，未放宽断言。

全量 Release 0警告错误，2996 Unit通过、205 Integration通过/7默认原生跳过。日志 `/tmp/wd-oidc-logout-unit{,-2}.log`、`/tmp/wd-logout-{build,all-unit,integration}.log`。这些不是安装态成功；仍需实际覆盖后，验证伪造提示令牌继续400、原身份不被伪造请求退出，以及正常双端退出与四次真实复制整轮。

中间开发覆盖5b8e177已完成，单独新真实身份再次正常退出返回 `/login`，WD401、Authentik403；随后收紧到批准issuer精确匹配的最终候选待覆盖/整轮验收。首轮失败数据在四份Root与完整内容、原父/源身份全部复核后已准确清理，原失败与永久历史保留，见 `cleanup-final.json`；不把清理改记为首轮退出通过。

## 最终安装与真实常规整链

`7b3f44e58750ec4eba58df4b135da36085e5a84d` 正常开发覆盖完成，无自动回滚。发布单元 `whaledeck-dev-publish-7b3f44e.service`，InvocationID `11dfe6f5cf5f41688ed61fab0b9f824d`，终态inactive/MainPID0/success/退出0。六组件及实际HTTP build-info同源，宿主UDS/权限/动态入口/HDD/运行时10.0.13校验通过。

全新临时管理员通过实际可见Authentik登录；伪造提示令牌返回400，原Authentik会话仍200。1440明色文件/暗色目录、320明色目录/暗色文件共四次明确确认复制，每次确认前取消新表单零登记，再实际登记/完整预检/确认；四次准确HTTP登记重试返回同意图，刷新原执行不重发确认。源保留、目标列表可见、浏览器文件正文0；新增一项Ready明确取消，没有执行ID或Root开始。两页正常退出回登录页、原WD Cookie401、Authentik拒绝通过。实际观察退出请求的浏览器Host与令牌issuerHost不同，未输出或保存令牌原文。

独立SQL双Job/Outbox/审计、Root永久原件、完整2MiB+7字节及UID/GID/普通权限/纳秒时间/ETag匹配；普通真实用户8路403、匿名4路401，缺CSRF400/错误Origin403。准确测试源与CID已清理，Root/业务历史保留。新独立进程重核4份Root原件逐字节一致、5意图4成功1取消、活动/租约/屏障0、临时身份0、11核心healthy。主代理已实际目视320明色预检和1440暗色完成截图，无水平溢出，按钮完整；第一轮另外目视1440明色完成和320暗色完成。

私有证据 `deploy/artifacts/container-copy-browser-86e2b2d5009b4ce19809f12927f1cfdc/` 的baseline/results/verified/independent-final、4份Root归档与8张实际截图。日志 `/tmp/wd-copy-real-browser-final.log`、`/tmp/wd-logout-provenance-final.log`、`/tmp/wd-logout-host-validate-final.log`。前序CI40801153829最后已读到四作业success、frontend运行，后续GitHub API多次EOF，未签终态；7b3f44e普通快进推送已成功，不将本地成功冒充新CI全绿。

这证明常规复制和动态入口完整退出，不代表正在复制时取消/撤权、Worker/Agent重启或Unknown跨管理员实际页面都已完成。当前59项继续active，17项移出不算完成。

## Authentik INFO URL follow-up

Bounded privacy checks then found JWT-shaped values in Authentik INFO access events (`event/path/spans[].path`), while Maintenance/API/Worker samples had zero matches. Classification without printing token text identified temporary validation tokens, a forged fixture and signed media tokens; no claim was made about all older logs. The dependency repository changed Authentik server/worker to WARNING, recreated only those exact containers and verified unchanged images/mounts/labels/rotation plus old-object absence. Fresh visible login/full logout passed again, and all new server/worker log bytes through that check contained zero JWT-shaped matches. Database/audit storage was retained. See database-platform `docs/qa/AUTHENTIK_LOG_PRIVACY.md` and private `deploy/artifacts/authentik-log-privacy-20261029/verified.json`. This does not claim forensic erasure or universal future-log sanitization.
