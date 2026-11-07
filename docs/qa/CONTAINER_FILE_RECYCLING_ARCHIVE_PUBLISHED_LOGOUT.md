# 发布后正常退出：真实Unknown/处置整链与退出观察修正

2026-11-07，验证修正 `181418e104b916187ffb66a78219654455ecd562` 在已部署f318d9ab/协议1.59上，用全新资源完成本项真实整链。首版880695c5失败及其API-only收尾单独保留，没有重放旧Apply/ACK或追认失败门禁。本批只改QA观察与验证代码，WhaleDeck/Authentik产品及配置未改动、未部署，master未改动。

## 原失败与观测风险

首版已真实取得Root Publishing、原pending完整目标与空stage、八个互异FD/pidfd绑定；实际顶层BFF POST `/api/v1/auth/logout` 被观察时原helper仍活，随后3.053945秒退出并收敛Unknown。BFF401、身份源403已确认，但浏览器从身份源end-session进入默认invalidation flow后转到身份源root/authentication flow，未出现WhaleDeck signout callback，停在身份源登录页。不能算正常双端退出完整通过。

安全导航证据只记录method/status/origin/path与id_token_hint/post_logout_redirect_uri是否存在，不含参数值、cookie或凭据。只读核对当前provider回调allowlist、默认invalidation flow以及安装态源码，确认回调已注册；未为绕过失败改provider、flow、ID token、超时或增加手动跳转。

旧验证器在身份源顶层导航开始后，立即用`BrowserContext.request`轮询身份API，再等待最终登录页。该request与浏览器共享Cookie jar，响应可更新Cookie，见[Playwright APIRequestContext](https://playwright.dev/docs/api/class-apirequestcontext)。安装态Django SessionMiddleware对已空session可响应删除session cookie；因此退出流flush/重建会话期间的并发探测有干扰风险。现有失败记录没有保存这些API响应Set-Cookie，不能据此断言已证明原失败唯一根因；provider默认双端失效流程的预期见[Authentik Single Logout](https://docs.goauthentik.io/add-secure-apps/providers/single-logout/)。

修正后的验证器在点击前挂起实际顶层导航观察：先BFF POST302/303、身份源end-session，再要求同一mainFrame的WhaleDeck signout callback响应302/303；随后等待最终WhaleDeck `/login`，最后才读取BFF/身份源会话。不能用SPA短暂`/login`或iframe回调代替真实RP回调。缺回调仍失败且不读身份API，不手动goto、不放宽30秒路由/会话断言；导航wait立即装拒绝处理，避免早期失败后留下未处理的后续promise。

3项受控Node VM回归覆盖延迟回调、暂态登录路由+iframe不算回调、缺回调零共享Cookie读取；这些明确使用page/expect替身，不当作真实身份验收。实际成功证据在下面的全新资源流程中取得。

## 失败资源的准确处置，不是UI补签

原失败先封存。固定CASE+seal的私有处置脚本在原Unknown上，经另一临时真实管理员正常登录产品API，保存ACK前实际DTO并与SQL的id/version/executionJobId/receipt/复制null核对，恰一次ACK/202。原件、claim、完整目标/空stage与HDD保持，实际binding观察未改变；API处置明确标记actualUi=false、originalGatePassed=false。

独立live后只对本轮自有容器/目录，持三原operation锁、原发布后父版本、nofollow FD与完整snapshot调用既有published专用QA cleanup，准确移除目标/空stage/原empty private和夹具。保留Root/claim/HDD，final及第三次当前核验通过。unit `whaledeck-qa-published-logout-disposal-880695c5` / invocation `26dd8daf7f37459592956153666a4469`，18.066秒，原失败仍为失败。

## 全新资源真实验收

新unit `whaledeck-qa-archive-published-logout-181418e10001` / invocation `ad177b5dbdb54a58a633053aa73af9e7`，实际206.869秒，passed journal精确匹配。

- 真实身份→可见Vue完成回收、留存清理Ready取消/实际清理、归档Ready取消及目标最终确认。Root仍Publishing、publishedIdentity为空，目标为原pending/output新inode、完整内容/属性/SHA匹配，stage为空，原八FD/Invocation/pidfd及source/purge/marker/claim/HDD证明通过。
- 启动原pidfd observer之后才允许UI退出；先准确关闭dialog/drawer，再从真实用户菜单退出。Node只在实际navigation POST事件创建id/分域executionId/path/单调时钟标记，父进程另证当时原pidfd仍活。原helper3.279683827秒退出，早于原cut lease；开始时lease大于45秒、execution期限远于10分钟，Unknown审计晚于实际BFF请求且在settled释放前。没有目标cancel、撤权或核心服务故障。
- 原用户真实经过身份源与RP回调，BFF401/身份源403、最终WhaleDeck登录页，旧会话读取原目标401、三类hints清空。新的独立context以不同Subject管理员正常登录，9个旧意图/by-key/来源引用均404。
- 原任务Unknown/Running/1 Outbox/barrier，Root Unknown/复制null/完整目标与空stage/HDD及claim保持。实际Jobs页19字段核对，显示未知/未证明且无重试按钮。另一管理员先“暂不处置”0管理写，前后数据相等，再唯一页面ACK/202→Acknowledged/Failed/0 Outbox；复制null、Root/claim/原目标/HDD及原receipt不变。
- 当前管理员第二次真实正常双端退出，同样观察RP回调。两条receipt均BFF POST302、providerNavigationObserved=true、rpCallbackObserved=true、BFF401/身份源403及finalLoginPage=true。普通9/匿名5拒绝通过，目标取消时间和审计均0。
- 独立live后才执行精确QA target/空stage收尾，不泛化为产品Unknown cleanup或原子inode条件unlink。Root/claim/HDD保持，准确夹具移除后重新读取HDD；final及第三次当前核验通过，当前基线等于after。

命名空间存在与完整物理副本不证明目录fsync或Published/Completed持久回执；对应证明标记仍false，archiveCopied始终null。源与HDD同device，不签跨物理盘灾备。

## 检查与当前状态

初版633项回收root零跳过（38.539秒）；最终636项零跳过（38.705秒），含11退出专项和3项新增顺序回归，8范围/14 CI合同及Node语法通过。上批bccc2800精确CI42321461678已确认12项success；本轮181418e1 CI42325656456尚未完整终态，不签其完整CI成功。

失败处置after与全新资源before完全相等。累计意图75回收/29恢复/59清理/40归档，活动0；Root/HDD193/173、恢复93、目录98、purge84、archive44。旧业务、2346保护、11核心和21安装模块/f318保持，user NOPASSWD保留。

root目录0700/文件0600，文件数另加manifest：

| 证据 | 文件/冻结源码/真图 | manifest SHA256 |
| --- | --- | --- |
| `archive-published-logout-prep-731b643aa9be44f3b3d0896d6172c4cb` | 163 / 160 / 0 | `885a229979dab29f4c47cbc8c4dcd774ee72af0aa8cf549682cbaf88f8131245` |
| `container-recycle-archive-published-logout-1a8173f465a547e4a232962472e72397`（首轮失败） | 226 / 160 / 8 | `83b9e9b5c6d22cc37dc761d6526680de24d702c6feffd277dd724d797462a4f1` |
| `archive-published-logout-failure-disposal-f05d5f2932bb4e2590345b2c17efc219` | 188 / 160 / 0 | `f82bc16a56b9701b69286a107bf5cce2fec1549da38dace6b48af5682ce3ced4` |
| `archive-published-logout-callback-prep-24ee31830c04415ab858ce0daee9e6bf` | 166 / 161 / 0 | `469201d308abf70deb4745bd6cf5cbbbb597992ae8841dd87d9026f94ec48842` |
| `container-recycle-archive-published-logout-e581292e562e4067a0a23a13c6051dc2`（成功） | 268 / 161 / 13 | `04e4e240fc544affb09869f4201c03442ba2abed3757ac3652215c362cd25da6` |

仅签本次代表性写后正常退出整链，不外推所有身份源/并发/超时稳定性。继续来源/共享写入保护、成功后claim/重复来源与留存策略、维护连续性、大文件及59项完整交付；17项移出不恢复、不算完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
