# 历史与回收站交互验收清单

最新运行时为c60a7c7/Agent1.26，小屏长目录修复已实际部署并签收（见文末）；以下7ece209与候选记录保留为历史，不能当作最新状态。

2026-10-17，运行时7ece209/Agent1.26已通过普通文件历史/回收/恢复/版本清理的真实后端与管理员/普通用户网页子范围，详见文末及`docs/FILE_HISTORY_RECYCLE.md`。实机发现的小屏长目录指针问题已有修复源码，尚未部署。以下矩阵是完整要求，不把尚无证据的行当作通过；全部76项继续。

按`playwright-interactive`同时覆盖需求、实际新增交互及最终拟声明结果；自动化fixture、独立目测和真实身份/工作站验收分开记，不互相替代。

| 用户可见行为 | 功能检查 | 必须目测的状态/尺寸 | 证据要求 |
| --- | --- | --- | --- |
| 历史/回收站入口与文件更多菜单 | 仅管理员及明确目录权限可见；普通/匿名不发送新文件请求 | 初始桌面、320px、明暗 | 截图、请求计数；真实角色另验 |
| 历史列表 | 当前根/所选文件、刷新/续页/空/忙/错误/版本冲突、日期/二进制单位 | 最密集长文件名与状态、错误和空页 | UI正常输入、截图、无水平溢出/遮挡 |
| 副本状态 | Present仅为副本存在而非完整SHA重验；Pending/缺失/损坏不能伪装可恢复 | 全部状态、错误提示 | 断言及目测；真实坏副本另验 |
| 回收文件 | 固定原版本、普通文件/保护目录拒绝、计划后明确确认/取消 | 警告、版本/大小与确认按钮在窄屏均可见 | fixture流程；真实Job/Worker/UDS/HDD字节和审计另验 |
| 恢复与版本清理 | 目标存在/不存在分开、覆盖前备份说明、清理只针对选定HDD版本、不暗示删除本地保留或证据 | 两种目标、破坏性确认，明暗 | 正常点击与真实依赖证明；不操作生产业务对象 |
| 登记冻结与刷新恢复 | 只把非秘密计划ID放路由；从服务端找回同一计划/幂等键；不把Issued当确认；响应丢失不生成新操作 | 计划前、计划后、丢失响应、刷新找回 | 请求/Job计数、原ID一致；不向storage/URL写正文或签名token |
| 放弃未登记计划 | 同事务取消栅栏，拒绝迟到Submit；已登记则显示原任务，不悄悄取消 | 取消确认/网络失败/已经登记 | API及持久层先验，UI循环初始→计划→取消→初始 |
| 已登记任务 | 路由ID刷新查询、运行/成功/失败/取消/目标变化/Unknown、关联任务链接 | 长任务/错误/Unknown、320px | 只查询原任务；取消与Unknown确认不宣称撤销或成功 |
| 并发与撤权 | 上传/编辑/变更/回收互锁；根切换/组件卸载/降权清空敏感状态，晚回复不能复活弹窗 | 多对话框边界、角色切换、等待中 | 计数/取消信号/页面截图；真实普通角色拒绝另验 |

探索至少覆盖：①计划/登记响应丢失与刷新/后退/切根交错；②长路径、320px、缺失副本及404/409晚响应叠加。使用正常点击/键盘进行30–90秒探索，不用evaluate替代验收输入。目测单独检查裁切、横向滚动、遮挡、弱对比、按钮可触达及动态层叠；数值布局检查不能推翻截图中的缺陷。

结束时精确清理浏览器/验证服务及本次测试对象，保留永久任务/审计和真实失败；仅对已执行的行填通过。安装态/真实BFF与页面未完成前，整项保持active。

## 本地已执行证据（不含真实服务器）

- 218单元、type-check/lint/format/生产构建通过。71项既有+新文件联合浏览器通过；17项新归档交互连续两轮34通过。最终regular说明色构建下17项与7项首次授权合计24通过，CI已加入新spec但本次提交尚未跑CI。
- `file-archives.spec.ts`覆盖320/1045/1440明暗、有效/缺失/异常/未完成/已清理副本、回收明确确认、恢复存在/不存在、只清所选HDD副本、Issued刷新不自动确认/取消栅栏、丢失响应原seal/key重试与直接刷新不重发、任务取消/Unknown保留与人工确认、历史续页/409/无效元数据/空、计划中取消/晚回复/403清空、普通直达刷新请求0及只读根所选/全部历史。模拟后端不证明实际权限实现。
- 独立headed检查：桌面1440×1000历史与长名；320×760明暗历史、完整路径/2.0MiB恢复覆盖说明/清理范围/危险色确认。最终暗色dialog实际x12、width296、bottom691；footer两按钮bottom663。历史正文client/scrollWidth280、文档width320。目测无裁切、遮挡、横向滚动或主要按钮不可达；初版淡色说明已提高对比。完整历史按设计纵向滚动，不将不可见下方状态当作初始全部可见。
- 截图（本地gitignored `web/test-results/`）：`archive-interactive-desktop-history.jpg`、`archive-interactive-320-light-history.jpg`、`archive-interactive-final-320-dark-history.jpg`、`archive-interactive-final-320-dark-plan.jpg`及自动矩阵截图。前两张属于提高说明色之前的独立检查；最终暗色和自动矩阵来自最终构建，不混同版本。
- 实际鼠标/触摸式点击探索跨多次短调用超过30秒：关闭/重开历史、恢复计划取消后再清理计划、主题往返和长路径布局。手工错误曾误点隐藏switch input导致超时；改可见外壳。原生窗口、全部错误/Unknown状态独立目测与工作站真实管理员/普通浏览器仍待补，不能仅凭自动断言签其视觉质量。
- 新实际链路脚本已准备，未运行。P2-03/P2-04和全量76项保持active，不能由此签完文件管理。

## 7ece209真实网页与后续小屏修复

- 真正Authentik临时管理员（无route fixture），桌面回收计划/Issued刷新不确认、320px暗色历史和恢复不存在目标、完整42字节独立下载及单版本purge、明色已完成/原文件保留/所选历史为空通过。实际三个Job/root私有回执/HDD删除与SQL审计由独立root观察者复核，并精确清理测试文件；长期记录保留。真实普通用户首次/刷新文件surface0/请求0，历史GET及带真实CSRF/Origin的计划POST403。临时账号/浏览器/凭据已清理；详情和任务ID见主证据文档。
- 真正截图：`archive-real-admin-dark-trash-plan.jpg`、`archive-real-admin-320-dark-history.jpg`、`archive-real-admin-320-dark-restore-plan.jpg`、`archive-real-admin-320-light-purge-result.jpg`、`archive-real-ordinary-320-light-denied.jpg`。结果截图捕获时目录刷新尚在加载，之后独立等待已验证原文件仍显示与历史为空，不用空表瞬间冒充文件丢失/已清理。
- 原生窗口929×917已补正常回收确认、Unknown/保留待调查、历史503/刷新/空与人工接受未知（不冒充成功），截图`archive-interactive-native-dark-plan.jpg`、`archive-interactive-native-dark-unknown.jpg`、`archive-interactive-native-dark-history-error.jpg`；仍是本地协议fixture。最终320明色说明对比也已目测`archive-interactive-final-320-light-history.jpg`。
- 实机指针缺陷：320px固定操作列覆盖长目录button中心；使用键盘可继续不等于指针可用。候选限制button命中区、文本span省略+完整title，新自动用例先检查中心命中再正常click；候选25生产预览/218单元及类型/lint/格式/构建通过。独立候选320/305内容区button x43/w80/right123、无重叠/横滚、图标和省略显示及普通点击通过；尚未覆盖线上，不能把候选截图`archive-long-directory-320-light-candidate.jpg`当作实机修复证据。

## c60a7c7小屏修复实际签收

- CI39020311959的backend/frontend/installer/native-postgres全部completed/success；前端218单元/136联合浏览器，原后端856 Unit/117 Integration和127 Linux基线保持。新目录清单源码不属于此CI或运行时。
- 实际whaledeck-platform-update.service在2026-10-17 17:29:48 CST成功结束（inactive/dead、Result=success、ExecMainStatus=0），不是仅排队成功。宿主安装校验通过，Agent/Maintenance程序集包含完整提交c60a7c7d73874df7197cec2c534a49ce3701207a；11核心容器healthy。
- 新建两个自有空测试目录，真正Authentik管理员正常登录后，320×760窗口实际内容宽305px：父目录按钮x43/y679.1875/w80/right123，中心命中自身、完整title与省略均正确，文档clientWidth=scrollWidth=305。普通点击进入父目录、长子目录、面包屑返回，再切明暗主题和1440×900重复；跨短调用超过30秒探索，无force点击、无evaluate代替输入，文件写请求0。
- 独立目测真实截图`web/test-results/long-directory-real-320-light-c60a7c7.jpg`和`long-directory-real-320-dark-c60a7c7.jpg`：没有全局横向溢出、固定列遮挡、图标压缩或主要动作不可达；省略为预期行为，不靠截屏宽度冒充命中验证。
- FilesView-B0Sj_d8b.js SHA256 `c0eb763963ce2e1085bed517f61530af969e13a0f72c2b346ac9c7d8cfe75c65`；FilesView-CxuTHG2h.css SHA256 `78bb8ae4c44e0774bbedc27cf40a028e83cc866f53453a8c79f8ce2370f325b3`。实际HTTP、活动Gateway和本地已测生产产物三方一致。
- 浏览器正常关闭，临时身份精确删除；测试目录经设备/inode/所有权/无链接/空检查后仅rmdir两个自有目录，不接触业务文件。凭据与私钥引用清空。此前7ece209的完整回收/恢复/HDD/普通用户证据仍单独归属原版，不将本次只读指针复验冒称重新执行全部故障场景。

下一步仍是目录复制移动/回收/归档、容器文件、隔离与长期清理；P2-03/P2-04和完整76项保持active。
