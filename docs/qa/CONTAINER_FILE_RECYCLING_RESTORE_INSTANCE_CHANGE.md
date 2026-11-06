# 独立恢复已返回原件后的同CID实例变化：实机验收

2026-11-06。工具冻结 `b1a9faa407369d1c50b39e628e278335eae6431c`，生产仍093f44a9。全新资源中，原件已实际返回后，只重启该自有CID一次；旧请求没有改绑新实例，原执行Superseded、Root回执Completed、历史恢复归属true而当前绑定false。清理前后独立核验通过。这里签返回后实例变化及后续正常退出，不签真正Unknown或其他切点。

## 原始范围和重启门禁

真实临时Authentik身份经可见Vue流程完成1来源回收预检/确认和1独立恢复预检/确认，共4个接受的业务POST。精确returned cut固定恢复Root Applying、来源Root Restored、原inode已返回、claim/当前HDD一致；容器Root及恢复锁/来源锁三份内核FD、原helper PID/cgroup/Invocation和停止状态重新核对。

重启前两次检查真实容器、源文件和工作状态，命令前再核对frame及原pidfd。容器为固定自有label、1000:1000、只读根、无网络、CapDrop ALL/no-new-privileges、唯一受控/work bind及固定预算；不能借用另一CID或核心资源。基础5类检查和全部13张意图表的其他活跃/屏障/租约计数为0，独立恢复表也被覆盖；仅豁免本轮正在Applying的精确行及其两个Job，另单独确认该行、租约、Root/claim和已返回证据，不要求故障前已SQL终态。

唯一命令为固定自有CID的docker restart --timeout 1；flag在发命令前消耗，失败不重试。前后CID、名称、镜像、mount、用户/label、安全和资源预算完全相同，只有PID/StartedAt按实际新实例变化。原件不移动、不重写，新namespace的/proc/PID/root/work与宿主目录dev/inode相同，文件完整快照等于已验证的返回后快照（包含返回后的ctime），并用新pidfd及两侧frame重核。

## 实际时序与结果

unit `whaledeck-qa-restore-instance-657611d5` / Invocation `d17816267dd44feb9bc1a66641ecdba1`，exited/Result success/ExecMainCode 1/ExecMainStatus 0。自有容器PID3180251→3184123，StartedAt递增，CID及全部配置保持，重启ack等于原CID，核心快照保持。

本轮原helper PID3182940在重启后仍暂停，按原MainPID/Invocation/三FD/日志重核后，只对同一pidfd发送一次SIGCONT并继续保留该fd，不重新发送Apply。约1.968秒观察到原pidfd结束，计时从重启前开始，包含重启与受控暂停释放；这不是“实例变化主动杀停helper”的证明。若原helper已自行结束，工具另记alreadyExited分支且不向它发信号；本轮实际alreadyExited=false、resumeExactOriginal=true。

helper退出标记被页面确认后才允许原上下文正常BFF退出，重启后退出前me仍200、同主体管理员。随后两次正常BFF/OIDC双端退出、同subject重登，原意图ID/幂等键/原来源三个旧引用404，专属历史读取200；取消/ACK/retry请求为0。普通9个、匿名5个请求拒绝。此顺序把实例结果与后续退出验证分开，不用退出触发原helper结束。

同一意图Superseded，执行Job Failed/phase Superseded及17字段摘要对应CONTAINER_RECYCLE_RESTORE_TARGET_CHANGED。原Root回执Completed、来源Restored、sourceRetained false、sourceRestored/本操作归属/HDD验证和可用均true，bindingCurrent false；租约、屏障和Outbox结清，取消时间为空。真实Jobs页正确显示历史结果变化而非Unknown，通用重试按钮缺席，正确文件页链接存在。

claim、来源Root、HDD history/data与cut逐项保持；本恢复Root从Applying到Completed，按原请求和回执完整验证，不称其终态字节与cut相同。原件仍是原inode、原字节、原属性，在新namespace和宿主目录均可独立读取。Superseded结算后额外保存新实例仍活及原件仍在的时间/frame证明，观测时间不早于SQL完成时间，独立live再次复核。

## 独立清理和封存

只在独立live核验通过后，精确匹配原件的独立快照及原始稳定身份/内容，先检查空私有目录原身份、root:root/0700，再移除本轮文件/私有目录和按fixture合同核对的自有容器/种子。没有递归删除未知内容，没有重绑、回滚或重放原恢复。final继续读取保留的HDD和历史、核对原live证明，不声称读取已删除的源或容器。

9项专项、64项关联Linux root检查全通过、零跳过；Python/Node语法与9项CI分片合同检查通过。专项覆盖错误CID/镜像/mount/权限预算、错误PID/时间、非整数或遗漏恢复表计数、已退出进程不再发信号、单次恢复暂停决策及退出先于BFF动作。离线namespace/SQL提供者有明确替身，与本批实际Auth→Vue→PG/Worker→签名Agent→原inode/HDD/新namespace证据分列；CI新步骤仅检查工具语法，专项沿原installer通配收集，不冒称新产品发布。

私有证据 `deploy/artifacts/container-recycle-restore-instance-fe1ea06e16a84d8494a08a0374862b67`，实际冻结126份源码闭包，共178文件清单摘要 `7ed41716fb7e2cc2af1ac161674d2ae26f5d31b0ad7946e4946c8cabbf71153c`，逐文件复核通过。父目录0700、文件root:root/0600，凭据不入Git、日志或项目图谱。169文件的上轮位移组合证据保持，未复签成实例变化。

最终34回收/20恢复意图、活动0；回收Root68/HDD71、独立恢复Root53。2346保护文件、11核心服务及093f44a9两宿主/PID保持（Agent2974845、Maintenance2974836）。源/HDD仍同设备2049，不称独立物理盘灾备。接下来推进真正Unknown、跨写入组合、长期清理及其余59项；17项移出不算完成，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持，master完整交付尚未完成。
