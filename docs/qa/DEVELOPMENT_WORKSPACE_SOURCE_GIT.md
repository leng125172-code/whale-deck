# 开发空间固定 Git 源码快照

2026-11-09，代码 `c52ff359`。本批交付非 root Git 获取器及固定路径安装校验。受信任的调用端经 stdin 提交已登记的仓库 ID、完整 ref 或提交 ID，以及该仓库专属的凭据。管理员登记文件为 `/etc/whaledeck/development-git.json`，只保存 HTTPS 仓库地址、允许的私网范围和凭据引用。凭据值只用于上游 TLS 请求。成功时输出提交及 TAR 的大小、SHA-256 和文件清单统计，随后输出 TAR 字节。

获取器检查全部 DNS 结果，将连接固定到其中一个合规地址，并校验 HTTPS 主机名。回环中继只向登记仓库转发 Git Smart HTTP 的 `info/refs` 和 `upload-pack`，拒绝重定向及其他路径。Git 子进程使用独立环境，经中继访问仓库。获取器检查 ref 在发现与获取之间是否移动，并在临时裸仓库中读取深度为一的提交。源码按原始 blob 封装，保留普通文件字节和可执行位，拒绝符号链接及子模块。源快照上限为 128 MiB，执行要求不超过 512 MiB 的临时文件系统。见 [Git fetch](https://git-scm.com/docs/git-fetch)、[Git cat-file](https://git-scm.com/docs/git-cat-file) 和 [Git v2 packfile URI 处理](https://raw.githubusercontent.com/git/git/v2.53.0/fetch-pack.c)。

## 实际验证

- 在内部容器网络用 Git 2.53.0、原生 `git-http-backend` 和校验主机名的 TLS 验证公开、Basic 私有、分支、`HEAD`、附注标签、固定历史提交以及 SHA-256 仓库。公开与私有同提交的归档逐字节一致，`export-ignore` 和 `export-subst` 不改变快照内容。
- 错误凭据、未登记仓库、未批准地址、错误证书主机名、重定向、Dumb HTTP、ref 移动、符号链接和子模块均被拒绝。真实服务端注入协议 v2 的额外 pack URI，客户端拒绝获取，服务端计数确认重定向和 pack URI 指向的陷阱路径均收到零请求。第一次验收使用的容器子网与已有网络重叠，改用独立子网。随后证明脚本显式采用夹具 CA，完成请求计数验收并清理容器、镜像和网络。
- 将真实 TLS 获取输出的同一 TAR 和 SHA-256 交给离线 KVM 中的现有宿主暂存、发布入口。在真实 ext4 项目配额下验收发布前工作树缺席、发布后目录配额证明、文件内容、可执行权限及 UTF-8 空文件。现有归档安全和 Node Runner 用例同时通过。
- Git 专项 3 项、现有源码暂存 3 项、范围基线 8 项、安装脚本语法与 Git 差异检查通过。

后续仍需接入签名 Agent、凭据服务、Worker、BFF 和 Vue，持久化固定提交及来源证明，并验收已存在工作树的更新、冲突处理与物理工作站部署。

私有证据位于 `deploy/artifacts/development-workspace-source-git-c52ff359/`，清单 SHA-256 为 `af303419dd94fba71a1f0af95eb5ed8d043fdd69719de1c3056c3117f528e688`。
