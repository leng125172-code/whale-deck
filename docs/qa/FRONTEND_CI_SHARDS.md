# 前端完整回归六路分片：覆盖与门禁

2026-11-04。原产品28062da的CI41681017703已经五路completed/success；本改动用于后续CI，不取消、重发或替代该次回执，也不改变待发布的28062da产物。

## 完整范围保留

原frontend四条Playwright命令的41个文件固化在frontend-layout-baseline.json，原提交28062da和固定SHA256在离线测试及真实收集器中复核。新清单仍含全部41个文件且无重复，所有既有Node请求绑定/下载/上传观察器测试、三个实机脚本语法检查、lint/format/unit/build和固定Node24.15.0、pnpm12.10.1及frozen-lockfile安装门禁保留。

新frontend_checks执行上述检查和只收集覆盖核验；frontend_browser在6个独立runner中各自检出同一github.sha、安装锁定依赖、构建生产预览及Chromium系统依赖，再运行自己的1/6–6/6。每片仍保留原workers:1、fullyParallel:false，文件内不并发；这是Playwright的文件粒度分片语义，见[官方分片说明](https://playwright.dev/docs/test-sharding)。不共享runner的node_modules或未声明构建产物，不新增外部发布步骤。

运行器只允许web目录及http://127.0.0.1:5175生产预览，拒绝真实工作站地址、非法序号/参数、缺文件/重复/路径穿越清单；直接使用已锁定安装的项目Playwright Node CLI。依赖安装仍经过原Corepack固定版本/有界下载重试。测试本身不自动重试，并新增forbid-only防止只跑选中子集。

## 失败不能变成成功

matrix fail-fast:false使一片失败不会取消其它片的完整检查；未设置continue-on-error。单一required名称frontend保留为汇总job，通过always()等待frontend_checks和全部矩阵片，只在两者聚合结果都为success时通过。failure/cancelled/skipped/空值都失败；25种结果组合已实测。语义依据[GitHub矩阵](https://docs.github.com/en/actions/how-tos/write-workflows/choose-what-workflows-do/run-job-variations)及[needs/always工作流约束](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)。没有新增cancel-in-progress。

## 本批验证和准确边界

- 14项离线流程/覆盖测试通过：保留原41文件、25种汇总结果、六片不重不漏、非法清单/地址/序号拒绝、单次进程失败退出码及无自动重试等。
- 用项目实际已安装Playwright依赖完成原全集和六片的list-only收集：749项，分片127/138/125/112/130/117，重复0、缺失0，没有启动浏览器。另以原四命令的未转义过滤规则实际收集，逐ID与新全集/六片核对，原749、新749、新增0；并非只比较文件数量。
- YAML按拒绝重复键的解析器核对成功，Python警告即错误编译、shell语法及git diff通过。
- 初轮YAML构造出现重复runs-on，提交前修正并增加回归；首次本机Corepack缓存缺bin/pnpm.cjs导致收集失败，保留失败日志。运行器改为调用已安装的项目CLI，未改全局Corepack配置或降低CI依赖安装要求。

**这是CI改进候选；新的六片尚无远端执行成功终态或实测提速结论，749是收集数而非本轮浏览器通过数。** 本地另起的409项旧末组生产预览回归仍在运行，它使用测试接口，不是真实Authentik/文件HDD证据；当前产品部署及来源UI真实签收仍需独立完成。
