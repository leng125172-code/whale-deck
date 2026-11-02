# 递归所有者：底层候选、逐项永久历史与真实 HDD 验证

2026-11-02。候选冻结`ef5425a6f0472a8a52cbd0b267a89feb64403933`，**未安装、未接签名RPC/永久业务/Worker/API/Vue**。实际运行仍为`0dede08`/协议1.49，本批没有发布、服务重启或自动回滚。不能把本页记为递归所有者完整功能或真实人类页面签收。

## 实现与安全边界

新增`whaledeck-container-recursive-ownership.py`，复用有界递归清单/逐项回执框架及既有单对象所有者引擎。共用框架通过仅可信类型构造时可选的严格bool区分变更类型，不接受请求中的类型开关；默认chmod命令、历史和摘要字节域保持原值。构造时核对单项引擎命令字段及聚合历史namespace/resourceType，避免错误接入。

- 命令仅接受准确容器实例、非空规范目录、expectedVersion、显式数字UID/GID；bool、字符串、负数、4294967295及多余权限字段均拒绝。0是显式root目标，不是“保持原值”哨兵。执行进程UID/GID、原属主及请求目标分别绑定。
- 最多10000项、深度32、16MiB清单、总时限300秒，逐项检查取消；双遍清单绑定九字段身份和普通属性摘要，按最深路径优先、目录最后修改。禁止链接、硬链接、特殊文件、跨挂载/别名、特殊权限位、ACL/security属性及受保护路径；旧普通权限保护不放宽。
- 修改前封存整树清单与原属主元数据，每个inode复用单对象所有者计划/HDD历史，实际只执行`fchown`并fsync，不写内容或chmod。No-op不调用fchown，保持ctime。实际依赖为已有Linux FD/openat2和fchown能力、Python标准库、容器root/挂载证据、现有HDD policy/attestation与FileBackups；没有新增通用root命令或sudo授权。
- 独立`container-recursive-ownership-v1`聚合/子历史、resourceType、计划/清单/历史/结果摘要和子操作ID域；不能用chmod清单或历史替代。每个子项目标UID/GID必须与聚合命令一致，完成项必须`ownerConfirmed is True`且当前绑定有效。
- 原子持久startedThrough/finishedThrough与逐项结果；inspect/recover/迟到apply只核对原子回执并封存未确定结果，不续做、不自动恢复旧属主。缺失聚合仍有子回执或HDD遗留时保持Unknown，HDD不可用不作“缺失”证明。Partial/Unknown留给后续业务层保持写入保护和当前管理员明确处置。
- 聚合Root和HDD schemaVersion严格要求整数1，拒绝JSON true冒充1；该加固也适用于候选默认chmod路径，尚未覆盖运行产品。

## 自动化测试

16项新增Linux-root专项，以及所有`test_container_file*.py`合计**421项通过、0跳过**。测试使用隔离临时目录和明确合成HDD attestation，不冒称物理HDD证据。覆盖实际fchown/完整字节/权限/mtime/普通属性、UID/GID独立变更与显式root/no-op、域隔离/错误子引擎、异常输入、链接/特殊位/ACL、清单和预算、实际子进程强杀、回执/历史丢失、重算摘要后子UID篡改、未确认owner结果、schema bool、容量/依赖故障、外部漂移及外来子项不接管。

专项首轮有一个测试调用错误：在类上调用非静态child_request时缺少self；修正测试后通过。不是产品引擎或实盘门禁失败。落盘后8项范围检查通过，208份MD的1258个相对链接零缺失；代码和Python语法/diff检查通过；本批未修改.NET/Vue，不借旧后端/前端全量或CI作为新结果。

## 真实容器与已attest HDD候选

首次完整运行通过。冻结10个host模块及验收器/独立核验器，要求HEAD和传入SHA一致、工作树干净；只读预检全局Job/终端/锁及各类文件意图/保护均0。使用唯一准确自有、无网络、非root1000:1000、只读容器根、限资源和唯一HDD bind的临时容器。不是已装helper调用，不调用产品Agent/RPC或模拟人类登录。

目录树为2文件+2目录，各文件完整2097159字节、普通user属性、混合UID/GID与权限及纳秒mtime。两次真实SIGKILL只发生于运行候选的自有fork子进程，使用可信测试checkpoint，不是线上产品故障接口，不杀核心服务。

| 场景 | 实际结果 | 完成/修改/中止/未开始/未知 |
| --- | --- | --- |
| 全树目标2200:3200 | Completed，当前绑定有效 | 4 / 4 / 0 / 0 / 0 |
| 同目标无变更 | Completed，不调用fchown，九字段不变 | 4 / 0 / 0 / 0 / 0 |
| 第一项完成后SIGKILL，目标2300:3300 | Partial，仅第一文件修改 | 1 / 1 / 0 / 3 / 0 |
| 仅写开始标记后SIGKILL，目标2400:3400 | Aborted，无属主修改 | 0 / 0 / 1 / 3 / 0 |

每次操作/恢复/迟到重放后完整读取所有文件字节与九字段；inode、内容、mode、mtime及普通属性保留，仅目标UID/GID和必要ctime变化。重放前后Root原字节及源树不变。4份聚合HDD及9份子项旧属主元数据保存并校验，不把元数据历史称为完整内容备份或可用恢复入口。

独立只读进程重读Root与实际已attest HDD，逐域重算计划/清单/子计划/历史/结果摘要、目标UID/GID、原版本/实际版本、ownerConfirmed与各计数、前后场景连续性和Root原字节摘要。核对准确CID/目录已清理、11核心容器ID/镜像/状态/StartedAt不变且healthy。它明确不声称清理后重读已删除源字节。永久候选Root在私有证据目录，HDD元数据留存。

源device与HDD device均2049；这只计同设备元数据留存，不计NVMe运行/异盘灾备或HDD故障。没有修改产品Agent最小权限，user NOPASSWD授权保留，没有扩大核心操作权限。

## 原递归权限兼容性

共享框架变更后，另用新独占资源以同一冻结SHA运行原`validate_container_recursive_permissions.py`四场景：2完成/1部分/1中止、2次真实候选子进程SIGKILL，完整字节/原UID/GID/mtime/普通属性及模式目标、迟到不重做通过。其独立只读核验也通过：4聚合/9子项HDD、Root、准确清理及11核心不变且healthy。默认chmod仍为原permission摘要域和资源类型；这不是重跑已部署人类页面链。

## 私有证据与后续

项目图谱同步两次遇连接错误，本地`.project-atlas/pending/whale-deck.container-recursive-ownership.ef5425a.engine.json`已保存，待同一eventId补发；尚未收到网站成功回执，不计已同步。目标/实施状态及本页均已本地更新，阶段保持active。

- 递归所有者：`deploy/artifacts/container-recursive-ownership-38998cf310c2484087693d4978e12260/`。
- 原权限回归：`deploy/artifacts/container-recursive-permissions-ddaadaa6095e481fb83cacb3942046f5/`。

均root/0700、原件0600且不入Git，含冻结源码SHA、baseline/完整源身份、4组请求/结果/前后物理证明、Root与HDD原件、准确清理及verified/independent-final。凭据不进入仓库或日志，无人类登录截图。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_ownership.py ef5425a6f0472a8a52cbd0b267a89feb64403933
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_ownership.py container-recursive-ownership-38998cf310c2484087693d4978e12260
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permissions.py ef5425a6f0472a8a52cbd0b267a89feb64403933
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permissions.py container-recursive-permissions-ddaadaa6095e481fb83cacb3942046f5
```

已有回执，不为展示重复创建资源和真实强杀。两条verify为独立只读核验，未来环境变更应区分运行边界，不改写本批历史。

下一步接独立C#签名RPC/SDK/能力协商与固定传输/安装验证，正常覆盖后实测；再接永久EF业务/共享写入保护/Worker/管理员API/Vue、真实Authentik→实际树/HDD页面及故障矩阵。ACL、可恢复删除、长期清理和P0-05回退来源等其余59项继续。旧17项移出不计完成，master尚未交付。
