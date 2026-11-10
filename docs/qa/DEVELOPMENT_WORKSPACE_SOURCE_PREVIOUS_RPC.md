# 工作空间旧树签名流验收

2026-11-10，P3-14，代码提交 `918c9793930f788ab06c7949f97bd18b4d749eed`。

Agent 1.72 提供 `development.source.previous`，将方法、请求摘要、短时效和一次性 nonce 一同签名。请求绑定工作空间、发布记录、前驱、计划摘要和已确认策略。流分块有界并携带偏移。宿主成功退出后发送最后一块和归档 SHA-256。导出限制为一个并发，停止命令仅作用于本次导出的 transient unit。

## 验收

- 隔离 KVM 使用实际非 root Agent、Unix Socket gRPC、sudo、systemd、Git Smart HTTPS 与 ext4 项目配额。签名客户端验证多块归档偏移、总长度和摘要、重复导出一致，以及实际留存的本地修改和大文件字节。
- 验证缺签名、签名用于不同方法、请求前驱改写和 nonce 重放均被拒绝。正确签名的错误计划、策略、未完成回执和发生内容变化的旧树返回对应拒绝，零归档字节。旧 Git 获取、更新交换和中断恢复通过回归。
- 首轮只读挂载被现有存储校验拒绝。第二轮允许读取该挂载后，隔离诊断得到 ext4 `Q_GETQUOTA` 返回 `EROFS`。最终保留真实配额读取，使用 Landlock 拦截路径写入，由 systemd 禁止元数据修改。真实运行单元验证只保留 `CAP_SYS_ADMIN`、`CAP_DAC_READ_SEARCH`，以及私有网络和固定单元停止。独立本机探针验证读取与管道输出可用、五种路径写入均拒绝。
- .NET 单测 6,230 项通过，范围检查 8 项通过。开发空间相关 Python 63 项中 26 项通过、37 项按本机环境门禁跳过。来宾完成并关机。
- 所有者 BFF 下载授权、浏览器下载、逐文件差异和物理工作站整链仍待验收。

本机验收包 `deploy/artifacts/development-workspace-source-previous-rpc-918c9793930f788ab06c7949f97bd18b4d749eed/`。包内 `SHA256SUMS` 校验通过，清单摘要为 `f54879c29879b1f489e71ff9d13feabf7f59f49b1ab23a60be0922446e1bcc1b`。
