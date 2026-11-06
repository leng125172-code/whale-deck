# 归档恢复：准确CI、标准发布与生产迁移验收

2026-11-07，产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 已经标准dev更新并完成生产后验。承接[Jobs与预构建](CONTAINER_FILE_RECYCLING_ARCHIVE_JOB_RESULTS.md)。本批只签发布、实际组件/迁移/保持与匿名边界；没有创建临时真实身份、没有业务归档意图或浏览器写入，不能替代Authentik→Vue→Jobs/Worker→Agent→当前新文件/HDD整链。

## 准确CI与标准入口

[CI42277124968](https://github.com/leng125172-code/whale-deck/actions/runs/42277124968)准确head=f318d9ab，12个固定检查全部completed/success：安装、后端、真实PG/Valkey、前端基础、六浏览器分片及frontend汇总。较慢的第3/6分片实际147通过，日志不是卡死；本机按同一完整分片入口另复核147通过，未删测试或降低聚合门禁。

运行checkout由仓库用户从9694af1c通过本地可信Git对象fetch及fast-forward固定到f318d9ab，私有.env/.images.env保持，未手改生产源码。运行目录推进不是服务版本更新；标准入口前再验证干净HEAD、准确CI12成功、idle、完整Root/HDD/保护及53回收29恢复17清理原行hash保持。当前Agent/Maintenance原先独立宿主版本3f41895b不作为应用账本previous。

一次固定 `/usr/local/libexec/whaledeck-platform-maintenance update`，unit `whaledeck-dev-archive-a48574e1` / Invocation `f820a07dbd624365a04be85b3d20c7fb`，最终exited/MainPID0/Result success/ExecMainCode1/ExecMainStatus0。标准工具自行冻结源码、publish/install宿主、构建镜像、运行migrator、强制替换应用并等待健康；未用手动文件替换、SQL造状态或自动回滚。预构建用于核目标和缓存，未绕过标准构建/安装路径。

## 实际六组件与迁移

API/Worker/Gateway/Migrator/Agent/Maintenance六组件的实际CLI或运行image metadata以及实际HTTP来源均对应f318d9ab；宿主 `/proc/exe` 和DLL映射的device/inode匹配安装文件，标准host validate通过。发布账本current=f318d9ab、previous=9694af1c、pending为空；不是按Git HEAD或宿主分项版本推导previous。

生产EF迁移 `20261106175807_ContainerRecycleArchiveIntents` 已存在，21 CHECK、3 FK、8索引（含主键）、16双向共享写入围栏、1不可变guard及1来源保护触发器。归档业务意图0、活动0，DDL本身不算复制成功。旧53回收/29恢复/17清理完整行hash逐项保持。

11核心健康；8数据库平台容器完整ID/image/startedAt保持，3应用容器按标准更新替换。2346保护文件集合保持，只有发布账本允许内容变化；安装元数据仅既定sudo规则、capability key、resources.json、agent.env及账本五路径变化。四配置内容保持，key首8项身份/内容及mtime保持，只允许ctime变化；user NOPASSWD:ALL按授权保留，单文件visudo校验通过。两个Git index均归user:user；主工作区0664，运行仓库经user快进后的实际模式0644，未用root替换索引或递归chown。

普通回收Root/HDD130/131、独立恢复Root81、目录Root98、清理Root42、归档Root4及其余来源Root/HDD逐项保持，后验后另一次当前完整独立重读仍相等。新增纯比较器锁定标准发布差异，禁止Root/HDD/业务或数据库容器变化、key替换/mtime变化、开发sudo规则变化及bool/int强转，4新负向/正常检查通过；与既有宿主比较4项合计8项接入installer CI，未新增I/O或放宽产品保护。

## HTTP与维护观测

ContainersView/FilesView/JobsView三个实际入口及40份共享依赖HTTP字节与运行Gateway文件逐个相等，归档/异常/19字段摘要的真实资源标记存在；不是只看镜像标签。新sources、recovery及by-key三个匿名GET均401，业务写入0。静态资源和匿名拒绝不转签已登录页面/真实文件恢复。

维护观测unit `whaledeck-qa-archive-observe-a48574e1`，2秒间隔110样本、约218.659秒，全部HTTP200；有限采样不证明瞬时零中断，也不补签此前安装连续性失败。稳定维护入口的完整连续性仍是剩余目标。两个本批QA unit在保存成功回执后准确停止，没有停止产品服务。

## 失败事实、封存与下一步

发布前只读脚本最初给JSON query helper返回原生PostgreSQL布尔t，解析失败；保持同一谓词改用to_json后通过，未启动部署或SQL写入。HTTP验证器最初重复计数同一相对/绝对chunk引用，随后拒绝合法带点bundle文件名；规范化basename、拒绝路径穿越并逐依赖核实际字节后通过，未改产品或弱化标记。第一次生成的非raw正则触发语法提示，一并修正；原始未保存输出只记事实，不补造。

私有证据 `deploy/artifacts/container-recycle-archive-release-a48574e1aa124ad3a0769a46b6266fb1` 共43文件（另加manifest），含准确CI/分片日志、发布/观察unit回执与journal、前后/独立当前保持、迁移/宿主映射/账本/HTTP、3份新增QA源码和失败事实。清单SHA256 `dd4ff905d70e079ed5b4f43c2d8b626ccc79f820586268a68d1cb8caa9b71483`；root:root目录0700/文件0600，不上传私有证据、凭据或内部端点。

request阶段 `published-postflight-verified`；本次更新已完成，不重放。下一步在已发布f318d9ab上完善真实整链门禁；终态GET是保存回执的历史快照，必须区分回执时BindingCurrent与当前文件状态，核实界面措辞并另读当前副本/HDD，不能用历史true替代当前物理证明。随后创建全新自有容器和准确临时身份，完成可见页面的来源回收→清理→归档复制/取消→原Jobs摘要→新inode/字节/属性及当前HDD的独立读取，并在准确QA清理后再次读取；继而适用故障矩阵、claim/暂存/重复来源策略、留存、维护连续性与其余59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成，完整master尚未交付。
