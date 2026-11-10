# 开发空间首次物理部署与真实页面验收

2026-11-10。实际工作站由 `f318d9ab130ecab94668acf9754a6a6e448de92c` 覆盖至 `c51ed7866a1b43a79295a907c20996136873d00a`。当前会话的标准 sudo 已可用；本批补齐部署基线，不代表 P3-13–P3-18 或全部 59 项完成。

## 发布失败、修复和正常覆盖

干净运行 checkout 先快进到已推送的 `c78ab4de`，只启动一次标准维护 update。首个单元 `whaledeck-dev-publish-c78ab4de.service`，Invocation `4f896790c7764903b24c1a226d962c6f`，10:34:19–10:39:08 CST，真实终态 failed/exit-code/1。

宿主包已安装，但镜像构建访问 `archive.ubuntu.com:80` 超时，APT 索引不完整，API 依赖安装返回 100。数据库迁移和三应用容器替换尚未执行；宿主和容器因此暂处混合版本，未记成功、未自动回滚。

`c51ed786` 将 API、Worker、Migrator 的官方 Ubuntu 源改用 HTTPS，设置有限重试和 30 秒连接等待，并让索引更新遇错失败。实际 HTTPS 访问、11 项既有构建测试通过后提交并推送，运行 checkout 精确快进到该提交。第二个标准 update 单元 `whaledeck-dev-publish-c51ed786.service`，Invocation `cdd8e6ffe2224dd095524d8e41cf863d`，10:43:04–10:46:42 CST，终态 active/exited、MainPID 0、Result success、ExecMainStatus 0。

修复后的实际镜像构建、正常 EF 迁移及 API/Worker/Gateway 替换成功。六组件二进制/镜像和 API/Gateway HTTP 来源均核对为 `c51ed786`；Migrator 来源探测走无网络、只读元数据入口，不额外运行迁移。标准 host validate 通过运行库、HDD 策略、systemd、UDS、受限 helper 及动态入口检查。

11 核心容器最终 healthy，8 数据库平台容器的 ID、镜像、启动时间和健康与发布前完全相同。私密 `.env`、`.images.env` 和两份 sudo 规则的 SHA、属主、模式保持。user NOPASSWD 保留。账本 current=`c51ed786`、previous=`f318d9ab`、pending=null；活动任务和文件锁均 0，新开发空间表已迁移且行数 0。未提交的合并预览源码不在冻结构建内。

## 真实 Authentik 与已部署页面

新增 `validate_development_installed_browser.py/.mjs`，直接使用已部署 Vue、BFF、真实 PostgreSQL 与 Authentik，没有请求夹具或身份注入。两个随机临时身份的凭据仅通过私有 stdin 传递，不写文件、追踪或控制台。

- 管理员实际登录，工作空间列表及目录查询 200；创建页禁用无效登记，真实目录报告运行时 0、存储根 0。
- 普通用户实际登录，登记目录 403，页面显示拒绝且没有登记链接；其所有者列表 200、0 项。
- 两种身份均检查 320 暗色与 1440 明色列表、320 创建页，无水平溢出；保存 6 张真实 PNG。
- 两种身份均通过正常用户菜单退出，观察身份源和 RP 回调，BFF 会话 401、身份源会话失效，退出后的目录访问 401。临时账号均已准确删除。
- 应用写入请求 0；前后工作空间数、任务/锁、11 容器及宿主身份、账本和受保护文件摘要完全一致。

本批只验收已部署页面、目录权限和空配置行为，不把空配置当作完整开发功能。完整源码导入、Runner、报告和逐行差异的物理工作站整链尚未通过。

## 原始观察及剩余前置条件

维护采样覆盖 10:35:06–10:47:37 CST，共 376 次：374 次 HTTP 200，另有两次 URLError，分别发生在 10:35:18 和 10:44:01，与两次宿主维护服务重启对应。首轮发布最初约 47 秒没有采样。保留全部原件，不签全程无中断或零停机；最终状态 healthy。

现有物理 NVMe 仅含 EFI、boot、swap 与 ext4 系统根；根文件系统没有 project/quota 特性，先前真实只读配额查询为 `QUOTA_UNAVAILABLE`。`development-storage.json` 未登记，实际运行时目录也为空。没有改动系统根特性、分区或格式化设备；用户已说明另有 NVMe，待提供路径后先只读核对，再配置受支持的硬配额根和固定运行时。隔离 KVM 的 ext4/XFS 证据不替代物理验收。

源码基线 `c78ab4de` 的 CI `42741860294` 已全部 success。修复版 `c51ed786` 的 CI `42744552152` 在本次记录时仍运行，不能记整条通过。合并预览仍是工作区中的未完成改动，继续独立实现和验收。

两份本地私有验收包及 SHA256SUMS 摘要：

- `deploy/artifacts/development-release-c78ab4de-20261110/`：8 份原件，`29e2ac2335c9870fd67e0b864feaf85a464890fd170da20d3f4aebc6ceee5eed`。
- `deploy/artifacts/development-installed-ui-4ee60dacc53d4935a59798a3928c1291/`：18 份原件，含冻结验收源码、前后状态和 6 PNG，`8d72dd0acd1e86b8b09846631a062579a49634da31195d1fb68b8ba0fd0db285`。

文件保持 root 私有；凭据正文不入原件、Git 或日志。全部 59 主任务完成率不因本部署分项上调，`master` 尚未交付。
