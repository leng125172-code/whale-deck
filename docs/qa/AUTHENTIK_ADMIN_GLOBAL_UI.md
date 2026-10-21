# Authentik 管理员 Logo、工作背景与原生全局属性验收

日期：2026-10-21。源码 `131c5fb`，最终样式 `f788a1e`，已应用到工作站。
这是外观子阶段，不代表完整 P0–P5 / 76 项完成；项目仍 active。

## 需求与验证清单

| 用户可见要求 | 实际验证 |
| --- | --- |
| 管理员 Logo 不再过小 | 实际组件为 `ak-page-navbar .brand .logo img`，135×36 →224×59.71875；SVG 比例保持；不放大头像或应用图标 |
| 管理/门户背景减干扰 | 管理壳层、独立 Shadow DOM 的 main-section/card/table、页头/侧栏统一不透明工作面；浅色底 #f7f9fc、白色面板，深色底 #111820、面板 #1b2430；原生状态/告警颜色保留 |
| 门户去掉斜切装饰 | 原生背景属性生效后 `background-default-slant` 数量 0；实际门户背景分别 rgb(247,249,252) / rgb(17,24,32) |
| 原生属性实际使用 | 新临时用户 `/core/users/me/` 200，实际 settings 包含 zh-Hans / automatic / theme.background / 2-column 四项；不只检查写入请求 |
| 登录原图保持 | 独立匿名上下文 HTTP200，完整 1,565,840 字节、SHA256 bc82bb3b2c685fbe35fe1b58b15b0b7049c67b2efdfbd02aa07134937a3daf6e 与用户原 JPG 一致；320 深色及1440 浅色实际登录页截图检查 |
| 响应式与主题循环 | 管理页1440明暗、390/320浅色；门户1440明暗、320明暗；门户160×42.65625 Logo；管理页320及390、门户320均 scrollWidth=innerWidth；系统主题 light→dark→light 生效 |
| 交互无破坏 | 普通页面导航、门户搜索输入/清空、后台侧栏展开/收起均正常；原生注销后用户名输入可见，me 200→403；不以删除 Cookie 充当退出 |

探索检查：后台窄屏原生 navbar 固定多列曾撑宽到592.875px，已改成标题/工具两行，320px无横向溢出；默认同步重复执行保留用户壁纸且无重复写入。手机后台原生在1210px以下隐藏侧栏品牌区，本次不强制额外塞入 Logo，菜单和工具保留。

## 属性所有权

仅管理 `settings.locale=zh-Hans`、`settings.theme.base=automatic`、
`settings.theme.background="background: var(--wd-page-bg);"`、
`settings.layout.type=2-column`。详见 [配置及等价 YAML](../../deploy/branding/authentik/README.md)。
用户已有偏好仍可覆盖品牌默认值。实际登录页中原流程配置的英文标题/按钮不因 locale 自动翻译，本次不修改认证流程。

同步器深拷贝/合并四个叶子键，不整体替换 attributes；未知键、policy、feature、权限属性保留。非对象父节点在媒体写入前拒绝；PATCH前完整非派生快照一致性检查，PATCH后未托管配置相等且完整期望属性相等。流程、Provider、组与入口不变。

官方依据：[Brand attributes](https://docs.goauthentik.io/customize/branding/)、
[Admin settings](https://docs.goauthentik.io/customize/interfaces/admin)、
[User settings](https://docs.goauthentik.io/customize/interfaces/user/)。当前运行2026.8.3，已用实际返回设置与渲染验证支持，而非仅依赖最新版说明。

## 自动化、应用与清理

- Windows 及工作站 Python 原生品牌同步测试各34项全部通过；包括属性保留/父节点错误/并发修改/服务端漏写、旧媒体验证与壁纸幂等。
- 实际首次应用变更只有 attributes/CSS；最终f788a1e应用只有CSS；最终再执行 changedFields=[]、identityChanges=false、backgroundPreserved=true。
- 最终CSS SHA256 `9f09153817c470c3b18d7bf8670ecbae04a4847ba17064f9adbbfab7f5d1744f`。Logo/图标素材未重绘、未重复上传；未构建/替换任何运行镜像或重启宿主服务。
- 实际最终11核心容器全部healthy，Agent/Maintenance两服务active，Whale Deck首页200。
- 三次独立短时测试账号：前两次在延长检查时到期自动删除，过期后的登录/导航失败不计入通过；第三次最终完成正常登录/明暗/导航/退出。最后精确终止该账号通道，finally确认删除；SIGTERM触发KeyboardInterrupt退出1是预期清理结果，不冒充退出0。独立API计数剩余0，自有Chrome关闭、临时密钥/凭据清除。
- 截图位于忽略的 deploy/artifacts/authentik-*.jpg，仅本地，可能包含真实事件，不上传展示站/Git。最终后台截图等待sync/outpost canvas可见且事件已加载；早期spinner截图不作为最终验收。
- f788a1e CI39528203593、131c5fb CI39527083271在最后检查仍in_progress；不声称全绿。前一d7a991f CI39524831389已success。
- GLM Flash只读定位尝试1次，模型访问繁忙无有效输出，路由耗时2310ms，input/cache/output/费用未返回；没有据此给出结论。实现、检查及最终验收由GPT完成，无第三方凭据/远程私有配置传输。

## 主线保持

三范围清理面板8个工作副本文件保留，未混入本次提交/部署。39聚焦单元通过、19浏览器用例仅登记的原证据不升级。后续恢复最终类型/构建/完整前端测试、真实BFF/UDS/HDD/权限验收和dev发布；继续完整76项目标。
