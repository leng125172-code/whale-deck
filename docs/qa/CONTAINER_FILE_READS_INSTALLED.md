# 容器文件只读：已安装 helper 与签名 UDS

日期：2026-10-22。单工作站59项范围，P1-11/P2-03子增量；不是完整文件管理或整个阶段完成。

## 实际发布和验证

- `c083c5d`冻结源码：42项真实Linux-root执行/通过，零跳过；包含真实管道关闭、内核槽释放与原有路径/内容保护。不可变镜像的独立、非root、无网络/只读根测试容器完整读取2097159字节，逐字节和SHA256一致，源身份不变。
- 先仅安装新增引擎和固定helper，两个旧共用模块逐字节匹配原安装。真实Agent UID分别执行list/stat/download，观察实际systemd User/Group、权限、capability、AF_UNIX、只读挂载、资源/进程/文件预算符合源码。完整下载与原件一致，journal无driver/metadata/chunk内容帧。
- `fd3bda2`冻结源码的Agent和MaintenanceHost实际安装；宿主权限/systemd/UDS/动态入口验证通过，NRestarts为0。没有新建或替换API/Worker/Gateway镜像：这些容器仍是`9ec8721`，不能再称六组件同一版本。新宿主DLL逐字节等于本轮冻结发布产物；自包含框架及PKCS均为10.0.13。
- 新验证客户端与宿主均由已锁定SDK镜像构建，公开冻结Git输入，不挂私有checkout；客户端验证其程序集来源等于fixture冻结SHA。首次直接执行未设置执行位的验证构建脚本报EACCES，改用bash正常构建，没有放开文件权限。
- 实际Agent minor38、`docker.files.read`四方法签名UDS：目标/列表/属性及完整2097159字节下载通过；缺签名、跨方法、跨服务、重放、核心资源、敏感路径、符号链接、旧文件版本、旧进程实例和路径穿越均按预期拒绝。没有容器Exec或生产写入。
- `fd3bda2`再次冻结运行引擎/观察器43项真实Linux-root全部通过。前轮42项和本轮43项包含公共测试，不能相加为85项独立覆盖。

## 独立清理与失败记录

每次验证只创建带独占标记的测试容器和私有合成数据目录。删除前核对完整ID、名称、镜像、标签和唯一只读bind；不删除卷。载荷删除前核对目录及每个对象身份、模式、完整内容和符号链接目标。失败保留未证明的文件，不自动重建或重试写入。

第三轮结束后，新SSH独立读取三份原生回执：三个载荷目录均不存在，三个完整容器ID均由Docker API返回404；没有活动读取unit、匿名内容缓存或运行中的本批测试容器，11核心容器healthy、两宿主active。这里只留下私有QA回执和零字节内核槽文件，不把这些当内容缓存。

完整Release门禁曾在旧OperationCoordinator测试中遇到Windows File.Replace瞬时缺名；修正仅限有界持久化轮询，最终仍要求实际文件终态，不吞永久缺失。修正后两个原测试连续10轮通过，最终完整2275 Unit/159 Integration通过、6项真实依赖本机明确跳过。没有改变生产取消/原子发布语义。

## Gateway候选与尚未完成

Application已有独立ContainerFileLocation/Target DTO与只读Gateway接口；Infrastructure使用专用ContainerFileService、major1/minor>=38及显式能力门禁、四个独立方法签名和有界RPC。没有把容器映射成宿主rootId或增加docker.sock。

11项内存gRPC测试覆盖门禁、四方法精确实例/纳秒标识/Unicode映射与签名、NotFound单独映射null，其他拒绝不降级。该Gateway尚未部署或经真实业务调用验证，内存夹具不是UDS证据。

仍需：Application当前管理员复核、元数据审计/流式终态、BFF及当前Cookie撤销、容器文件Vue页面与真实普通/管理员界面；安装态主动断线/强杀读取驱动证明，以及完整写入/HDD历史/恢复/长期清理。不能用本轮只读基础替代这些要求。

CI：`3667fec`和`c083c5d`已终态success；`fd3bda2`最后观察39697812494仍in_progress，未重发。后续Gateway候选CI待提交后观察。

GLM 5.3 Flash本轮仅只读定位现有验证器模式，GPT负责权限、实现和复核。一次成功/零重试，116052ms；输入164003/cache136064/输出2444 tokens，实际费用未返回。其建议不是安装态验收结果。
