# 退役原生 CLI 与会话适配候选

2026-11-09，候选 `292e8bec86d617eaf35e778ab4a874a28db3939f`。新增独立 `whaledeck-container-recycle-archive-retirement-transport.py`，旧transport、冻结Python与宿主helper均未改动；没有安装、Agent映射或能力升级，运行仍1.59。

## 固定入口与会话

CLI只接受plan/reserve/apply/inspect/discard；Root身份、参数数量及规范InvocationID在bootstrap/读取stdin前检查。19个同目录模块为固定白名单，加载前检查Root所属、常规文件、单链接、无组/其他写入权限与有界大小。状态根均固定：原source/restore/purge/archive保持原路径，退役使用独立Root state/claims；不接受调用方指定模块、状态目录或时钟。

沿原工厂顺序重建copies，再由retention、独立retirement fences和executor组合。Plan校验命令后解析祖先原容器/实例/相对路径，只在固定ContainerRoot会话内预检。其他四动作先校验完整请求，绑定同一session并在finally关闭。仅对明确的目标消失/实例/root变化与已知不安全容器拒绝，将None交给原生内核判断；不把Docker/socket/HDD等泛化异常当作不存在，也不重定向新实例。

None不会授权新的删除：Reserved的Apply/Discard拒绝SESSION_REQUIRED，数据与围栏保留。已完成意图允许在容器消失后返回历史Completed回执，Reserve/Apply/Inspect/Discard均不重放unlink；这与拒绝一切历史成功不同。原内核仍在持锁、当前副本/存储及真实时钟重新校验后才可首次unlink。

## 私有输入与错误

UTF-8正文最多65536字节，另加唯一LF，与C#预算对齐；拒绝缺LF/CR/非对象/重复键、非有限数值、非法编码和超过20层结构。InputLines必须在dispatch前证明EOF，无晚到第二请求；Ready只绑定本次InvocationID，不是版本或资源授权。输入10秒、执行110秒，finally清理alarm；入口自身无scheduler、自动清理或重试。

行读取超限等协议错误投影为固定REQUEST_INVALID，读取超时为TIMEOUT；C#补充5种固定动作/请求/模块/invocation/timeout状态映射。错误输出不泄露路径或任意异常文本，确切退役缺席与祖先缺席保持区别。复核指出HOST_ROOT、EOF trailing-data仍保守投影为generic retirement unavailable；均不dispatch或报告删除成功，暂不作为精细诊断已完成。

## 实际验证

19项新native专项通过：固定动作/验证顺序、session关闭、目标消失与泛化异常分离、65536正文边界、深度/重复/非有限JSON、实际pipe EOF、main定时器和不dispatch、模块权限/链接拒绝，以及真实子进程非法CLI在Ready前退出。隔离临时目录实际完成reserve/cancel、一次unlink/fsync和历史无重放；namespace/存储attestation及到期时钟为明确double，**不是配置HDD、自然老化或安装态RPC正向证据**。

最终完整回收root回归 **814通过、零跳过（89.327秒）**；190项退役C#联合、全量Unit **6102通过、0失败、0跳过（28秒）**，Release全解决方案0警告/0错误，14CI合同/8范围、2源码编译与diff检查通过。初始813完整回归亦保留；增加底层输入诊断后重跑814，不将旧结果替代最终结果。独立只读复核无阻断项，已复核最后quota/timeout分支。

首轮16专项有1失败/2错误：误把逐行read_request当作EOF验证、以及夹具异常类型路径错误；第二轮18专项仍有main参数断言/夹具路径错误。修正测试并保留独立pipe/main EOF验证后18、最终19专项通过，未放宽原生安全政策。失败原始日志未另行封存，只保留实际输出记录和摘要，不重建为原始证据。

原自然老化来源构建后独立重读通过：未到期、未执行、无自动执行，最早仍2026-11-10 10:00:02.252+08:00。原来源/策略/claim/实例/配置HDD及生产部署保持。准确d579 CI运行42559038683中installer/backend/native-postgres/native-valkey/frontend_checks成功，六browser查询时仍运行；本批新CI仍待完整终态，图谱补发失败继续pending。

证据 `retirement-native-transport-8450ddb5f984441988306ea8da40f267`：10文件加manifest，SHA256 `cd24b1d9910f6dd16d6df0a6dc07f8def939edc448a0cb60cca76a74a5d530af`。含4份提交源码、完整6102单测/Release/初始813及最终814日志、实际老化观察和验证摘要；Root:Root/0700目录、0600文件。

## 继续点

继续独立原生探针、固定helper/session沙箱预算和版本安装门禁，再进行标准安装及实际RPC整链；不得提前广告新能力。自然到期受控执行与独立物理证据、永久业务/当前资源授权/共享保护/Worker/持久化/API/Vue/Jobs及其余59项仍待。17项移出不算完成，整项0/59、阶段0/6、工程25%–35%、酒馆/Live2D功能0%保持。
