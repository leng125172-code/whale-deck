# 固定前端工具链启动门禁

日期：2026-10-22。运行时没有重新部署。本记录属于安装/CI可靠性增量，不是P0/P5整阶段完成。

## 实际失败

- 44c4d1d / CI39712480445：frontend安装固定pnpm12.10.1的Linux二进制时，npm registry请求30秒超时；后端、真实PostgreSQL、安装器均success，前端编译/测试尚未启动。
- e2c35cf / CI39715331092及文档c1899a8 / CI39715635790：frontend更早在setup-node的pnpm缓存探测失败。实际日志为根目录执行`pnpm store path --silent`，Corepack查询yarn/latest收到503，并尝试下载未锁定的pnpm12.14.0，之后30秒超时。`web/package.json`固定的是12.10.1；run步骤的working-directory不改变uses步骤的缓存探测位置。
- 一次GitHub日志下载unexpected EOF、另一次CLI拒绝直接输出含转义序列的日志；通过同一已终态job的日志API并过滤相关文本取得上述证据，没有重新发起未知状态的CI。

## 修正与门禁

- d302303取消setup-node的显式pnpm缓存并设置官方v5支持的`package-manager-cache: false`，安装Node后再enable Corepack；只在web目录安装固定版本，不引入仓库根packageManager或浮动版本。
- 安装脚本仅匹配实际观察到的pnpm二进制下载超时，最多3次、等待5/10秒；仍为`install --frozen-lockfile`，保持原失败退出码。锁文件错误、安全审计或其他错误立即失败，lint/build/tests不重试，不更改源、TLS或依赖版本。
- 5项离线门禁在工作站Linux实际全通过：正常一次、超时后成功、三次上限/退出码、锁文件/审计不重试、workflow不在根提前解析管理器。Windows1通过/4个Linux进程测试明确跳过；不以跳过当通过。
- 已推送dev并快进工作站公开源码至d302303，工作树干净，未执行更新服务或重启任何产品容器。CI39716700162已确认in_progress，需观察同一运行终态；不因源码检查和Linux假进程测试通过就声称npm网络或整个前端CI通过。

官方配置依据：[actions/setup-node v5 action.yml](https://github.com/actions/setup-node/blob/v5/action.yml)。具体根目录误解析和网络失败来自本仓库实际job日志，不由文档推测。
