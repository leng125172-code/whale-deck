# 归档成功后的 claim、stage 与 HDD 留存边界

2026-11-07，验证提交 `7ce9839d62a2bcc3a9d5dbb406d8ef953201795e`。本批补齐留存门禁并撤回不安全的自动空目录清理候选，没有安装新内核、发布产品或实现自动到期删除。

## 保留策略与撤回原因

当前 Completed 只证明归档副本曾成功发布，不授权删除来源 claim、HDD 历史或 stage。目标消失、会话丢失、旧 Apply/inspect/discard、不同新操作 ID 都不能自行释放成功来源。Unknown 保留不确定现场及来源保护，不自动续写或清理。Reserved 仍只按既有授权取消流程先提交 Aborted，再释放本操作 claim。

本轮曾尝试在 Completed 落盘后检查空 stage 的 inode、Root 权限、当前目标和父目录版本，再 rmdir。候选相关 654 项测试通过，但只读复核发现最后检查与 rmdir 之间仍存在路径替换窗口：容器进程能在可写父目录中改名 Root 拥有的 0700 子目录，Root 的业务锁不会约束该进程。重复 stat 不能把按名字删除变成按 inode 原子删除。该候选 diff 已封存并撤回，没有进入提交或安装态；最终提交仅给原内核增加保留原因注释，执行逻辑保持原状。

新增真实 Linux 临时目录用例：在 Completed 检查点打开父目录 FD，仅将本测试的父目录临时设为可写；fork 子进程清空 supplementary groups，实际降为 uid/gid 1000，改名旧 Root stage 并创建同名、uid 1000 的空目录。原 stage 和替换目录均保留，目标复制仍为 Completed；原操作重放/观察/取消不调用 rmdir。命名空间与硬件证明仍使用显式测试替身，不将此用例声称为安装态容器攻击验收。

尚未解决的产品工作是：有明确授权和失败恢复的 stage 退役方式、成功来源重新消费策略、HDD 留存期限/到期清理及管理页面。不能将本批保守留存门禁记成这些功能完成。

## 自动化验证

- 6 项新留存测试：成功及冷引擎重放保留 claim/stage/历史；Completed 同源新 ID 和新计划拒绝；目标消失不释放、不再复制；Prepared/发布后中断保留原 claim；无 session 不取消 Reserved；真实降权进程替换 stage 后不误删。
- 完整回收相关 Linux root 测试 **653 项通过，零跳过，41.857 秒**；8 项范围、14 项前端 CI 合同、4 文件 Python 编译检查通过。
- 初版新测试有 2 项误把拒绝请求创建的空 `.lock` 当成 journal 改写，4 项通过。修正为独立校验新 ID 的 Root:Root/0600/单链接/零字节锁，同时仍要求无新 `.json` 和旧 claim/历史逐字节、inode/时间帧保持；最终 6 项全部通过。没有放宽执行 journal 或来源保护。

## 全新资源与当前 HDD 实读

unit `whaledeck-qa-archive-retention-7ce9839d0001`，Invocation `359356653cb94ae9a1437c388e66fe49`，成功退出 0，耗时 74.497 秒。冻结 16 宿主模块及 118 验证闭包文件，使用全新自有惰性容器、全新来源/清理/归档 ID；Root/claim 注册表位于本次私有证据目录，没有替换生产注册表。

1. 实际回收并仅清理本轮原件，当前配置 HDD 保留完整归档；归档预留取消一次，另一个新 ID 复制 1,048,583 字节为新 inode。完整字节、UID/GID、权限、mtime、单链接及 `user.*` 属性对应原文件，旧 source/purge/marker/HDD 保持。
2. Completed 副本与空 stage 存活时，旧计划换新 ID 的 reserve 和新 plan 均明确 `CONTAINER_RECYCLE_ARCHIVE_SOURCE_BUSY`；无竞争执行 journal，只有经校验的空锁。原 Completed journal、claim 和受保护历史保持。
3. 独立进程直接重读存活副本、stage 实际身份、容器实例和 HDD 后，才允许 QA 收尾。只在本次自有惰性夹具内移除准确新测试副本及空目录；这不是产品自动清理或返还旧 inode。
4. QA 收尾后，历史仍 Completed/archiveCopied=true，当前 bindingCurrent=false；再次用另一新 ID reserve 和新 plan，仍为同源拒绝且不释放 claim。独立最终核验及第三次当前 HDD/Root/claim 重读全部通过，夹具已准确删除。

原业务全行保持：78 回收 / 29 恢复 / 65 清理 / 46 归档，活动 0；生产 recycle Root 202、恢复 Root 95、目录 Root 102、purge Root 90 及 archive Root 48 保持。HDD 179→181，仅新增本轮来源的 `.data`/`.json`；2346 项保护、11 核心、21 安装模块及产品 `f318d9ab` 保持。来源与 HDD 同 device，不能据此签跨物理盘灾备。

成功证据 `container-recycle-archive-copies-e45a42feccf14644a7ec41518379e5f4`：165 文件另加 manifest，SHA256 `9900a5525f758425fe6f259459e9b467906694e2098b72c24173b3e945680cad`。撤回与修正记录 `archive-stage-retention-review-9e9c90fddae04247bdee4ec9f24b311f`：2 文件另加 manifest，SHA256 `b20f59d86949997b9459e5b29eebc927264a5ae4a0209b675d0b6957fcfb3e22`。证据目录 Root:Root/0700、文件 0600；私有路径和资源引用不上项目图谱。

## CI、交付边界与续跑

上批验证 `61faea6c` / CI `42330686454` 及文档 `4857a46b` / CI `42331673602` 均已核对 12 项 success。本批 `7ce9839d` 已推送 dev，准确 CI `42333250826` 仍待完整终态，不能沿用前批绿灯。

本批不是新的签名 RPC、真人身份页面、维护连续性或自动留存策略验收。继续完成安全退役/来源复用、留存管理、维护入口连续性、大文件稳定性及 59 项完整交付；17项移出不算完成。整项 0/59、阶段 0/6、工程粗估 25%–35%、酒馆/Live2D 功能 0% 保持。
