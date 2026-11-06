# 留存清理：准确CI、标准覆盖与生产迁移

2026-11-06。准确提交9694af1c473fe9a62b786532f7bd6a3bd2107e1a已通过[CI42182505777](https://github.com/leng125172-code/whale-deck/actions/runs/42182505777)，12个固定检查全部completed/success，包含安装、后端、真实PG/Valkey、前端基础、6个浏览器分片和frontend汇总。上轮a0634ad5的旧断言失败与修复事实保留于[预构建记录](CONTAINER_FILE_RECYCLING_PURGE_PREBUILD.md)，不回填通过。

本批显式执行一次标准dev覆盖，不请求自动回滚。只签发布与生产/匿名边界核验；没有临时身份、没有真实清理意图、没有UI业务点击，不能替代Authentik→Vue→Jobs/Worker→Agent→原件删除/HDD保留整链。

## 固定来源与发布操作

新目标四镜像预构建unit whaledeck-qa-purge-images-7a51181e / Invocation 7aab31cd5ea7407b94cb494a88069beb，两宿主unit whaledeck-qa-purge-hosts-7a51181e / Invocation 740ff396670d4cec834b2af697714e0c，均exited/Result success/ExecMainCode1/ExecMainStatus0。4个实际镜像标签和只读/无网络build-info、2个自包含宿主build-info精确匹配目标；707文件SHA及10.0.13运行时检查通过。未使用上轮a063的构建冒充本版本。

发布启动器要求同一headSHA的12项完整成功、运行目录精确/干净、空闲和旧基线/业务保持，才调用固定whaledeck-platform-maintenance update。实际先验证缺ci-success回执时失败关闭，发布unit不存在且request不变；该预期拒绝记录保留。四个发布/观测/后验脚本语法检查通过，随后才在准确CI完成后放行。

标准更新unit whaledeck-dev-purge-7a51181e / Invocation 53e374d9e1f34f34a1dc55acd1143c24，最终exited/MainPID0/Result success/ExecMainCode1/ExecMainStatus0。标准维护流程重新冻结源码、构建并安装宿主、构建镜像、运行migrator，再强制替换3应用容器并等待健康；没有绕过配置、手改数据库或手工替换应用文件。

## 实际组件与数据库

API、Worker、Gateway、Migrator、Agent、MaintenanceHost六组件及实际HTTP来源均匹配9694af1c。宿主运行/proc/exe和DLL映射与安装文件dev/inode吻合，标准host validate通过。发布账本current为9694af1c、previous为093f44a9、pending为空；previous不是上次857e018d的宿主分项安装。

生产EF迁移20261106060735_ContainerRecyclePurgeIntents实际存在：21 CHECK、3 FK、8索引（含主键）、15共享写入围栏、1不可变guard和2来源保护触发器。清理意图0、活动0；DDL检查不冒充真实清理成功。原36回收/22独立恢复完整行SHA逐项保持，既有递归来源与恢复业务摘要保持。

11核心健康，8数据库平台容器ID/镜像/StartedAt保持，3应用容器按标准更新替换。2346保护文件集合保持，内容只有发布账本允许改变；安装元数据仅既定sudo配置、能力key、resources.json、agent.env及账本5路径变化。四个配置内容保持，能力key首8项身份保持，仅允许ctime变化；未递归修改Git或私有证据所有者，两个Git index仍user:user/0664。user NOPASSWD:ALL按授权保留。

普通回收Root/HDD75/83、独立恢复Root59、目录Root98、原清理Root4及其他来源Root/HDD逐项保持，发布后再独立完整重读与postflight一致。未创建清理任务或改变业务留存。

## HTTP资源与维护观测

ContainersView的清理入口、FilesView的独立异常处置、JobsView的16字段摘要/页尾提示所在3份实际HTTP资源，均与运行Gateway文件逐字节相等；不是只看镜像标签。三个新清理匿名GET均401，业务写入0。静态资源与匿名拒绝不能转签可见页面交互或实际身份授权。

维护观测unit whaledeck-qa-purge-observe-7a51181e / Invocation 574cb54032074a44a28a79e3bb6bc331，exited/success/0。约114.419秒、两秒间隔58样本：57次200，66.124秒处1次ConnectionError，随后200恢复；不得记为全程可达或精确中断时长，更不补签此前维护连续性失败。稳定维护入口与安装时连续性仍需后续实现和真实验证。

## 封存、恢复点与下一步

私有证据deploy/artifacts/container-recycle-purge-release-7a51181e4d4549bab0b96ef467072b20，774文件含707宿主文件及14冻结源码；RELEASE_EVIDENCE_SHA256.json摘要d6f2632d08ca2d82f210e9aab47f729531342716026a4ef9096979ff4f048980，逐项重算通过。另有13文件不可变预构建检查点，摘要1c53789f32cbfa1dda109fa8c14f0fcb92a70514fd120886829616573a5d0abe；当时的CI进行中快照保留，不用后来的成功改写历史。

request阶段为published-postflight-verified，准确CI、unit/journal、6组件、宿主映射/校验、迁移/完整旧行和Root-HDD、HTTP与维护样本均保留；已完成更新不重放。私有目录0700、文件0600，凭据未写入Git或项目图谱。

接下来在已发布9694af1c上创建全新自有容器和临时真实身份，完成来源回收→清理Ready取消→新意图明确不可逆确认→原Jobs摘要→原件消失及HDD保留，并在准确fixture清理后重新打开当前受认证HDD再核对；然后执行适用故障矩阵/共享保护、留存策略/归档恢复和维护连续性。仅文档记录不要求重复部署。59项保留、17项移出不算完成；整项0/59、阶段0/6、工作量粗估25%–35%及酒馆/供应商与Live2D功能0%保持，尚未交付完整master。
