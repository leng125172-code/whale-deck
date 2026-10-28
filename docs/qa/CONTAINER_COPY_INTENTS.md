# 容器复制：独立计划、永久回执和恢复

2026-10-28，冻结候选 `f1edb437d2f10f1b31a32e333b1c13bc66371dab` 已通过内部真实 Docker 验证。安装态仍为 `5f3622d` / Agent minor44；尚未新增复制签名RPC、业务SQL/Worker/API或Vue操作。这是P1-11/P2-03增量，不是完整文件管理或全部59项完成，17项移出不算完成。

## 产品代码

- `whaledeck-container-copies.py`：容器独立域的有界完整计划、严格结构和字段类型、原主体所有权、永久回执、原操作锁及Plan/Apply/Inspect/Discard。计划固定原容器ID/实例、源路径和容器ETag、源九项身份/父inode、目标父inode/版本、mount/namespace proof、内核fsuid/fsgid和底层完整计划；普通文件只能copy，不能将底层计划改成move/chmod或选择UID。
- `whaledeck-container-copy-scope.py`：支持从已验证冻结计划构造元数据观察范围。目标消失时不能解析容器路径；可读永久回执或写入阻止迟到执行的记录。新会话的实际proof/所有者必须匹配原计划，不重新采信重建或重配置后的容器。
- `whaledeck-container-copy-transport.py`：固定四动作、固定root私有状态路径和相邻可信模块、完整单行stdin/EOF、有界输出、枚举错误码及超时。模块加载要求root所有、普通单链接、不可被组/其他用户写入；不接收宿主路径、模块名或命令。目标明确消失/重启才允许转入元数据观察；普通socket/HDD/注册表异常继续报错。入口尚未加入安装器、受限helper和Agent签名能力，不能把候选传输函数验收称为部署。

公开完成版本由实际目标完整身份派生为**容器域ETag**；底层宿主引擎版本不对外冒充容器版本。Completed后仍保留原历史结果，`bindingCurrent`重新核对当前内容；目录后代被修改，即使顶层目录元数据不变，也会使当前绑定为false。

## 持久恢复规则

1. 首次Apply先对真实容器重新规划并比较全部原计划，永久记录Preparing，然后在调用底层前持久化Running。
2. Preparing被中断、底层尚无回执时，在两层独占锁下证明尚未调用底层，记录Aborted并阻止迟到执行。
3. Running以后回执缺失不足以证明未发布，永久保留Unknown，底层同样封存为Unknown；不重新调用Apply/copy。
4. 底层私有副本仍有准确inode证明时，沿既有恢复逻辑处理。普通文件自有私有副本可以安全清理并Aborted；目录部分树保留，`privateMayExist`提示可能有私有内容。
5. 底层已完成还需核对目标准确inode、完整内容/树摘要和原属性，才能生成容器ETag并持久化Completed。若此时目标失联，保留底层完成原件与外层Unknown，不补造容器版本。
6. 明确Discard在执行前写入永久Aborted；源改变或目标消失都不能使迟到Apply重新执行。Unknown后不自动重新解释为成功，需后续当前管理员的业务处置链。

## 自动化验证

`sudo -n python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_container_copy*.py'`：**40项全部通过、0跳过**，包含原适配层19项、独立回执14项、传输7项。实际Linux隔离文件系统及fork/SIGKILL用于持久性测试，Docker身份/存活在这组测试中为合成。

覆盖完整文件/树/空目录、容器版本隔离、计划只读、所有者/幂等冲突、内层move/权限/版本篡改、重算指纹的错误owner policy、字段bool冒充整数、源改变、父目录和proof变化、丢失目标只观察、前置取消、坏回执/锁链接、目录后代变化以及各个真实候选子进程强杀恢复。

CI原容器文件集294项另行全执行通过、0跳过。新复制通配符与候选脚本语法检查已加入CI。这里的Linux测试不等同当前新增提交的全CI成功。

## 实际 Docker 九项

执行：`sudo -n python3 deploy/validation/validate_container_copy_intents.py f1edb437d2f10f1b31a32e333b1c13bc66371dab`

证据：`deploy/artifacts/container-copy-intents-89807578d6ca4bf2a782b765abe11344/`。

使用冻结Git字节、临时root可信模块、实际ContainerRoot/pidfd/namespace/mount proof和固定传输分发函数。测试镜像已有且不可变；容器无网络、1000:1000、只读根，只允许自有bind数据。未启动/重启任何产品Agent、Worker或核心容器。

| 实际切点 | 原外层回执 | 关键核对 |
|---|---|---|
| 普通文件正常复制 | Completed | 全2MiB+7内容、内核所有者/权限/原mtime、目标inode、容器ETag |
| 普通目录正常复制 | Completed | 全载荷、空目录、元数据与树摘要 |
| 外层Preparing后SIGKILL | Aborted | 尚未调用底层，永久阻止迟到执行 |
| 外层Running后、底层回执前SIGKILL | Unknown | 缺失回执不能当作未执行，不复制 |
| 文件私有副本Prepared、发布前SIGKILL | Aborted | 自有私有inode证明，未发布 |
| 底层完成、外层尚未记录结果SIGKILL | Completed | 完整发布证明，不再次Apply |
| 目录Ready后SIGKILL | Aborted | 完整私有树保留并核对 |
| 目录Published后SIGKILL | Completed | 原发布inode及全树证明，不重新populate |
| 文件Prepared子进程SIGKILL后实际移除准确CID | Unknown | 明确Docker404转元数据观察；完整私有内容及底层Unknown保留，重试不写 |

共七次候选子进程SIGKILL，限于本验证器fork的PID。最终四Completed、三Aborted、二Unknown。每项的原请求/计划、永久外层/底层Root、原件SHA及结果均保留；不把Unknown称作业务成功。

独立`independent-final.json`重新核对11份冻结模块SHA、九份Root原始字节SHA/外层指纹、完成态容器版本、两个准确CID/来源已不存在、11核心healthy。合成目标/私有副本只在准确inode、完整确定性内容、已枚举后代与原属性证明后清理；保留永久历史。现有部署业务未因内部候选产生新的SQL任务或屏障。

## 失败与后续

传输首轮测试两项失败：代码未使用context manager的`__enter__()`返回对象。已改为保留manager并使用进入后的会话，40项全重跑通过；未改变拒绝规则。旧真实ContainerRoot返回self，因此不能将此问题误报为生产容器身份错配。

上一轮CI `40630856569` 的installer失败是项目元数据将“17项移出不算完成”改写成“不计完成”，违反范围检查原文。`3d7db9f`恢复原文后，CI `40631540932` 的backend/frontend/native-postgres/native-valkey/installer五项全部success。新增候选提交的CI单独跟踪，不沿用旧全绿。

继续Agent签名四RPC、独立请求/能力域和协议版本、固定helper预算/可信安装、永久业务/共享屏障/Worker/API及真实身份桌面手机页面。ACL/xattr/特殊权限位、移动/覆盖/删除、完整隔离/长期留存和其他59项仍保留。
