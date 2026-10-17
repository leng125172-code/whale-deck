# 目录移动执行引擎：源码基础，尚未安装/开放

2026-10-18。线上仍为6c5b1d6/Agent1.27。新增`whaledeck-tree-moves.py`是内部执行引擎，不是完整用户功能；尚无移动RPC、固定helper枚举、Agent能力声明、BFF任务/锁owner/人类审计或WebUI。目录回收、HDD目录恢复和ZIP/TAR归档仍待实现。完整76项保持active。

## 实现与边界

- 目录移动计划封装独立域分离的目标复制计划、源父目录UInt64身份和外层指纹。源必须具备list/download/mutate/tree-move，目标必须具备upload/mutate/tree-move；copy和move授权不互相替代。仅在私有解析器接受新枚举，不修改默认根、不授予新动作，不开放CLI或任意宿主路径。
- 旧复制引擎抽取可信代码常量：域、状态目录、源/目标动作集。旧默认值和旧计划/签名JSON不变，黄金摘要与既有完整Linux回归验证；新内部子类使用独立`tree-move-copies`回执，不借用公开copy的操作命名空间。
- 先完整复制/全SHA/元数据校验/不覆盖原子发布目标，重新核对源清单和当前目标，然后逐文件移入源文件系统内根所有0700私有保留树。每项检查原完整身份、版本、全SHA、源路径及祖先绑定；私有保留后再检查原inode/字节，绝不unlink原文件。
- 目录仅按相反顺序核对原inode/模式/归属后rmdir。迟到的新文件、保护名、挂载、链接或替换目录导致拒绝/Unknown，不把整个源祖先rename进私有区，不递归删除源目录。未参与计划的成员不被祖先操作带走。检查是乐观逐项校验，不宣称能对外部不协作进程提供原子文件系统快照；竞争后的不确定对象留存，不盲目继续。
- 移动操作内核锁、独立复制操作锁和现有全局namespace门闩串行化写入。外层锁不递归取得同一namespace锁；等待结果不重复物理发布。已有子回执但门闩忙时保留原非终态并返回Busy；已开始而子回执缺失时Unknown，不能因没看到回执就自动重新复制。
- 小型Root回执最大32KiB，完整私有清单最大16MiB，最多10,000项/32层/16GiB。清单不进入公共帧。新私有写入按实际UTF-8编码预算原子落盘/fsync；首次清单RENAME_NOREPLACE，已有证据不被覆盖。源散列逐块有界，不能误套旧普通文件上传1GiB上限。
- 公共结果分别携带destinationPublished、sourceRemoved、sourceRetained和partialRetained。Completed说明原任务完成，原文件保留在私有树，不代表之后无人改写目标；完整保留不是“部分失败”，也不是HDD备份或独立容灾副本。
- 查验/重复请求不恢复copy、rename或rmdir。只有源路径缺失、原私有inode/完整成员/全字节仍对应清单、当前目标仍对应原发布证明时，才可在最后rmdir之后的崩溃恢复中证明Completed；其他中途结果Unknown/Aborted并保留证据。未知状态不自动回滚、不清掉私有原件；显式取消是原操作栅栏，不把已完成移动改成Canceled。

## 实际验证

- 最终工作站独立root代码副本执行30项新移动测试，36.975s，全部OK且没有skip；150项既有文件测试、26项既有目录复制测试和3项诊断测试也通过。合计209，其中原文件/复制176、移动30、诊断3。
- 实际1,073,741,831字节（1GiB+7）单文件完整移动/全SHA、目标独立inode、原inode私有保留；二进制/中文/空目录、400个中文成员且清单大于64KiB、普通权限/mtime、目标登记UID/GID通过。
- 实际不同设备tmpfs目标、同设备源bind mount拒绝、真实bind别名目标重叠拒绝。两者是隔离文件系统证据，不是物理NVMe/HDD或生产目录迁移证明。
- 真实fork/SIGKILL覆盖Preparing、CopyStaging/Ready/Publishing/Published、DestinationCompleted、SourceValidated/Prepared/Retaining/Retained/Removing/Removed、Completed；另在最后空源根rmdir后强杀，后续查验只证明结果、重复请求不再变更成员。没有使用模拟kill或生产故障注入。
- 迟到根/嵌套保护文件、迟到普通文件、空目录替换、源叶替换/源祖先替换、目标字节被改、根策略撤权、取消、操作/全局锁竞争、owner/计划重绑定、清单损坏、回执mode/硬链接/状态/布尔/整数损坏、重复拒绝FD不泄漏通过。没有删除不确定的保留原件。
- 本地Release/分析器及935 Unit/120 Integration通过；6个原生依赖用例明确跳过，不能算作已执行的SQL证据。C#业务与migration未变；本批源码CI另外记录，不用上一批CI冒称本批通过。
- 实测传输包`tree-move-source-20261018-v5.tar` SHA256 `febb91299c2b2ec1001a65fe8b8bf3134e2f60811e9a4ee68e94ef24b4d92a3b`；核心移动模块SHA256 `cdda887582cdeff2f173b0f671f2b53caafc76f43f8dd0f46737937c82886563`。包为既有Git源码加4个明确文件覆盖，测试没有读取生产环境文件或挂载生产数据给新执行器。
- 4份自有`/tmp`验证源码副本按原传输包逐字节、成员白名单、UID/设备/inode/无链接/无挂载核对，合计3,722个源码/匹配pycache文件后精确移除。原传输包保留可重新展开；不删除业务数据、安装态、SQL审计或Root回执。

## 保留的失败与下一步

- 首轮19项通过；新增27项时，锁测试错误要求“子回执缺失”仍报Busy，实际保守返回Unknown。拆成有回执锁忙/无回执未知两个真实场景，最终30项通过；没有把Unknown改成成功或自动重试来迎合测试。
- 首次apply_patch上下文不符时整批拒绝，无部分落地；修正实例digest方法上下文后实施。传输包的LIBARCHIVE.creationtime提示只是GNU tar忽略Windows扩展元数据，真正测试命令exit0另行确认。
- GLM/glm-5.3-flash仅只读定位两份既有文档/复制测试：输入79,002、缓存57,856、输出1,937，83,189ms，1成功/0重试，费用未返回。GPT独立设计、实现、复核并执行安全/恢复测试。
- 下一步：独立Plan/Apply/Inspect/Discard protobuf与Agent/helper固定路由、方法级签名和重放保护、根策略/资源预算/安装态探针；再接异步计划、持久SQL任务/层级锁owner、Worker/人类审计与WebUI。不能把引擎测试当作这些层已经完成。
