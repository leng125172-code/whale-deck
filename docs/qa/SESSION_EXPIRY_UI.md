# 实机文件读取与跨标签退出界面

日期：2026-10-22。单工作站59项中的P0-01/P1-11子范围；不签完整文件管理或P0–P5。

## 后续CDP网络终态已补齐，运行时仍0d18dd3

- 新增仅QA用的`browser_download_probe.mjs`，精确固定一个下载路径/请求ID，仅保留状态、长度、累计字节和Chromium网络错误枚举；不留URL/query、Cookie、头部全集或正文。snapshot/timeout/dispose均不冒充终态，没有未处理异步Promise。8项Node原生测试覆盖准确绑定、部分/完成区别、无事件不成功、重复/无关请求、预算/缺失长度、快照不可伪造、错误文本裁剪及监听清理，并接入frontend CI。它的模拟事件单元不是实机证据。
- 正常真实Authentik临时管理员、独占只读/非root/无网络合成容器、实际Gateway/API/UDS，没有API拦截。320暗色页面收到67,108,871字节声明的HTTP200及6,405,456实际字节后，正常点击“取消”；CDP最终receivedBodyBytes=6,412,956、loadingFailed=`net::ERR_ABORTED`、canceled=true、invalid=false。无保存成功。独立SQL该路径Incomplete1/Validated0。
- 取消后不重新登录，正常点击小文件下载：浏览器完整2,097,159字节和独立宿主源SHA一致；同一观察器新实例记录declared/received=2,097,159、loadingFinished（wire encoded=2,097,462，不把HTTP开销当文件长度）、invalid=false。不是只验证下载触发事件。
- 同Cookie的1440明色页面准备正常退出。重新启动大文件，确认HTTP200、声明67,108,871、实际6,942,456字节且未终止后，通过另一页用户菜单退出；原页CDP终态receivedBodyBytes=7,050,456、loadingFailed=`net::ERR_ABORTED`、canceled=true、invalid=false。两页login、returnUrl=/containers、0私有文件区/用户菜单/成功保存，WD401/Authentik403。独立SQL小文件Validated1、大文件Incomplete2/Validated0，与两次不同取消吻合。
- 320×844暗色和1440×900明色退出后截图已实际目视检查，document宽度分别320/1440，无横向溢出、残留管理控件或保存完成文案。原始无凭据网络JSON和截图保存在忽略目录`deploy/artifacts/session-network-real-ui-0d18dd3/`。
- 正常close租约报告源身份/完整摘要/核心快照通过后精确清理，temporaryBrowserUserRemoved=true；新SSH再核对身份0、独占容器/目录不存在、11核心healthy，专用浏览器正常关闭、空私有交接目录删除。没有重启/重建产品服务、改权限、停止其他容器或删除用户数据。

本批补齐的是**页面取消与同源跨标签正常退出的浏览器网络终态**。下方pending是前次失败历史，不再是当前同场景缺口；2小时过期/组撤权组合、跨不同源/Storage受限场景、容器写入/HDD历史/恢复及完整59项仍未签。未依据本次CDP证据修改产品下载逻辑或放宽任何授权。

GLM `glm-5.3-flashx`仅只读定位指定下载函数/测试：41,692ms、input76,983/cache45,504/output2,176 tokens，一次成功无重试、费用未返回。GPT复核定位结果，独立编写观察器/测试并完成全部真实身份/SQL/页面及安全判断。

## 0d18dd3已部署后的真实复测

- 固定更新任务正常完成，未回滚dev。当前实际API、Worker、Gateway、Agent、MaintenanceHost、Migrator六组件及实际HTTP来源均为`0d18dd31f030fb2acad15b91f33e4f9a46839a6b`；Agent协议minor38。覆盖前该冻结源码的隔离真实PG dispatch实际1执行/1通过、cleanupConfirmed=true；不是仅发现TRX文件就记通过。
- CI `39726926076`终态success：frontend/backend/installer/native-postgres四路全部success。当前源码又重跑13项会话失效单元和60项真实Linux-root文件/探针/夹具回归，全部通过；原444/22/2308/160及6跳过仍是下文对应门禁，不重复计为新增测试。
- Playwright独立系统Chrome、真实临时Authentik管理员、真实维护入口/Gateway/API/UDS，无API拦截。320×844暗色从正常容器详情进入work，完整下载2,097,159字节；浏览器消费全流计算SHA与独立宿主源一致。文件面板边界x32.8125、宽267.1875，document宽320无横向溢出。
- 大文件真实HTTP200、Content-Length67,108,871后，另起同Cookie的1440×900明色页面，通过正常用户菜单退出。两页均回`/login`，旧页returnUrl为`/containers`；私有文件区、创建容器、用户菜单和保存成功文案均0。WD auth/me401、Authentik me403。独立SQL按本轮完整容器ID读取：小文件1 Validated、大文件1 Incomplete/0 Validated。手机/桌面登录稳定截图无横向溢出。
- 不发送Storage退出信号的额外兜底：再次实际登录，合法CSRF+Origin的BFF POST logout（不跟随OIDC，故不拿此例证明SSO）返回302；仍在容器页点击正常“刷新”，实际401后回`/login?returnUrl=/containers`。等待旧DOM detached后创建/用户菜单均0；首次只等待URL导致过渡帧计数1，保留该观察，不将过渡帧冒充稳定结果。
- 第二个独立真实普通用户：auth/me200且isAdministrator=false，直达容器页进入`/forbidden`、“没有管理权限”、0创建按钮、0页面容器文件请求；对精确文件target API实际403。通过“首页”及“我的门户”进入普通首页/个人门户，1440稳定布局无横向溢出、仍0文件请求；此例未测试门户创建/持久化。正常用户菜单退出后回login、WD401/Authentik403。
- 两个临时身份均按精确ID删除；独占容器和两份合成文件经源身份/全摘要/核心快照复核后精确清理。新SSH独立复核临时浏览器用户0、该容器/目录不存在、11核心healthy。专用浏览器关闭、两个空的私有凭据交接目录已删除；无用户文件、数据库、数据卷或历史记录删除。

### 未证明的传输事件与工具问题

- 本机首次找不到Playwright捆绑浏览器，下载遇TLS bad-record-mac；没有关闭TLS验证。系统Chrome可用后停止本次下载进程，使用独立无用户profile的Chrome完成实机UI验证。
- 虽然大文件SQL明确Incomplete且UI销毁/无成功，Playwright的`requestfailed`未观察到、`response.finished()`仍pending。关闭上下文使无catch的观察Promise拒绝，Node工具重置。因此**不能签收浏览器网络层即时终态/准确已收字节**，后续需用带catch的CDP loadingFailed/字节探针重验；不能以SQL或关闭浏览器替代此证据。之后新启动普通用户实例正常关闭，独立进程查询无本轮Playwright专用Chrome残留。
- 首次个人门户定位为link超时，实际语义为menuitem；改用正常菜单控件后通过。第一次普通首页截图处于路由模糊过渡，等待有限动画完成后重拍稳定帧，不据模糊图签视觉。
- 忽略目录`deploy/artifacts/session-expiry-real-ui-0d18dd3/`保留手机完整下载、手机/桌面正常退出、401兜底、普通拒绝/首页/个人门户截图及无凭据JSON。该目录名绑定实际被测运行时，不表示后续文档提交已部署。

剩余：上述网络层终态、组移除/缓存过期等完整权限组合；容器文件写入、HDD历史及恢复；其他59项功能继续。退出界面与普通拒绝子范围通过不等于完整文件管理或完整认证模块验收。

本轮GLM `glm-5.3-flashx`仅只读整理任务ID索引：58,370ms、input137,976/cache100,544/output3,128 tokens，1次成功无重试，费用未返回；GPT重新计数确认59项，并负责全部权限判断、真实测试与文档修改。未向外援发送测试身份或凭据。

文档范围回归首轮因元数据使用“17项移出不是完成”而缺少既有精确短语断言失败；恢复“17项移出不算完成”，不删除或放松原断言，重跑8项全通过后提交。

## 已装dd290e9的真实页面证据

- 以实际Authentik临时管理员、维护入口、Gateway、API、SQL及UDS运行，不拦截生产API。Playwright使用正常可见控件完成用户名、密码、首次同意、容器筛选、详情与文件展开。
- 首次密码阶段定位仍使用英文`Log in`而实际按钮已为中文“继续”；明确为验收定位错误，不是身份服务失败。改用实际可见阶段按钮，未绕过认证。
- 1440明色及320暗色各自实际浏览独占临时容器、下载2,097,159字节，并将浏览器完整接收的SHA与独立宿主源匹配。截图已目视检查，文件区和动作没有横向溢出；手机file-panel左右边界约32.8/300、viewport320、document scrollWidth320。桌面实际work目录与下载完成状态、手机完整下载后状态分别留图。
- 真正响应200、Content-Length67,108,871之后，在慢速传输进度29%通过页面“取消”。界面清除忙碌状态、不显示完整校验成功；独立SQL按本轮完整容器ID确认1 Started/1 Incomplete/0 Validated。
- 第三租约在320暗色页面完成面包屑返回根/目录点击/刷新/收起重开；另起同Cookie页面通过用户菜单正常退出。原下载实际requestfailed、无保存成功、私有列表及详情销毁；SQL为该路径1 Incomplete/0 Validated，完整小文件1 Validated。退出页回/login，WD auth/me401、Authentik me403，非模拟SSO。
- 截图在忽略的`deploy/artifacts/container-files-real-ui-ed11963/`：desktop-work-light.png、desktop-root-light-second.png、mobile-work-dark-second.png、mobile-dark-full-download-third.png、mobile-after-other-tab-logout.png。文件名表示本批观察器起点，不表示运行时变更或新的部署。

## 暴露的产品问题与候选修正

上述正常退出后，旧页虽已清除私有容器数据且后端401，仍停留/containers并显示管理员导航和创建容器按钮。不能将服务端拒绝与下载中断通过冒充前端已完成退出。

候选修正：

- 共享API错误边界收到401时立即清空CSRF缓存并通知会话生命周期；不是403/429/503或网络错误，不对写请求自动重试。
- 会话失效清空主体、管理员状态和私有目录提示，受保护路由replace到/login并保留当前站内returnUrl；没有权限缓存时仍由服务端决定访问，未改变2小时权限规则。
- 正常CSRF POST退出导航准备成功后，通过同源StorageEvent发布只有version/kind/随机id的“已退出”标记；不存Token、Cookie、账号或角色，不赋予任何权限。跨标签只做本地清空/回登录，不调用退出写操作；Storage不可用时依然由实际401兜底。
- 初始化与refresh使用本地generation，失效前发出的迟到200不能恢复管理员状态；重复401不重复跳转；可清理/替换监听器，支持HMR。
- 保留原HttpOnly BFF/OIDC退出及CSRF/Origin、禁止本地密码/浏览器Token原则。该标记不替代服务端撤销证明。

## 当前候选门禁与边界

- 全量444前端单元通过（13项新生命周期单元）；lint、格式、类型及生产构建通过。新增正常/失败CSRF导航、401缓存清理、403/503/网络不误退出、迟到身份、Storage边界和监听清理测试。
- 22项生产预览浏览器通过：7既有初始权限、11容器文件、4新增401/503及320/1440跨标签正常退出；使用合成API，不能替代上述实际SSO/UDS证据，也不是候选上线后证据。新增文件已加入CI的显式生产浏览器入口。
- 本机.NET10固定SDK10.0.402 Release2308 Unit/160 Integration通过、6原生依赖明确跳过；当前未新增schema/迁移或后端业务逻辑。
- 本段原候选时尚待冻结PG、覆盖发布及当前页面复测；其后续结果见上方0d18dd3章节。更广故障/组撤权及网络终态仍未签。

## 测试工具失败与清理边界

- 最初500秒租约先到期，后续取消请求503；这次取消无效，不计通过。测试租约改固定900秒、容器最多1200秒；原产品310秒下载/会话策略不变。
- Windows私有stdin桥接曾多带空行/CRLF，读证据时使第二租约提前结束；新增准确协议回归，仅允许空分隔/固定evidence/close，不接收命令或路径。身份精确删除；保留的合成源另经完整确定性重算、已知小文件SHA、目录/文件元数据及准确清单核验后精确清理，私有0600证据保留。
- 新异常清理回归暴露夹具污染全局umask，已改为每个对象显式权限、不修改调用方umask；不降低750/640等原断言。9270c0f实际Linux-root60项全部通过；异常body只有源完整身份/摘要与核心快照仍匹配才清理，否则保留已变化证据。
- 第三租约正常close实际报告fixtureBodyCompleted/verifiedPayloadRemoved/temporaryBrowserUserRemoved均true；源码运行时无重建/重启。最终全局残留仍需新SSH复核，不以这些单次回执代替。

GLM只做机械定位/测试计数，权限与修复均由GPT复核实现。最初Flash定位调用122334ms、input254159/cache215168/output2837；用户更新额度后停用Flash。本批FlashX计数调用32337ms、input89140/cache61696/output1321，一次成功无重试，费用未返回；其“11单元”对应新增两例之前，当前实际13项，以真实测试结果为准。
