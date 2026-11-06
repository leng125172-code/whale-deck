# 归档复制：真实身份、可见页面、SQL/Root与当前文件/HDD常规整链

2026-11-07，在已发布产品f318d9ab上，冻结验收源码 `788676be11f5458f5fac1a314bab5f87247f8ca1` 完成一次全新资源常规整链。承接[标准发布](CONTAINER_FILE_RECYCLING_ARCHIVE_RELEASE.md)，没有重复更新、重放旧操作或手改DB/Root状态。只签本批正常复制和Ready取消组合，其他执行中故障、撤权/重启/Unknown、来源故障和长期策略仍待。

## 真实操作与逐阶段放行

使用固定预算、无网络的本轮自有非核心容器及1,048,583字节普通文件，原1000:1000/0640、mtime及user属性作为物理基线。真实临时管理员经Authentik可见登录，Vue完成原回收、清理Ready取消、新清理明确不可逆确认，再从成功清理意图选择HDD来源、归档Ready取消、新归档明确确认复制为新文件。原回收/清理与两个新归档意图/执行域独立；不接受客户端计划、owner或物理路径。

父进程在source/cancel/ready-to-purge/final-confirm/success/archive-cancel/archive-ready/archive-final-confirm/archive-success九个标记后，分别读取SQL、Root、原件/新件和HDD，再写accepted放行。归档最终确认前另核当前purge Root/HDD及原业务/保护保持，不能只沿用Ready缓存。两个确认框取消都无写入；Ready取消无executionJob/Root预留或Apply，不冒充执行中取消。

真实浏览器没有route/fulfill、注入会话、导出storageState或原生Apply；新任务登记、确认和取消经可见按钮完成；清理阶段另以HTTP context测试同键Ready幂等登记及CSRF/Origin拒绝，SQL确认没有新执行，不能把这些探测称为全由按钮发起。生成的临时密码仅经本次Popen私有stdin传给浏览器，未进argv/环境文件/证据；未记录登录页截图或浏览器trace。明暗320px页尾与无横向溢出、实际Jobs详情及稳定原引用均由当前可见页面验证。退出后归档GET401，真实重新登录仍同subject但旧Owned/by-key共9引用404；当前管理员跨登录recovery读取不误判为404。第二次正常双退出完成。

## 独立持久化和物理证明

SQL三行链精确join原回收、成功清理及本次归档，核实际规划/执行Job、Outbox处理与audit次数、MAC绑定字段和原登录/实例/路径。原purge历史仍Succeeded/Completed/Purged，HDD索引captureState仍Purged，不覆写原任务为Restored。16字段旧清理及19字段新归档Jobs的实际HTTP/可见组件逐项匹配SQL；取消后原规划保持Ready历史，新执行自身Succeeded且复制true。

无损转换器独立检查21字段SQL计划及完整嵌套source、11字段公开回执和19字段Job；设备/inode及纳秒字符串严格十进制，拒绝bool/float/指数/前导零/越界、未知字段和来源替换。模块没有产品导入、RPC或I/O，14纯转换/范围检查本身不证明实盘。

实际Root/claim/原source/purge/marker及当前HDD通过固定registry/namespace与受认证FD重新读取。独立live进程再打开实际容器/实例、parent/stage/leaf FD，验证副本是与原已删inode和HDD inode均不同的新inode，UID/GID/mode/大小/mtime/完整SHA/属性一致，stage为空且归属准确。Root Completed、claim、SQL和公开回执精确相连。成功前不得把record_proof返回的物理未检查标记当实机完成。

只在live独立核验放行后，按parent/leaf/stage FD及精确身份移除本轮已证明新副本、空stage/旧private和自有容器；没有递归删除未知文件或创建旧inode。临时身份均按本轮上下文收尾，未结fixture不得擅清。收尾后另一次独立final及第三次当前SQL/Root/HDD重读均通过；历史Root/claim和HDD证据保留，不用历史bindingCurrent=true替代当前物理文件存在。源和HDD本批仍同physical device，不签跨物理盘灾备。

## 实际结果与保持

unit `whaledeck-qa-archive-browser-788676be`，Invocation `e48d42b36cb74412886678d099cc7776`，标准输出actualRecycleArchive=passed、Result success/exit0，206.739秒。1原回收、2清理意图和2归档意图，清理/归档各Ready取消1次、正常完成1次。当前普通身份9拒绝、匿名5拒绝；新归档退出401和新登录原引用404另由浏览器实际检查。无真实群消息/邮件、生产重启或HDD填满。

原53回收/29独立恢复/17清理全行hash及旧archive0不变，只新增本轮自身记录；总54回收/29恢复/19清理/2归档、活动0。2346保护/11核心和宿主版本保持；普通Root130→133/HDD131→133、恢复Root81→82（准确claim）、purgeRoot42→44、archiveRoot4→6只增加限定本轮操作，目录Root98不变。旧Root/HDD和业务逐项保持。

实际私有证据 `deploy/artifacts/container-recycle-archive-browser-6c98a8fa94c040d3b04ce94c6d91555f` 共198文件（另加清单），含128份冻结验证闭包、全部阶段原始事实/SQL/Root/HDD/live-final-third及12张实际页面截图；SHA256 `7b7a5402713945c7f217ad117627fd8ee492d82e6ea7a95c1fe4b46eddd0b44a`，root:root目录0700/文件0600，不上传身份/私有证据或内部端点。实机首次运行即通过，没有把构造帧当成身份/HDD验收。

准备期工具证据 `deploy/artifacts/container-recycle-archive-browser-candidate-4d85c4817d9d4c62842d63f946db4585` 共14文件（另加清单），含6新源码、14专项、467回收root零跳过、8范围/14前端CI合同及实际unit输出，SHA256 `c705374395350302ea8918c915744eaff3316f690000dee3756e6feaef9f4ef2`；Python警告即错误编译及Node语法通过。范围检查本身不转签实机，实机结论来自前述独立I/O和真实身份。

## 续跑与界限

本次新写入已完成且准确收尾，不能重放；后续每种故障使用新自有资源/新操作并保留失败。当前常规链与Ready取消签收不等于全部功能或整项P1/P2完成；继续写入前/后/部分/Unknown适用故障、来源变化/跨写入、claim/暂存和重复来源策略、留存清理、维护连续性及其余59项。终态GET/Jobs均是保存时历史，当前副本和HDD必须另读，并核实界面措辞不把旧binding当即时状态。17项移出不算完成；整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，完整master尚未交付。
