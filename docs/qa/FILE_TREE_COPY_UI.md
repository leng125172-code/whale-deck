# 目录复制 UI 与运行时验收

最新已验收运行时为 **6c5b1d6b41562b1106e84d1b3d586406923c0061 / Agent1.27**。本次签收独立目录复制的BFF、Worker和WebUI子范围，不代表完整文件管理或P0–P5完成。下述“尚未发布/待验证”均保留为对应历史阶段，不覆盖本节最终证据。

## 最终实机证据：6c5b1d6

- Release零警告错误，935 Unit/120 Integration通过；6个依赖原生环境的用例在本机明确跳过，不冒称执行。222前端单元、类型/lint/格式/生产构建通过；92项联合文件浏览器回归通过。CI [39042732294](https://github.com/leng125172-code/whale-deck/actions/runs/39042732294) installer/backend/native-postgres/frontend四作业全部completed/success，CI前端实际154浏览器通过。
- 同源码冻结工作站PostgreSQL实际执行1/通过1且清理确认；非超级应用角色、空库migration、原登录查键恢复/确认、并发唯一执行、层级锁、租约、审计事务、取消、过期、Unknown和预算均在真实SQL中验证。报告`dispatch-bc326aeefaf94969aad00a686b1410cc.json`：2026-10-17 14:55:36–14:57:15 UTC，SDK ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。该SQL套件的Gateway为明确stub，真实Root证据另列，不混同。
- 实际更新unit于23:10:33 CST开始，**23:11:53 CST成功结束**（日志Deactivated successfully，1min19.800s）；安装Agent程序集包含完整6c5b1d6提交。宿主运行时PKCS/.NET10.0.13、权限/UDS/动态入口校验通过。容器程序集仍未统一嵌入Git SHA，构建来源标记缺口保留，不将宿主SHA冒充容器程序集证据。
- 真实OIDC→BFF→SQL→Worker→已装UDS/helper复制2,097,159字节：完整独立字节、空目录、mode/mtime/登记UID/GID、不同inode、原源保留、双Job、人类审计和路径锁释放通过。原登录绑定、CSRF/Origin、重放唯一、取消、保护成员拒绝、源后代变更后封存原操作且不重新Apply通过。
- 真实Authentik管理员（无route fixture）桌面1440×1000生成Ready后刷新：仍为原计划且POST confirm为0；正常点击确认后Succeeded，计划Job与执行Job不同，只有一次登记/一次确认。320×900深色正常点击完成第二次复制，重复查询不重做；收起/查看/关闭引用及真实主题开关明暗往返不产生额外登记或执行。两次完整字节/mtime/mode/登记归属和原Root回执由独立root观察者核验，专用目录已精确清理，永久回执和SQL记录保留。
- 真实普通用户独立登录，从文件深链接首次进入及刷新均到无权限页，文件界面请求0、复制弹窗0；另外显式安全探测在有效Cookie/CSRF/同源下GET/POST均403。该探测与UI零请求计数分别记录，不靠隐藏按钮证明后端鉴权。
- 独立目测：桌面Ready/Completed、320深色Ready/Completed及明色Completed、普通用户明色403；长路径与UUID换行、按钮和完整说明可达，无页面横向溢出。320深色Ready内层dialog x12/w296/h632.984，Completed h567.594；非overlay全屏尺寸。截图保存在本地gitignored `deploy/artifacts/tree-real-{desktop-ready,desktop-complete,mobile-dark-ready,mobile-dark-complete,mobile-light-complete,ordinary-320-light-denied}.jpg`。普通拒绝页初次截到过渡模糊态，已重拍并复核稳定截图；不把模糊过渡当成最终画面。
- FilesView JS `12596524d6fd3162885da84af5f3943819459b91cfa466a51995c088780edfdc`、CSS `cd5c2aa2f991b7358bef0a1d791eb1f19412e712d096e96e849d30bd290e3c01`：已测本地dist、实际HTTP与活动Gateway三方相同；8项原有公共资产HTTP/Gateway一致。
- 原33MiB+7上传/7秒停顿、普通文件变更、文本/HDD、普通文件回收/恢复四条真实链路回归通过，各自自有对象清理。最终11核心healthy、Agent/Maintenance active，活跃tree/upload/mutation/archive、路径锁、未关闭终端及临时验证/浏览器用户均0。验收浏览器正常关闭、密码/私钥引用清空；sudo策略未更改。

### 保留的失败与边界

- 第一轮真实BFF脚本在源变更后把暂态Unknown当最终失败，提前断言退出；实际原任务7秒后Failed/Aborted，目标未创建、未重新Apply。修正等待原任务调和，不放宽产品判断；重跑真实链路通过。原失败目录在核对4个终态、2个Root回执、完整源/目标、精确成员、设备/归属/链接/挂载后由专用清理器删除，审计/任务/回执不删除。
- 清理器初次对SQL PascalCase、Root的N格式UUID和源内嵌expectedVersion作了错误假设，均在删除前拒绝。按实际契约修正后精确清理通过。浏览器SSH子通道失败未创建对象；改现有RSA-OAEP内存凭据通道。一次按钮本地化异步切换导致等待超时，浏览器进程已退出，原临时身份精确清理后重新验证；不归因产品登录失败。
- 此次GLM Flash只读UI审查240秒超时，无结论，Token/费用未返回；未将它当审查通过，由GPT检查、实现和验证。原先成功的文件定位外援也不替代实际测试。
- 生产上未注入断电、强杀、网络中断或人为Unknown；丢响应/Unknown和更广故障使用明确的SQL/协议/Linux隔离证据，不能冒称生产故障演练。目录移动/回收/归档、正文搜索、容器文件、租户隔离和长期私有对象清理仍未完成；完整76项保持active。

## 本地页面证据（发布前历史，不是生产签收）

- 222前端单元测试、类型检查、lint、格式和生产构建通过；新增18项浏览器协议测试通过，涵盖320/1045/1440明暗、两阶段明确确认、刷新只GET、登记/确认响应丢失、原会话引用、取消/过期/Unknown人工处置、403清空、普通用户首次与刷新零文件请求、合法字面特殊名称及非法目标拒绝。
- 使用本地生产预览与接口夹具，独立交互检查桌面Ready/Completed、320px深色Ready/Unknown；目录/UUID换行、按钮、实际内层弹窗边界及横向溢出已检查。没有把该证据写成实际Authentik、SQL、Worker、UDS或生产文件复制。
- 追加结果丢失处理：Root已经开始而回执传输失败时持久标Unknown、保留路径锁，后续仅查询/封存原回执，禁止再次Apply；重复失败不重复写事件/审计。该变更及按原登记键查询的新增SQL断言需在同源码原生门禁重新验证。
- `deploy/validation/validate_file_tree_bff.py`已准备真实身份/双任务/字节与元数据/保护/过期版本/任务锁/清理验证；尚未执行，失败保留自有证据，不自动回滚或盲清理。

使用 playwright-interactive 技能检查交互与视觉，固定本地生产预览，不对真实工作站安装UI fixture。覆盖：

| 用户可见能力/控件 | 功能检查 | 视觉状态/证据 |
| --- | --- | --- |
| 目录行复制入口、目标根/路径和生成计划 | 只对允许根/目录显示，扫描不提交执行；中文/超长/非法路径 | 320/1045/1440明暗，表格固定列不遮挡正常指针 |
| 计划/执行双任务、完整统计和确认 | Ready不代表复制；明确确认后才写，显示原两个Job | Ready、Copying、Succeeded截图，单位/换行/按钮不裁切 |
| 原请求与原计划恢复 | 登记前路由记键，丢响应及刷新只GET找回，原会话失效不补新任务 | 未知登记/恢复Ready/404保留引用 |
| 收起/查看/关闭引用 | 收起继续查询；关闭引用不取消/重做；用户确认再解除提示 | 状态条/弹窗与提示可达 |
| 取消与未知处置 | 用户二次确认，迟到回复不复活/不自动执行；Unknown不当成功 | 取消确认、Unknown/部分对象保留及Acknowledged |
| 权限/过期/错误 | 普通用户首次与刷新0请求、403降权清空、过期禁确认、503只查询 | 无权限、过期、断线/错误提示 |

额外探索：登记响应丢失后换会话再刷新；复制响应丢失时收起/再打开；小屏长中文路径/长UUID及明暗切换。测试fixture仅证明UI协议，不能冒充SQL、Worker、UDS、真实字节或实际Authentik/生产浏览器签收。
