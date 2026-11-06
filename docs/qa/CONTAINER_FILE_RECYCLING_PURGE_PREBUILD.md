# 留存清理：标准预构建、CI失败及独立证明候选

2026-11-06，承接[Jobs摘要候选](CONTAINER_FILE_RECYCLING_PURGE_JOB_RESULTS.md)。本批完成标准预构建和生产保持核验，但准确CI出现浏览器断言失败，因此没有覆盖部署、生产迁移或真实身份清理。修复候选4b503ba63eaa6cce4f4b78c1b6141005b3cfad47另行等待新准确CI，不把旧构建当作新版本。

## 准确预构建

固定目标a0634ad5d405f0407108bb021bba666081df5ed6，运行目录由仓库用户fast-forward到该提交，私有配置保持。运行目录版本不是运行服务版本；没有复制私有.env到工作区、没有将私有配置放进Docker构建上下文。

四镜像经标准deploy/build-images.sh从Git archive构建，五个基础镜像digest固定；unit whaledeck-qa-purge-images-7514f424，Invocation da5dff5c6d36433b86d0c7f6e55a610b，exited/Result success/ExecMainCode1/ExecMainStatus0。API、Worker、Gateway、Migrator镜像实际revision标签和独立只读/无网络/无capability build-info均精确匹配目标。

两宿主经标准publish.sh冻结公开源码、自包含linux-x64构建；unit whaledeck-qa-purge-hosts-7514f424，Invocation b4461e5096eb40e386d7450486724396，相同成功终态。707文件SHA256和标准运行时servicing检查通过，候选Agent/Maintenance apphost的build-info对应目标。未执行publish-and-install或迁移；完成后只停止本批两个QA unit，未停止产品服务。

## CI真实失败与修复

准确[CI42179735729](https://github.com/leng125172-code/whale-deck/actions/runs/42179735729)对应a0634ad5，最终completed/failure：12个检查中10成功，浏览器第1分片和frontend聚合失败。第1分片128成功/4失败，失败均为acl-job-results.spec.ts的320/1440明暗bootstrap链接检查；新增清理页面场景在该分片中成功。其余5个浏览器分片及安装、后端、原生PG/Valkey、前端基础检查成功，但不替代全部通过门禁。

上批把实际Jobs链接扩为“前往文件页核对原回收、恢复或清理任务”，只同步了单元断言，遗漏这组浏览器断言。旧断言仍寻找“前往文件页核对原回收或恢复任务”，准确CI按原门禁失败；原日志与终态保存，不回填为成功。

4b503ba6更新精确文案并把两类purge Job加入bootstrap用例。仍要求真实构建bootstrap的link角色、A元素、/files目标、另一任务族链接缺席、通用重试缺席、零写入/零retry/零页面错误及无横向溢出，未放宽选择器或减少旧用例。重新类型/lint/生产构建及整份8个实际Chromium预览场景全部通过，覆盖4个明暗尺寸组合的10类Job及既有ACL结果。身份/API仍为夹具，不是工作站真实身份整链。

## 独立清理证明准备

新增recycle_purge_browser_proof.py及13项检查：严格Pascal SQL计划到native转换、20字段/嵌套字段完整性、UInt64与纳秒保真、独立来源/新执行域、三态回执、16字段Jobs与SQL对照、原规划Ready历史保持、预留/Apply不冒充结果，以及Unknown/处置/晚到证明。

补强实际清理后应使用的HDD再次读取比对：历史完整元数据/digest、来源计划/字节摘要、归档inode及普通属性/xattrs、存储身份/hash均须保持；拒绝空对象、非法或强转frame和丢字段。此函数本身没有I/O或产品调用，不能用两份构造快照证明真实HDD。后续真实门禁必须从当前受认证的HDD FD分别在删除前和准确fixture清理后重新读取，不能只检查historyRetained布尔字段。

13新项及关联清理工具合计91项Linux root执行全过、零跳过；14前端CI合同/8范围检查通过。新测试按既有test_container_recycle*.py模式纳入CI，没有另开缩减测试集。未创建临时身份、未执行浏览器真实业务、未调用新的Apply/Discard或重放旧操作。首次独立证明实现后复核补充了归档frame、属性和空快照拒绝，再执行最终91项。

## 生产保持与恢复入口

本批前/中/后完整只读基线和36回收/22独立恢复完整业务行SHA逐值保持，活动工作0，清理业务表仍不存在。2346保护/11核心、Root-HDD75/83、恢复Root59、目录Root98和清理Root4保持。运行Agent/Maintenance仍857e018d/1.58，三应用容器和发布账本current仍093f44a9；仅运行目录Git已推进到a0634ad5供构建。构建后的全局镜像标签指向候选，不表示运行容器已经替换。

私有恢复入口deploy/artifacts/container-recycle-purge-release-7514f42409db41ddac8b0bc693cfb5c9/request.json，stage为ci-failed-awaiting-fixed-target；保留已完成构建，不能重新执行它来绕过失败CI。727文件含707宿主文件和3份修复源码，PREBUILD_EVIDENCE_SHA256.json摘要73d98a12ceee307bdfab6513e27aaec1ad24f108e595c78d0acc4ebf1b60ac00，逐项复核通过；私有父目录0700/文件0600，原失败日志保留。

继续修复提交的新准确12检查终态及同版本标准构建/覆盖，核对生产迁移和六组件；之后补真实身份/来源目录/取消和不可逆清理/Jobs/原件消失/HDD保留及清理后重新读取的独立整链，再故障矩阵、留存策略/归档恢复、维护连续性和其余59项。整项0/59、阶段0/6、粗估25%–35%、酒馆与Live2D功能0%保持，17项移出不算完成。
