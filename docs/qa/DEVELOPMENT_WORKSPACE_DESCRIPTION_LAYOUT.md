# 开发空间描述区布局与实机验收

2026-11-10。开发空间环境/存储页的窄屏逐字竖排修复已完成准确 CI、标准 dev 发布和真实身份安装态验收。下方保留本地候选验证的范围，不用合成元数据替代实机结果。

## 问题与修改

原定义列表使用两列自动排版，一个固定运行时标签对应多个值。后续运行时值会进入标签列，其长镜像摘要扩大该列、压缩其他值；真实 Chrome 在 320px 暗色生产构建中复现逐字竖排。

候选显式将标签和值固定到各自列；600px 以下改为标签在上、值在下的单列，所有值使用可用宽度。长摘要仍可换行。保持原 Element Plus 颜色、权限、业务请求和数据，未增加专用移动端流程。

## 已执行的候选验证

- 基于 2e4709bd，生产构建和类型检查成功，格式检查通过。
- 真 Chrome、合成且拦截的 API 元数据，包含 Node/.NET 两个固定运行时；320/390/768/1440px 的明暗主题共 8 组原问题均复现，候选 8 组均无水平溢出，运行时值同列且手机值使用完整宽度。实际查看了修复前后 320px 暗色截图。
- 原有启停及丢响应恢复、重新授权、注册/存储/运行与报告三项生产预览浏览器回归全部通过，30.1 秒。
- 测试 Chrome、5174/5175 本地预览服务均精确收尾。Corepack 缓存入口两次不可用，保留原失败后使用已有固定版本原生 pnpm 12.10.1 构建；未改依赖版本。观察器初版将内容宽度误标为 viewport，保留原 JSON/脚本后增加明确字段，原几何与失败结论不变。

证据目录：deploy/artifacts/development-workspace-description-layout-2e4709bd-20261110/。36 文件 SHA256-MANIFEST.json 摘要 8b6068c73768835236399b573c25c9bf16882e123a7cb785a660261bf4978b60，候选差异摘要 741bbb3e4d91c89ec3d3f9cc406f099f0026574ae3c59663b3095ccf94bd8085。

## 准确 CI、标准发布与真实身份验收

afcb11c27539806ce09ac1d6f5a6ab409d711af1 的准确 CI [42840151593](https://github.com/leng125172-code/whale-deck/actions/runs/42840151593) 已 13/13 success，无失败或复验。正式干净检出固定该提交，经标准维护 update 入口发布成功；六组件来源一致、23 宿主模块字节匹配、11 核心健康，8 个原数据库依赖身份保持。1600 次四端点升级采样取得 1568×200、32×真实维护 HTML503、零连接错误；维护进程更新，而 PID 1 的双监听 inode 和激活时间保持。host validate 的真实网卡/桥隔离等检查通过，发布 oneshot 已精确退役，journal 保留。

新增显式启用的 validate_development_layout_installed.py/.mjs。临时 Authentik 管理员通过正常页面登录真实 8080 入口，不拦截 API，不注入会话；核验 API/Gateway 来源及空间真实 Node/.NET 的版本和不可变镜像。在 320/390/768/1440px 明暗 8 个唯一组合逐项检查渲染文字、值列对齐、手机满宽和无水平溢出，8/8 通过，保存 8 张截图并实际查看 320px 暗色图。正常用户菜单退出观察到 Authentik end-session 和 RP callback，BFF401/身份403、空间 DOM 消失；临时账号删除、浏览器关闭且验证器进程为零。凭据仅经内存/stdin，未导出 Cookie/token/storageState。

首次实机检查因观察器对 textContent 的错误假设失败：br 不产生文字分隔，版本号和镜像名被拼接，首个版本前缀断言误拒。原失败源码、状态和临时账号删除证据单独封存，未到正常退出步骤，不追认为通过。隔离真实 Chrome 复现后，观察器改读实际渲染 innerText；成功轮同时保留两种文字，8 组均证明旧检查为 false、渲染版本可见为 true，未改产品代码或放宽布局条件。另一次前序清单复核误用 JSON 清单名，改按原 SHA256SUMS/JSON 分别逐字节验证，原件不改写。

两轮均无空间写请求；18247 条原记录、5 空间物理源码和配额、18 报告、epoch15、2300 个原任务保持，活动任务和文件锁为零。两临时账号均删除，HDD 无 Capture，原 72 项归档 journal 不变；120GB/300GB 完整预留强制上限、user NOPASSWD 和用户原未提交源码改动保持。13 份前序清单的 516 文件再次核验通过。

实机证据：deploy/artifacts/development-workspace-description-release-afcb11c2-20261110/，71 文件，SHA256-MANIFEST.json 摘要 a4f44ec6b51f53ba709c1a64650b810ac5cacc503c68e4d8c64879b9e46b0d99。主要结果为 batch-acceptance.json、final-state.json、installed-layout-rendered-text/accepted.json；首轮失败保留于 installed-layout/，不能混同成功轮。

## 未完成边界

只签环境描述布局及本次发布观察；未重跑构建/测试/导入等完整用户链，也未实现或验收持久归档、HDD 捕获与空间退役。下一批应推进持久 Plan、用户确认、Capture/独立 Verify、取消/重授权及 Resume 数据库围栏，源码删除和预算释放须另行验收。完整主任务仍 0/59，ST/PET/供应商/Live2D 继续，17 项移出不恢复。图谱阶段更新已保留本地 pending，尚未远程同步。
