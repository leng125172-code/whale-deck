# 清理Ready：正常退出、新登录隔离与自然到期

2026-11-06，产品保持已发布9694af1c，验证候选a0f3f7eb。承接[常规真实清理整链](CONTAINER_FILE_RECYCLING_PURGE_REAL_UI.md)，本批新增Ready后正常BFF/OIDC退出、同Subject新登录不能继承旧引用/确认，以及不改时钟的自然五分钟计划到期组合。该组合已在全新自有资源/临时真实身份中通过；只签Ready退出/新登录隔离/自然到期，不代表执行中清理故障矩阵完成。

## 已验证的准备

新增父门禁、真实浏览器脚本、独立只读核验器和8个针对性检查，关联清理工具105项Linux root执行全过零跳过；14 CI合同/8范围及Python/Node语法通过。121份源码闭包固定并在身份/容器创建前核对，安装helper与产品版本逐字节匹配；没有重新发布或向Worker/Agent发信号。

Ready自然到期不是API的临时Expired投影：父进程要等实际SQL意图Expired、completed_at已写入，完成时不得早于原ready_expires_at且迟到不超过30秒。确认、取消请求/审计和执行/预留/Apply/回执/可空清理事实必须缺席。规划Job仍应完整保留当时Succeeded/Ready历史，原来源、请求、计划、签名和期限不得替换。

正常退出后旧所有者接口和旧确认请求需401；同Subject重新正常登录，旧ID/key读取及确认均404。当前管理员只可从专用恢复元数据接口只读观察原计划，不能把新会话当作旧会话或确认旧计划。浏览器不注入会话、不伪造响应、不改系统或页面时钟；记录的直接失败确认探测与零页面清理确认/取消分开统计。

测试源已经真实回收并保留原inode/HDD，不能用普通Ready未回收文件的清理方式处理。到期与源/HDD保持证明通过后，门禁只允许同一来源走已有独立恢复的专用预检/明确确认；独立核验原inode返回后，才删除本门禁的已证明测试文件和空private目录，保留HDD。清理后再次以当前attested只读FD读取归档，只允许专用恢复导致captureState由Trashed前进Restored，其余metadata、归档字节/身份/属性/存储保持。

本批不执行purge确认或取消，不应新增任何Native清理记录；完整旧清理Root必须保持。其他来源Root/HDD只允许本门禁来源和独立恢复的确定ID，全部旧业务完整行SHA要保持。未决或证明失败会保留测试现场，不自动删除留存原件。

## 实机结果

unit whaledeck-qa-purge-ready-a0f3f7eb / Invocation 0704e50133da46ba8cbfefbbbaf76f0f，最终exited/Result success/ExecMainCode1/ExecMainStatus0。验证源码a0f3f7ebec38682c2ead590f503ae847befbf25c，产品及实际helper仍9694af1c，没有覆盖发布、Worker/Agent重启或向产品进程发信号。

新1048583字节普通文件经真实页面回收，父进程独立核验原inode进入留存区、SourceRoot/HDD和源SQL后，才允许生成清理Ready。页面未点击任何清理确认或取消。正常BFF/OIDC双端退出后，4个原入口均401；保留在内存中的旧确认按旧会话提交也401。正常重新登录的同Subject新会话，对原ID/key读取均404，使用新CSRF但旧计划确认也404。这两次直接HTTP失败探测不混同页面零确认统计。

浏览器169次只读观察原恢复元数据，保留原readyExpiresAtUtc；从Ready起至浏览器流程结束330.560秒（包含后续QA恢复）。SQL的期限为17:28:07.441318+08:00，completed为17:28:07.627795+08:00，真实自然到期后0.186477秒写入Expired。API过期投影只触发父进程复查，实际SQL完成后才认可。原意图的来源/请求/计划/签名/期限不变，规划Job完整行与Ready前快照逐值相等；执行Job、确认/取消审计、Root入口、预留/Apply/回执及所有可空清理事实缺席，既有全部Native purge Root不变。

到期时原留存文件/原inode/字节/普通属性/xattrs和HDD快照保持。随后在同一新登录中，页面选择该来源走独立恢复专用预检及明确确认；父进程先重新核对清理SQL终态和原件保持，再放行恢复。实际原inode回到原路径，Restore Completed/Succeeded、当前绑定及恢复归属true、SQL/Native claim/Root/HDD/Job对照通过。这个恢复仅用于本轮自有测试资源收尾，不是清理执行，更不是从已清理归档恢复。

独立进程在容器仍存在时重开物理命名空间、原件和HDD。通过后才删除这份已重新核验的测试inode及其空private目录，并由fixture机制删除该自有容器；HDD历史和.data没有删除。清理后父与独立进程再次打开当前read-only/attested HDD FD，确认captureState仅因专用恢复前进为Restored，完整归档数据inode/字节/属性/存储hash保持；第三次独立最终重读再次通过。

原清理规划Job在真实Jobs页仍显示历史Ready/Succeeded及16字段、无通用重试，原意图则为实际Expired；不把两个层面的事实互相改写。共3真PNG（Ready、QA源恢复、历史Job），抽看320宽Ready和桌面历史Job。之后第二次正常双端退出。真实普通权限门禁5GET/4POST共9拒绝、匿名5GET均401；加上退出后的旧引用/确认拒绝，证据阶段分别保留。

## 保持、封存和边界

旧37回收/22独立恢复/2清理的完整业务行SHA保持，全部旧Root/HDD、2346保护和11核心/已发布版本保持。最终总38回收/23独立恢复/3清理（本轮1个Expired）、活动0；SourceRoot/HDD80/87、恢复Root62、目录Root98、PurgeRoot仍6。本轮没有Native purge预留、执行或取消，没有清理留存原件；只新增准确来源和QA恢复记录。

私有证据deploy/artifacts/container-recycle-purge-ready-logout-83d0af42f4fe4aa19bae737cc3f9bfaf，165文件/121冻结源码/3真PNG，EVIDENCE_SHA256.json摘要50808b98050b8aec0ab85acedd565a6d5ad2bf1c2123ad58737164345cd7f54b，逐项重算通过。原始截图仍root私有权限，仅逐字复制两图到本任务另一个私有复核目录；原件/SHA/所有者不变。真实登录不使用替身数据/注入会话，凭据未进入Git、日志或图谱。

这是一项Ready正常退出加自然到期的组合门禁，未另签持续有效旧登录下单独到期场景，更未签删除执行中取消/退出/撤权/重启、自然执行租约、Unknown或跨管理员处置。继续上述故障矩阵、来源/共享写入保护、留存策略/归档恢复、维护连续性和其余59项；文档提交不重部署，17项移出不算完成。正式整项0/59、阶段0/6、工作量粗估25%–35%及酒馆/Live2D功能0%保持。
