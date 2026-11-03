# ACL历史读取维护代理超时：复现、修复候选与未验边界

2026-11-03。产品仍`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/协议1.52，Agent1399935/维护1214273；本批发现真实页面历史读取503，已定位并提交产品修复候选`b69302b8cb32048038d7dbeed193af205c70fd11`，**尚未安装或发布，写后撤权两场景未整体签收**。不以候选、重试或单个分项通过代替完整验收。

## 实际失败及已保留分项

`dc2433d`扩展schema11/version5（写后移组/停用），83项相关检查及语法通过；两新资源真实Authentik/Vue恢复在目标ACL已写、Root仍Applying时撤权。首场移组分项完成：产品4.834秒停helper，原任务Succeeded/Completed、目标新ACL/版本与当前前像HDD保持；原恢复GET403、身份GET200非管理员、页面forbidden/引用清空/取消POST0，旧正权限仍余7138.153秒。不同当前管理员Recovery GET200/no-store，原Owner专属`/restores/{id}` GET404及正常HTTP双端退出通过；这是实际HTTP读取，不冒充当前管理员任务抽屉UI。

同一批第二场在历史列表读取阶段得到503维护页，尚未登记恢复意图或执行停用；整批退出1。SQL按准确CID查到的唯一意图是已完成普通ACL来源捕获，Root/HDD原件只读复核保持；容器和临时身份已清理，未证明源树按失败策略保留。原件`deploy/artifacts/container-acl-faults-1aa89d751f624a648c00d4266edf8b9f/`，含失败截图、SQL消歧、分项只读复核与wholeBatchPassed=false，不生成整批通过回执。

`ea30f94`补上私有错误状态/阶段记录，只允许502/503/504时点击现有“重新读取”，最多2次，错误未被忽略。全新复验第一场在历史读取连遇3次503，重读上限耗尽、整批退出1，没有创建恢复意图或执行撤权；原件`deploy/artifacts/container-acl-faults-b13f3e2ea65c453d93bd1adecdfb0c6a/`。两失败源树保留，不宣称准确完整清理；不重放第一批已完成的移组或来源捕获。

## 根因实际对照

使用独占只读诊断容器和正常HTTP Authentik会话，不修改ACL或历史记录：

| 路径 | 实际响应 |
| --- | --- |
| 现有维护入口127.0.0.1:8080 | 5.005秒后503，text/html，645字节维护页 |
| 同一请求直达内部loopback Gateway 18080 | 4.972秒，200/application-json，扫描124份元数据且complete=true |
| 同一会话再经8080 | 5.007秒后503/text-html维护页 |

两次诊断资源均准确清理，数据原件在`container-acl-history-diagnostic-e83b006f05ff4136863fa6cbffc66fcb`和`container-acl-history-proxy-642e8708269443ed9bcb987f3188285b`私有目录。直达18080只作根因对照，**不是绕过维护入口的产品验收或新增端口暴露**。

MaintenanceHost的普通gateway cluster仅5秒ActivityTimeout，历史读取未命中已存在的下载/指标专用预算。当前HDD元数据增长到124份，一次验证扫描已接近5秒；代理先超时并由StatusCodePages转换为维护HTML，因此工具读不到JSON问题码（UNRECOGNIZED），不能把它解释成数据丢失或账号停用生效。历史reader自身还有5秒硬预算，单页最多200记录；继续无余量扫到上限也可能耗尽最终目标/库存校验时间。

## b69302b产品修复候选

- 新增两个明确的GET路由，仅规范小写64位CID的ACL历史列表和小写32位capture读取，禁止请求正文；35秒预算覆盖Agent的有界RPC与收尾时间。原5秒普通路由、写请求、其他API及更高优先级Authentik路由保持，不扩大权限或Agent能力。
- reader保留5秒硬截止、200条/页与10000库存上限、完整库存版本、目标身份和HDD attestation。扫描达到3秒软预算且已前进后返回原版本绑定的部分页，保留时间给最终校验；单次卡死超过硬截止仍失败，不返回伪空历史或跳过验证。读取不创建/修改Root/HDD。
- 无API/Worker/DB schema或依赖版本变更，仍.NET10/YARP2.3.0；新路由进入正式MaintenanceHost配置。代码已提交，不等于已安装。

## 实际验证与边界

冻结b69302b Release构建零警告/错误，4363 Unit通过；默认Integration 488通过/7原生跳过，不能冒称本机新原生PG已执行。2项新维护代理测试包含真实loopback服务器和6秒延迟的列表/快照JSON读取、规范ID/路径/GET/正文与优先级边界；慢响应来自隔离夹具，不是生产入口验收。17历史内核+15传输Linux-root检查通过，含软时间分页进度/同版本无重复、硬截止继续失败及原件不写；测试时钟仅限隔离单元输入，未改系统或SQL时钟。

冻结候选直接读取真实HDD的124份库存，在新独占只读容器下分82/42条两页完成，耗时3.085/1.604秒，原库存版本连续、124份元数据字节/九字段保持，准确清理。原件`deploy/artifacts/container-acl-history-candidate-9f9bb360c36c42e38b5a6dc941ab26fd/`；调用的是候选reader与现有受信模块，**不是安装态签名RPC、真实人类UI或维护代理发布证明**。

准确候选[CI run41647839607](https://github.com/leng125172-code/whale-deck/actions/runs/41647839607)已观察backend、installer、native-postgres、native-valkey四路completed/success，frontend仍in_progress。没有用此前cad或工具CI代签新候选五路终态。

最后只读核验：实际六组件仍cad6182，已安装history helper字节保持旧cad，11核心healthy/8数据库平台及API/Gateway不重建，Agent1399935/维护1214273保持，7份配置/能力密钥/sudo文件SHA/uid/gid/mode与发布账本保持，活动任务/终端/锁/屏障/未完成意图0。user NOPASSWD保留，不自动回滚、安装或重启。两失败树留证不是生产用户数据，更不记为长期清理已完成。

下一步取得准确CI终态后标准覆盖新候选，核对六组件/实际安装字节，再经8080真实历史分页与完整写后移组/停用、正常身份/文件/HDD整链及独立复核。此前单场移组只保留分项通过；账号停用写后整链、进一步超时/失败恢复组合、Agent写后重启、Unknown/实例或来源变化、磁盘/共享写入、递归ACL/恢复、删除/长期清理及其余59项继续。59主项完整签收和25%–35%粗估不变，17项移出不算完成，master未交付。

收尾8项范围检查、860条变动MD相对链接通过，目标第1节逐字保持，前批写后取消/强杀QA原文保留后追加接续。项目图谱已确认原`whale-deck/container-acl`的`whale-deck.container-acl.b69302b.history-read-timeout`事件，阶段active；按原eventId从pending移入synced，未将候选或失败批次标作完成。
