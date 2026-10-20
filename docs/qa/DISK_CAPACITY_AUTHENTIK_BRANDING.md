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

### 恢复在线后的最终验收

- 用户明确通知恢复连接后才重新请求工作站。启动时间2026-10-20 18:08:34 CST；11核心容器healthy，Agent和维护服务active、NRestarts均0，WD `/health/live` HTTP200。Agent仍6a6f8e2/1.34、API/Worker仍18f38bb，不冒称本地新业务已部署。
- 最终CSS SHA为`0cb423bd901d0d1f63580c4ea969e6e9ca09193f9813bf6256db3543bdef1afe`。补语言控件阴影、独立header定位/最小高度，解决短窗口与Logo重叠。发现真实系统主题动态切换会由FlowExecutor用流程默认背景覆盖body变量；使用仅在HTML解析的背景别名继承，保留原生动态URL，不硬编码signed URL、不改登录流程。1440明→暗→明→暗循环背景均为鲸鱼图。原因参照[实际版本原生主题同步代码](https://github.com/goauthentik/authentik/blob/version/2026.8.3/web/src/flow/FlowExecutor.ts)。
- 独立匿名新浏览器上下文完整下载实际动态背景：HTTP200、1,735,237字节、SHA256 `13b32257811f9a9ed11baf4e8414c426205a9d8accb56bec6526836bfd7b1ee3`，与源图全字节相等；未向下载携带管理凭据。此为补齐证据，不再使用前序“待校验”的状态。
- 使用playwright-interactive保持自有headed Chrome会话：最终320×780明色、390×844暗色、740×360短窗口、1440×900明暗截图均独立审视；document scrollWidth等于viewport宽，语言控件与主卡无重叠。短屏为正常纵向滚动，主按钮44px且可滚动到可见位置。320暗色也已独立审视，密码步骤正常输入/按钮可达，辅助页脚与语言图标可辨；不隐藏官方署名。
- 最终截图保存在忽略目录`deploy/artifacts/authentik-final-*.png`，不含凭据。视觉与正常操作探索分别完成：空用户名被原生校验拒绝、键盘焦点、窄屏/短屏滚动、明暗完整循环；未发现最终状态横向溢出、Logo畸变或主按钮遮挡。
- 新临时真实管理员通过用户名/密码、Authentik门户、WD OIDC显式同意进入工作台，overview HTTP200。正常用户菜单退出实际发生CSRF GET200→logout POST302→Authentik invalidation流程→WD `/login`；WD me401、Authentik me403，再打开身份门户出现用户名输入。确认两端会话均结束，不以重载或删Cookie冒充退出。
- 加密临时身份通道完成后返回`temporaryBrowserUserRemoved=true`，自有浏览器关闭、私有身份内存清除。最后发布仅Brand CSS变化、identityChanges=false；未修改用户组/OIDC/provider/flow、数据库卷或既有用户凭据。
- 外观脚本26项纯fake测试再次通过。后续仍继续完整76项目标，品牌子阶段签收不表示整个项目完成。新增覆盖恢复业务基础见[独立材料记录](FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md)。

- 独立品牌脚本纯fake HTTP测试26/26通过，包含默认只读、五字段白名单、非外观/并发保护、恶意CSS/SVG预上传零写入、无外部认证转发、匿名媒体hash、幂等及DRF裁剪。没有真实网络/token参与这些单测。
- GLM `glm-5.3-flash`仅只读检查CSS/wordmark，GPT复核并负责实际修改和发布。一次成功、零重试，input25303/cached256/output932、56948ms；平台未返回费用，不推算实际扣款。GLM提示的窄屏/暗色风险由实机验证决定，未直接作为结论。
- 2026-10-20用户通知工作站离线：立即停止工作站请求/部署及重连，等待用户恢复通知；继续本地实现、测试和仓库更新。完整76项目标仍active；不把远程子步骤的暂停作为整个目标暂停或阻塞。
