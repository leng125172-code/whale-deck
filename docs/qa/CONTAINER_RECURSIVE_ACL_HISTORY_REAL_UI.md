# 递归 ACL 历史发布与真实管理员页面分项验收

后续：新增26项捕获已完成真正的逐项前像下一页/返回/再前进及实盘审计验收，见[跨页分项记录](CONTAINER_RECURSIVE_ACL_HISTORY_PAGING_REAL_UI.md)。下文保留353c789常规4项夹具时点，不将其单页证据改记为跨页；多捕获库存分页仍待验。

日期：2026-11-04，dev。运行源码 `390889dad8c6cb42a881b5b05f6c70b5fda59c09`；真实门禁源码 `353c789e72ee918015567902faf49406da526028`，冻结70份验证源码。**真实 Authentik → Vue → API → 已安装 Agent → 实际前像/HDD → PostgreSQL 审计的常规读取已通过父级与独立只读验收；不表示真实跨页、恢复写入、完整权限或59项主目标完成。**

## 发布来源与保护

- GitHub Actions run `41838048867` 的完整 head SHA 为390889d，终态 completed/success。固定临时工作树先标准预构建四镜像、两个自包含宿主包；真实 build-info 与镜像标签均核对来源，宿主运行时为.NET10.0.13。预构建不替换服务。
- 运行 checkout 从b880208准确 fast-forward 至390889d，再通过已安装固定 `whaledeck-platform-maintenance update` 标准发布：宿主发布安装、冻结镜像构建、Migrator和Compose覆盖全部成功，无自动回滚、跳过构建或绕过迁移。六组件实际版本与API/Gateway HTTP来源一致，发布账本 current=390889d、previous=b880208、pending=null。
- 发布只重建API/Worker/Gateway三个产品容器；其余八个核心容器未重建。最终11核心healthy，两宿主active。发布前750份保护文件全部保留，四份宿主配置按安装流程重写元数据但内容保持，只有发布账本内容正常变化；ACL Root/HDD原件字节及九字段保持，目录Root94文件保持。user NOPASSWD保留，产品Agent权限未扩大。

## 新资源真实操作与读取

新建一份无网络、非root、只读容器根的自有夹具，目录仅含4项：根目录、普通文件、子目录与中文文件名。每个普通文件为2MiB+7字节，实际核对全部字节、inode/owner/mode/mtime/ctime、普通xattr和access/default ACL。

同一真实管理员通过可见Authentik登录和Vue控件执行8轮：default、access、noop、mask、命名ID0/权限0、default-noop、清除default、写前取消。7轮成功、1轮Canceled且无执行；每轮都先独立读取实际物理树、SQL意图/Job/回执、Root、HDD聚合及4个子项原件，再允许下一轮。没有复用旧任务、注入Cookie、拦截响应、SQL改账、修改回执或伪造历史。

随后在**原管理员浏览器上下文**点击“递归ACL历史”和“查看逐项前像”，完整核对7个成功capture集合，取消任务不出现在历史中。实际1次库存GET、7次快照GET（全部200/no-store、take=25、offset=0），28项前像严格对应HDD manifest的原始顺序、类型、原模式、access/default ACL及完整原计划摘要，保留空default、ID0、权限0和零变更历史。没有把当前统一目标ACL替代各项原ACL，也未把计划计数当成实际执行结果。

每次历史读取后，父级再次核对当前物理树、所有原SQL、7份聚合和28份子项Root/HDD原始字节，随后直接查询真实PostgreSQL audit_events。8条Validated审计严格匹配管理员subject、container target、唯一id/trace、job_id=null及实际请求/响应的摘要、cursor/offset/take、扫描/返回计数和complete；detail既核对精确字段，也拒绝明文路径/ACL等额外内容。审计来自实际EF保存，**本批不是测试拦截器抑制SaveChanges的候选证明**。

普通用户使用另一原始浏览器上下文正常可见登录，私有页面拒绝且不发历史请求；两个历史GET均403，无该主体的历史审计。管理员与普通用户均从页面菜单正常完成BFF/Authentik双端退出，退出后两个历史GET均401。保留原递归业务4个GET/4个POST拒绝及退出后4个GET拒绝检查；历史只读检查另列，不篡改原计数。

## 独立核验、清理与证据

父级、独立只读进程均 systemd Result=success、ExecMainCode=1、ExecMainStatus=0，按准确Invocation保存回执后停止QA unit。独立进程重新读取实际SQL、7份聚合和28份子项Root/HDD并核对全部页面信号、审计与正常退出；物理字节由父级逐轮、历史读取前后及清理前实际检查，**清理后不宣称重新读取已删除源字节**。

临时身份、自有容器与源目录准确清理，全部历史保留。永久递归业务从30意图/28执行/58Job变为38/35/73；旧750保护文件在发布后基线的字节及九字段全部保持，新增70份Root/HDD记录，总820，目录Root仍94、活动任务/租约/屏障/终端/锁为0。真实验收期间11核心ID/镜像/StartedAt及两宿主PID保持。主开发索引仍1000:1000/664。

私有证据不入Git：

- `deploy/artifacts/recursive-acl-history-release-prebuild-390889d-9cde0e464eb04f8d8826e798f117b995/`：726份文件及SHA256清单，含准确CI回执、预构建/发布日志、真实来源、发布前后和最终保护基线。
- `deploy/artifacts/container-recursive-acl-browser-fa340cfd170043b3b2c93a32f8c06da8/`：187份文件及SHA256清单，含70源码闭包、实际HTTP/SQL/原始Root-HDD记录、37份真实PNG、父级及独立回执。320×600暗色和1440×600亮色历史截图已人工查看，正文可滚动，关闭/返回/重读按钮保持可见，无横向溢出。

## 工具问题与未验范围

预构建首次发现主开发checkout没有私有.env，在启动构建前停止；复用同一临时工作树，只读链接运行配置，不将配置复制进源码或Docker构建上下文。首次镜像核验误查不存在的`:dev`标签，按实际`:latest`核对成功，未冒称第一次查询通过。两项均保留后续记录。

发布连续性采样误用了`/health/ready`，329条结果为000/404/503，**属于错误路径采样，不能据此推断329次维护中断或中断时长，也不能签收零中断**。原始日志及纠正记录保留；发布后实际`/maintenance/status`为200/Ready/healthy。前批48样本中3次未获200的P0-05失败事实仍有效，须另做覆盖完整发布窗口的正确连续性验收。

最终离线新证据专项11项、原递归门禁10项、关联ACL历史/恢复证据29项、范围8项通过，Python严格语法与两个Node语法检查通过。真实门禁运行时冻结353c789，原新专项当时10项；结束后仅补强“不完整库存页cursor不得落在本页最后capture之前”的独立反例及校验（11项），不重放真实写入，不把该离线反例算成额外实机场景。协作复核曾误以为historyReads来自基类verified.json；实际来自独立history-verified.json，已核对撤回，不修改有效证据。

本夹具每份4项、小于页面take25，因此**实际快照跨页未验**；本次库存1页也不代表真实多页列表通过。两者仍需超过25项/捕获的专属真实夹具，不能靠改请求take、模拟接口或空页代替。源目录与配置HDD实际device均2049，不算NVMe/HDD异盘恢复验收。历史读取实时撤权/来源损坏/依赖中断等组合、逐inode递归历史恢复写入、可恢复删除/长期清理、维护入口完整连续性及其余59项继续；不增加主任务完整完成数。

[候选与原始失败记录](CONTAINER_RECURSIVE_ACL_HISTORY_API_UI.md) · [安装态签名读取](CONTAINER_RECURSIVE_ACL_HISTORY_AGENT.md) · [当前目标](../GOAL_P0_P5.md)

项目图谱事件 `whale-deck.container-acl.353c789.recursive-history-real-ui` 已取得相同project/key/event回执，阶段保持active，pending已移至synced。发布临时工作树及4个QA unit均已准确移除/停止，主开发索引inode/ownership/mode保持。
