# 容器关联资源验收

## 测试清单（实施前定义）

- Docker inspect 对照：实际 image ID，Compose project/service，network ID/IPv4/IPv6/gateway/MAC，mount type/name/source/destination/RW，内部声明端口与宿主机映射配置。
- 保持复制配置旧字段兼容；新字段只读，不返回 Env 值、driver options 或原始 Config。旧 Agent 不伪造“无关联”。
- 镜像/网络/卷精确资源标识跳转与反向关联容器；Compose 精确项目跳转，仅注册应用显示应用详情入口。
- 筛选后可显示全部；资源已删除、搜索无结果、旧 Agent、403、迟到响应不能覆盖新页面/失效权限。
- 桌面 1440/1920，断点 1045，手机 390/320；明暗主题、长 digest/IP/挂载路径、抽屉滚动、链接可点、保护资源不可操作。
- 单独视觉截图与边界测量；至少两条非理想路径：删除的资源/权限失效；另查路由快速切换和空端点。
- Release、Unit/Integration、Vitest、Playwright、lint/type/build；工作站只读 native inspect 对照，匿名拒绝，临时认证用户清理。

## 本地验收（2026-10-12 至 2026-10-13）

- Release build：0 warnings / 0 errors；271 Unit + 60 Integration，全部通过。
- Vitest：42 条全部通过，包括资源 ID 精确匹配、路由迟到响应隔离和 403 清空旧行。
- Playwright：最终完整回归 55 条全部通过，本模块 12 条；包含镜像/卷/Compose 往返、注册应用详情、历史监控、容器轮询及管理操作既有场景。
- lint、type-check、生产 build、Python 验收脚本语法与 `git diff --check` 通过。
- headed Edge 截图检查 1440 明色、320 暗色抽屉：长 digest 与 bind source 完整换行；手机关联区 clientWidth/scrollWidth 均为 252，页面/viewport 均为 320。
- 关联区采用分组而非扩宽表格；复制表单沿用旧字段，结构化信息存在时不重复显示旧端口/网络/挂载摘要。资源表格只内部滚动。
- GLM Flash 只读定位，由 GPT 复核、实施与验收；无凭据/私有配置发送。以上本地场景不作为真实 Docker 数据证明，工作站独立结果如下。

## 工作站验收（2026-10-12）

- 运行时源码 `1b06016`，验收脚本修正提交 `f824ebe`（只修正普通用户登录助手参数，不改变服务源码）。dev 部署单元最终 inactive/dead、Result=success、ExecMainStatus=0；11 个核心容器健康，宿主机服务 active。
- 真实管理员 Cookie → API → UDS Agent 与原生 Docker inspect 对照：11 个容器、19 个网络端点、24 个挂载、11 条端口记录全部相同。镜像/网络/命名卷标识可对应真实资源列表，Compose project 可对应项目列表。
- 不执行容器生命周期操作，不创建/修改/删除生产卷；数据路径未变化。本轮主机上的挂载按原生字段验证，命名卷/IPv6/自动分配端口的补充边界由合成 Unit/浏览器场景验证，不冒充此工作站的实际部署内容。
- 匿名 5 个管理接口均 401；无管理员组的临时普通用户真实登录成功，5 个接口均 403。最初验收助手默认要求管理员，参数修正后整套通过；临时账号由现有 wrapper 清理，无清理报错。
- API 容器内复核 Socket 为 997:975 / 0660；普通 user 对运行目录不可遍历且 full sudo 被拒绝。外部稳定入口仍 0.0.0.0:8080/8081，Gateway 仅 127.0.0.1:18080/18081。
- 部署 8 分钟后复核 11 个核心容器持续健康；Worker 近两分钟 fail/Unhandled 错误计数为 0。临时本地浏览器与 5175 测试服务已关闭，Playwright 管理的 5174 服务随测试退出。
