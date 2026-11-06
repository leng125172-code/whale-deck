# 独立恢复写后实时撤权：两轮全新资源实机验收

2026-11-06。工具修订 `05d3ae4a81f9b432550a7464c5b02c46bb9fb554`，生产保持已完整发布验收的093f44a9。移组、账号停用分别使用全新自有容器/文件/真实临时身份，通过可见Vue操作、实际持久化/Worker/签名Agent和HDD，清理前后独立核验均通过。候选与隔离测试不能替代本页实机证据。

## 两个成功场景

| 场景 | unit / 准确Invocation | 原helper退出观测 |
| --- | --- | --- |
| group-removal | whaledeck-qa-restore-postreturn-group-d2739bd3 / c3be0d5645ed4235b74590669bf5541b | 5.390秒 |
| account-disable | whaledeck-qa-restore-postreturn-disable-7dd25633 / 48f8f44980204ea99bb92cab293aebf2 | 5.478秒 |

两个unit均exited/Result success/ExecMainCode 1/ExecMainStatus 0。先实际回收来源，再在页面生成并确认独立恢复；精确三内核FD/Invocation/pidfd切点要求本恢复日志Applying、原来源Restored、原inode已返回、成功claim及当前HDD数据匹配。授权变更前独立读取当前HDD，执行权限仍余10分钟以上。

只变更本轮临时所有者的组或active状态，身份源确认前后原helper均活，再用原pidfd观察退出。计时从授权变更前开始，不伪称从变更确认时刻开始。最终同一执行Succeeded/Completed，bindingCurrent/本操作恢复归属/HDD验证均true，成功claim保留；取消时间空，取消POST/审计和原所有者退出POST均0。已经发生的原件返回不误签为写前Canceled/Aborted。

移组后原意图403、me200且同主体非管理员，实际页面403；停用后原意图403、me403，实际登录页。两种私有提示和原操作面板清空。不同真实管理员只读原恢复及17字段Job，管理写入0，完成正常BFF/OIDC双端退出；各普通9个、匿名5个请求拒绝。两轮独立live核验实际返回inode/内容/属性、Root/claim/HDD通过后才精确清理；final核验继续读取HDD、历史和原live证明，不声称读取已经删除的源文件。

## 原失败与修复不得补签

首次启动unit whaledeck-qa-restore-postreturn-group-c56905f3 / Invocation52047d44534340b3a55cf910c3bcdadc，被source_closure内部Git的dubious ownership检查拒绝，在创建fixture/身份前结束，生产完整基线保持。之后只对验收进程和子进程设置精确仓库safe.directory与GIT_OPTIONAL_LOCKS=0；未改全局Git配置、仓库所有权或用户NOPASSWD授权。

首个实际移组unit whaledeck-qa-restore-postreturn-group-5a8ebbba / Invocation7b61b30702c94d419dd3ae19aa7a5bfd，总门禁失败。它已完成真实写后移组、原pidfd5.556秒退出、Succeeded/Completed、页面/权限/正常观察者退出及独立live核验，但清理误拿恢复前完整元数据比较，合法rename改变ctime导致失败；资源按设计留存。不能用这些分项补签原总门禁。

修改代码前再次运行原只读live verifier，核对实际原件与已独立验证的返回快照，确认仅ctime变化；随后单独QA收尾，仅移除精确验证的自有返回文件、空私有目录、原自有容器和按生成合同核对的fixture种子。旧Root/HDD、业务行及失败记录保持，没有重放产品恢复或执行新恢复RPC。收尾独立前后基线通过，只签自有资源清理，不改原总失败。

05d3ae4a修正清理判定：实际文件须精确等于独立返回快照，同时原identity前8项、内容/属性/大小不变，ctime为整数且不早于初始；空私有目录的原dev/inode、root所有权和0700在任何删除前核对。新增2个保护测试，拒绝新inode、旧验证快照、字节/属性变化或留存副本；68项相关Linux root检查全通过、零跳过。之后以上两个新unit才分别以全新资源完整通过。

## 证据与最终保持

| 私有证据目录（deploy/artifacts下） | 文件数 / 清单SHA256 |
| --- | --- |
| container-recycle-restore-postreturn-revocation-fe82cb01d0d54eaf9352195a3055d1ad | 160 / d506f7383c530795f0c5ec6cf4f62d8eea2eb475b7976172056a3c34d1443310 |
| container-recycle-restore-postreturn-revocation-c5d9a5365ea346bd81c6da9ab9d491f4 | 160 / 523d23933fdcf340d518f1b79405ab20404afbf2c1d46842ce51c2dd99717574 |
| container-recycle-restore-postreturn-revocation-2fd6c15442f940aab8ccc38325477b8d（原总失败） | 158 / 3c88be0a711e996e38845b2745aee3ca0ab86cb4bce1d066d44847873f3d65fd |
| container-recycle-restore-postreturn-owned-cleanup-7304537caa5442c4954f7656bc7d7bfa（独立收尾） | 6 / 761e31801ff06354cc4aa0b0be2fa3d42350a5e0a9344562869b3e8fe75b1f78 |
| container-recycle-restore-postreturn-revocation-launch-97aadbf41de0410d95bd866a567ecfd1（启动拒绝） | 4 / 8da72a290d6697a2eb5134386eca421e9ae688800727f5b60a7c7a60c5628cb6 |

实际逐文件校验通过，均父目录0700、文件root:root/0600。两个成功场景及原总失败各冻结其实际版本116份源码闭包，原失败仍绑定6925be4b，修复后成功绑定05d3ae4a；凭据不入Git、日志或项目图谱。

最终32回收/18恢复意图（包含保留的失败历史），全部活动0；回收Root64/HDD67、独立恢复Root47，原所有业务行按完整SHA保留。2346保护文件、11核心容器及两宿主版本/PID保持：Agent2974845、Maintenance2974836，均093f44a9。源与HDD设备号仍为2049，不称独立物理盘灾备。临时身份按上下文核对删除，自有容器和目录全部精确清理。

接下来推进按真实事实区分Superseded/Unknown的目标/实例变化、跨写入、长期清理及其余59项；17项移出不算完成。整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持，本批不代表可部署master完整交付。
