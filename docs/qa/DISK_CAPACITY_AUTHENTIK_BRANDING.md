# 磁盘容量修复与 Authentik 品牌验收（2026-10-20）

## 验收库存（执行前）

| 用户要求/可见状态 | 功能检查 | 独立视觉/事实证明 |
| --- | --- | --- |
| 首页磁盘不再永久100% | Agent真实新采样排除loop/squashfs/虚拟FS，保留真实满盘、USB和网络盘；首页/API与宿主一致 | 实机Agent mount namespace的df对照；更新后首页/新采样，无捏造0 |
| 登录品牌 | 默认Brand仅外观字段更新，Logo/图标/背景/CSS持久化且匿名可达 | 1440明暗、320手机明暗初始页、用户名与密码步骤；图片无变形、按钮和焦点可见 |
| 新生成图片 | 内置imagegen，单鲸、自然双胸鳍和尾鳍；最终图进入仓库及Authentik媒体 | 完整生成图独立审视；生成提示词/方法与资产路径记录 |
| 主题与窄屏 | 系统明暗偏好；320/390/1440无横向溢出、主按钮可达 | CSS像素截图及主要控件bounds；辅助操作不被遮挡，reduced-motion保留 |
| 登录与门户不退化 | 临时真实身份完整登录、门户可见、正常退出；OIDC/provider/组/登录flow字段不变 | 外观PATCH白名单及前后非外观字段对账；实际浏览器流程和既有OIDC校验 |
| 可重复部署 | 内容寻址媒体/完整下载哈希；二次运行无重复修改 | 固定脚本测试、两次实机回执；不删除旧媒体、不依赖WD提供背景 |

探索场景：空用户名表单验证/错误输入状态；窄屏键盘焦点与缩放/刷新；明暗切换完整返回。正常输入探索30–90秒，与独立视觉验收分开，不用DOM注入点击冒充使用。

## 已确认根因

宿主普通shell df：根ext4约27%、数据盘ext4向上取整约1%。进入实际Agent服务的mount namespace后发现15个只读`/dev/loop*` squashfs/snap挂载全部100%；原采集不区分真实容量，首页再取最大值，导致永久100%。这不是两次乘百分比，也不是磁盘I/O忙碌率。

## 实际发布与磁盘验收

- `6a6f8e25873639df93611c92c6aed49d1045fd41` 已推送dev；CI39393219440的backend、installer、frontend、native-postgres四作业全部success。前序7d2bd38的四作业也已独立确认成功。
- 实机GitHub拉取因GnuTLS断流失败，改为验证Git bundle后fast-forward同一提交。Gateway仅以Git archive冻结的公开源码构建；私有env与images文件不进入上下文。新Gateway镜像manifest list为`sha256:5800c4f2d32ab33f3541b631c68b5966bfaabf29499710ebe9eb60af8887ad79`。
- Agent/MaintenanceHost独立冻结发布并实际安装；宿主在位校验通过systemd、权限、UDS gRPC、固定helper、Docker访问和动态入口。安装态自带运行时.NET10.0.13/PKCS10.0.13通过校验；Agent build-info为6a6f8e2，协议1.34。API/Worker仍为先前18f38bb，不冒称六组件统一版本或覆盖恢复业务已上线。
- 最后在线时11个核心容器全部healthy。本批没有重建数据库容器、变更数据卷或运行完整身份provisioner。
- 真实临时管理员经用户名/密码、Authentik门户、OIDC显式同意进入WD；`GET /api/v1/overview`返回200，新的Good容量样本：`/`26.8969%、`/boot`14.2111%、`/boot/efi`57.3922%、`/data`0.2769%，不再含snap/loop/overlay/tmpfs。
- 浏览器实际状态卡为“硬盘使用率57.4% / 当前最高文件系统占用”，与当前最高真实EFI挂载一致；不改变原有“最高有效挂载”语义，不捏造合计或0。真实满盘、USB、网络盘保留；32项磁盘/批次专项和完整1493 Unit通过，Release零警告/错误。143 Integration通过，6项本机原生依赖明确跳过；真实PG执行证据来自本提交CI，不把本机skip算通过。

## 品牌发布与浏览器证据

- 已生成/发布原生鲸鱼海洋背景、复用透明标识并组合Whale Deck wordmark、favicon、native Brand CSS；完整提示词与生成方式见`deploy/branding/authentik/README.md`。
- Gateway默认1MiB上传限制造成首次413；只在Authentik server配置增加有界8MiB后重新构建/替换Gateway，保持只读rootfs。尝试直接复制配置被只读rootfs拒绝，没有解除保护。
- 默认Brand仅PATCH五个外观字段，非外观字段前后相等；媒体内容寻址，无覆盖/删除旧媒体。Logo和favicon使用独立匿名Session完整SHA验证。背景已被真实浏览器渲染，但尚未完成其独立匿名全字节SHA验收，不能把返回的本地backgroundSha冒称远端下载证明。
- 实机发现DRF裁剪CSS外层空白导致初次严格验证失败；已在比较/hash前规范化`.strip()`。随后连续两次实机verified且`changedFields=[]`。最终手机及密码步骤修正后CSS实机回执SHA为`c864c30df21217764a283b572f94fd441427eec1e531877bfa282a3e8525f5b7`，其后未再更改CSS。
- 1440明/暗初始页独立截图已观察，暗面板为`rgba(15,29,50,.94)`；320明色已修官方语言选择器横向溢出（document/flow/login scrollWidth均320、locale右界288），保留动态背景，不把短期signed URL写入CSS。
- 真实长用户名密码步骤发现static form固有宽度撑开；修正后320视口密码输入右界277.40625px、高44px，真实密码登录、Authentik门户及WD OIDC跳转成功。空用户名被浏览器验证拒绝，键盘焦点可操作；没有用模拟接口替代实际登录。
- Powered by authentik和语言选择保留。手机页脚对比度已修并实际写入，但最后独立视觉复核、最终320暗色/390/短窗口及背景匿名全字节校验尚待恢复在线后完成。
- 本轮正常退出检查尚未完成，不签成功或据此断言新的注销缺陷。临时账号通道已返回`temporaryBrowserUserRemoved=true`；本轮自有浏览器已关闭，私有内存已清理，无账号/密码写入仓库。

## 本地收尾与后续

- 独立品牌脚本纯fake HTTP测试26/26通过，包含默认只读、五字段白名单、非外观/并发保护、恶意CSS/SVG预上传零写入、无外部认证转发、匿名媒体hash、幂等及DRF裁剪。没有真实网络/token参与这些单测。
- GLM `glm-5.3-flash`仅只读检查CSS/wordmark，GPT复核并负责实际修改和发布。一次成功、零重试，input25303/cached256/output932、56948ms；平台未返回费用，不推算实际扣款。GLM提示的窄屏/暗色风险由实机验证决定，未直接作为结论。
- 2026-10-20用户通知工作站离线：立即停止工作站请求/部署及重连，等待用户恢复通知；继续本地实现、测试和仓库更新。完整76项目标仍active；不把远程子步骤的暂停作为整个目标暂停或阻塞。
