# 容器上传：实际 BFF、Worker、HDD 与网页验收

日期：2026-10-27。单工作站59项中的容器文件子范围。当前六组件及HTTP实际为`fab6e2e6f2c358d4e8c83f90b78f536d8f87b43e`、Agent minor41。常规新建/覆盖上传、未发送取消和退出撤销、原实例不可用后的显式管理员接管与桌面手机页面已实机验证；完整文件编辑、历史恢复、发布中故障/撤权组合及其余保留功能继续，不签整个P2或项目。

## 发布与实际版本

部署副本先确认干净、无活动文件或平台任务，再从f5a0029快进至fab6e2e。固定平台维护动作在独立systemd unit中完成宿主发布安装、冻结公共源码镜像构建、两项新EF migration与三运行容器覆盖；unit实际退出0/SUCCESS，耗时2分32秒，无自动回滚。私有环境文件未进入构建上下文，数据库基础容器未重建。

`validate_build_provenance.py`逐项核对API/Worker/Gateway实际程序集或静态元数据、不可变镜像ID及revision标签，两宿主程序实际build-info，独立无网络Migrator CLI与HTTP，全部对应该完整提交。宿主systemd/权限/UDS/受限helper/HDD策略与动态网卡入口校验通过，框架和PKCS10.0.13保留；11核心容器逐项healthy。后续af53376/95bb1a5仅增加验证工具，产品目录与fab6e2e无差异。

## 真实 Cookie / SQL / Worker / Root / HDD

通过短期Authentik真实管理员及完整OIDC/服务端Cookie/CSRF/Origin进入已部署API，使用无网络、UID1000、只读根且只有独占可写绑定的真实普通容器。脚本不修改SQL或权限缓存，不模拟Root回执。完整最终脚本`validate_container_upload_bff.py`实际执行通过：

- 覆盖原文件与新建各2,097,159字节，完整SHA、两次同键登记保持同一原意图/期限、202只暂存、专属Worker发布与原读取RPC全字节下载一致。独立读取SQL终态/Job/Outbox/审计和Root journal/payload；覆盖保留原inode、UID/GID1000与0640，HDD完整旧原件及范围/摘要匹配；新建无虚构旧备份。
- 未发送任何内容时显式取消，真实Worker落盘Prevented、任务Canceled/锁与租约释放、目标不存在。另一真实管理员登录的原Cookie正常双端退出后重放401；没有显式取消，实际Worker自行撤销其尚未接收的意图并写相同持久拒收标记。
- 真实普通用户有效Cookie/CSRF下状态和计划均403，匿名状态401；普通用户正常退出。测试账号来自Authentik，不在Whale Deck建立本地密码或资料。
- 原管理员创建另一意图后实际停止该自有容器，Worker记Unknown/CONTAINER_UPLOAD_TARGET_UNAVAILABLE；随后原登录正常退出并删除原Authentik账号。另一名不同主体、有效登录的真实管理员，经独立container定位和Verified摘要、计划/当前观察及二次确认，完成Root永久封存和SQL原子对账。新恢复任务Disposed/FILES_RECOVERY_DISPOSED，原任务Failed/CONTAINER_UPLOAD_ADMIN_ACKNOWLEDGED；原不可变身份/路径/实例/请求保持，封存主体为新管理员，没有Publish、文件载荷或假成功。

该Unknown来自尚未接收内容的停止实例，RootStart和全局PublishBarrier均未建立；不据此声称实测了发布中Unknown的全局保护释放。后者只有先前隔离原生PG及Root profile证据，仍需真实整链故障组合。本夹具源绑定与留存同HDD，是版本留存/内容证明，不是物理异盘灾难恢复演练。

## 真实网页与独立观察器

`validate_container_upload_browser.py/.mjs`通过正常可见Authentik用户名、密码及首次授权控件登录真实站点，无请求拦截、接口替换、storageState、trace或凭据导出。真实1440明色打开自有容器详情与work目录，先取消覆盖确认，确认期间零登记/零内容请求；重新确认后只有一次登记和一次二进制发送，页面显示已发布和旧版本留存。再切320暗色，通过同一页面新建中文文件，也只有一次登记/发送。

两个文件各2,097,159字节；页面确认和终态、当前目录自动刷新、文件面板及整个document无横向溢出通过。独立Python观察器随后再次核对实际SQL成功、审计、Root完整载荷、目标全内容与权限、旧inode/HDD全内容及新建无旧备份，不能只靠“已发布”文案签收。实际截图已目视复核：两种主题可读，长任务/版本换行，小屏操作按钮与文件列表均在面板范围内。

同源另一1440页面通过正常用户菜单退出，两页返回登录，上传面板和文件输入清除；BFF及旧Cookie401、Authentik401/403。两次上传未在退出后继续发送字节。最终浏览器/子进程正常结束。

## 失败、精确清理与永久证据

首轮CLI在读取管理员inventory后失败：验证脚本误写Integrity=Valid，实际公开契约为Verified。此前两次成功上传、两次取消及原Unknown都保留。修正脚本后，仅用原私有基线恢复该原任务，真实另一管理员处理通过；随后最终冻结af53376脚本全流程重新通过。未更改产品契约，也未删除永久历史。

前两次浏览器验证已完成真实覆盖上传，但断言请求头失败；Playwright早期request.headers视图不完整。添加固定阶段/源码行诊断，使用allHeaders完整视图后保留同样Content-Type、Origin和幂等头断言，完整第三次通过。没有修改产品请求、关闭来源校验或删除断言。失败时源目录先保留；后续针对精确两份私有基线，独立核对容器已删除、唯一SQL成功意图、Root journal/操作锁、新内容完整SHA及pendingProof、旧内容与originalIdentity、HDD全内容和范围后，只删除这两个已证明的QA源目录及其私有旧inode。没有递归或前缀批量清理Root/HDD。

自有容器/源目录全部清理；SQL意图、Job、不可变审计、Root回执/载荷、永久封存与HDD历史保留，未在目录记录背后删除归档。最终本批14条上传：8 Succeeded、4 Canceled、2管理员确认后的Failed；Unknown=0、活动租约=0、PublishBarrier=0，无活writer unit或自有验证容器。三类临时身份只读复核均0，11核心healthy。前序原生测试留下的空锁未因本批验收被猜测删除。

私有日志、原始失败、版本证明和验收结果在`deploy/artifacts/container-upload-bff-ui-20261027`；真实网页原始基线/截图/观察器证据在独占`container-upload-browser-*`目录，0700/0600。没有把Cookie、Token、owner、原登录绑定或密码写入公共文档/日志/图谱。

## 自动化与剩余

b681e1d本地Release零警告错误，2604单元/191集成（7专用原生默认跳过）、456前端单元和52合成浏览器门禁沿用原记录；与部署产品代码差异为零，不冒称本批重复执行全部套件。新Python编译和Node语法校验通过并接CI。b681e1d CI四作业已success，frontend最后观察仍在正式浏览器步骤in_progress，不宣称全CI通过。

继续进行中接收/发布的取消、断线、权限过期与撤权、Worker/Agent重启和有全局保护的Unknown实盘接管；再完成容器编辑、HDD历史查询/恢复与其余59项。当前实机常规流程通过不代表完整容器文件模块或全部目标完成，17项授权移出仍不算完成。
