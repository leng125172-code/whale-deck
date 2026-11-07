# 发布后重启：首轮失败、自然租约分项与准确收尾

2026-11-07，QA `9266669e0316b1593cffc2566e0ff7100c8f25f6` 准备发布后Worker强杀/自然租约及Agent重启两种门禁。实际仅执行Worker首轮；它完成故障与Unknown业务观察后，在原用户正常退出环节失败，**完整页面整链未通过，Agent场景未执行**。承接[发布后取消与Unknown处置](CONTAINER_FILE_RECYCLING_ARCHIVE_PUBLISHED_UNKNOWN.md)，该既有签收不因本批候选而扩展。

本批未改产品代码或部署，产品仍为f318d9ab/协议1.59，master未改动。`dc988b42`增加安全退出观察与单失败夹具处置；`a5a4002ff61e2329aa5a24cedfa6aa10da67790e`修正已ACK夹具的续收尾核验。后续仍须全新资源真实复验，不能重放本轮Apply。

## Worker实机已经观察到的部分

全新自有容器、临时真实身份通过可见Vue完成原回收、留存清理Ready取消/实际清理、归档Ready取消与目标归档最终确认。Root仍Publishing、publishedIdentity为空，但目标已为原pending/output新inode，完整SHA/属性/字节匹配，stage为空；原pidfd/Invocation/cgroup和八个互异FD证明通过。

故障前再次核对Root/目标/来源/claim/HDD及发布后父目录版本，并排除其他15类活动文件工作和不相关transient进程。仅对固定原Worker容器发SIGKILL，同ID/镜像恢复；原helper于0.106317299秒内退出。没有目标cancel或提前owner logout。

自然等待旧lease 84.247614206秒，40个密集到期前样本逐次核对SQL Applying/barrier/原lease、Root与claim原始字节、完整已发布目标/空stage、发布后parentVersion、purge原件和HDD。未改时钟或SQL。原Unknown结果审计发生在lease到期后，意图Unknown、执行Job Running、1条Outbox保留，Root Unknown/复制null/共享claim与目标保持。

这些是已保存的实机分项，不代替完整退出、不同管理员页面ACK、Jobs及整链验收。命名空间已经发布也不能证明parent/private fsync或Published/Completed持久回执，三个相应证明标记仍为false；未执行主机重启、断电或HDD填满。

## 原门禁失败与观测候选

unit `whaledeck-qa-archive-restart-9266669e0001`、invocation `d693949064224fbe972957285c3c7579`：exit-code/1，274.411秒。Node记录phase=`normal-owner-logout`、failureType=`Error`，对应MJS177行；没有完成browser.json或独立live/final。退出具体失败点未捕获，根因未确认，不能直接归因为身份源或声称已修复认证。

新`installed_ui_observed_logout.mjs`只增加有界诊断：两次退出各记录阶段、最多20条顶层导航method/status/origin/path，query仅记录id_token_hint/post_logout_redirect_uri是否存在。它不保存token/state/query值、cookie、凭据或trace/storageState，不改变产品退出流程、不增加自动重试，也不放宽正常双端退出合同。owner/recovery仍分别通过私有stdin与显式loginVisible凭据登录。新观察器经MJS import进入冻结依赖闭包；新的完整准备联合冻结161份源码。

## 已完成的失败夹具处置，明确不是UI补签

原失败资源保留。另行使用新临时真实管理员经产品API对固定原ID唯一ACK；原容器已按最初sleep600自然exited/0、StartedAt保持。ACK实际释放保护、Outbox 1→0，但首版QA后置证明要求所有receipt/行字段不变，拒绝了bindingCurrent从历史false变为null，因此该收尾attempt也按失败封存，尚未删目标。

核对产品合同：WorkflowService.Acknowledge先对原操作调用Discard取得新观察；WorkflowValidation.RequireCompatibleReceipt固定state/copied/published事实，允许当前绑定观察变化。此次差异仅为SQL和receipt的bindingCurrent false→null，Root/claim/目标/完整HDD及其他receipt内容相等，archiveCopied始终null。不能据此宣称产品复制事实被篡改；这是本次停止夹具收尾证明的过严假设。

续收尾工具仅接受两份固定失败目录及其seal，无任意case/PID/路径入口，不再登录、不发第二次ACK、不Apply/confirm、不重启容器或重绑定实例。专用disposal_ack仅在已证明同StartedAt、自然停止exit0的原QA夹具上允许这唯一绑定观察变化；其余operation、事实、Root/claim/物理/HDD及receipt字段严格相同，且审计必须从0变为恰1、由不同Subject完成。通用发布后门禁的require_ack未放宽。首个API收尾未保存ACK前完整API DTO，续收尾明确标记preAckApiDtoNotCaptured及actualUi=false，不补造该份证据。

`a5a4002f`续收尾unit `whaledeck-qa-archive-disposal-resume-9266669e`、invocation `dd17211272eb440cb4969a7b68fd1454`：success/0，16.046秒。独立live先重新读取当前SQL/审计、原件、目标/空stage和HDD；之后仅在原三把operation锁、原发布后父版本和nofollow FD/完整snapshot通过时，删除本轮目标/空stage，再准确移除原空private、原普通文件及停止的自有容器。Root/claim/HDD保留，原失败seal保持。独立final与第三次当前核验通过，originalGatePassed始终false。

当前总69回收/29恢复/47清理/28归档、活动0，Root/HDD175/161、恢复87、目录98、purge72、archive32。原始故障前基线受限比较及旧业务全行hash保持通过，仅允许固定Worker启动时间变化；2346保护、其余核心、21安装模块/f318和user NOPASSWD保持。源/HDD同device，不签跨物理盘灾备。

## 检查与封存

初版606项完整回收root检查零跳过（38.418秒），含11新重启专项；最终611项零跳过（38.846秒），增加5个固定失败案/自然停止/唯一绑定观察与禁止副本事实变化的专项。8范围、14 CI合同、最终3份恢复Python编译及2份Node语法通过。新增观察候选尚未进行全新真实页面复验；准确a5a4002f CI `42317812051`本轮仍在运行，未签完整CI终态。

全部证据为root目录0700、文件0600，文件数另加manifest：

| 证据 | 文件/冻结源码/真图 | manifest SHA256 |
| --- | --- | --- |
| `container-recycle-archive-published-restart-prep-ec0519ea25474c5a991d237be52f9a24` | 156 / 153 / 0 | `fc876044dee8931711b617dce6bcf49e9bb797e7a4fed4dcde77b27edf23b4ca` |
| `container-recycle-archive-published-restart-141f74cda980432898a1399ce56b6759`（原门禁失败） | 226 / 153 / 8 | `8394c0b238302fb906c3ecc2fca35ac4235eaf0db461f8c5072440dec9af9c35` |
| `archive-published-failure-disposal-96c137da31dc420ea32dd85da15a1f80`（首版收尾失败） | 132 / 126 / 0 | `4fd1ff933f05c3748bcb4cdf30bca3de07e451d99144f1e2a6f028e28277135e` |
| `archive-published-failure-disposal-cd0912d38d1047d3a870023c982c37d3`（续收尾） | 174 / 154 / 0 | `dee29e3542b93b1d0ac2943bd1ef308c60beb32c9a4d2c5494edd97f548e6ca4` |
| `archive-published-restart-observation-prep-8814cd9b07ee41e1a85153ad6b0d9054` | 164 / 161 / 0 | `6140e401be57d94e736e431d4f9a9c5eb9d34ce9e5ae5acde53cf91e8660f4f1` |

下一步先用全新资源复验带退出观察的Worker/自然lease/Unknown/不同管理员页面ACK；通过后再执行Agent重启。继续适用的写后身份变化、来源/共享写入保护、留存/重复来源策略、维护连续性、大文件及其余59项，避免无依据扩展为无限故障组合。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不计完成。
