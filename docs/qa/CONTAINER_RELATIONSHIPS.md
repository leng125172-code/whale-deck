# 容器关联资源验收

## 测试清单（实施前定义）

- Docker inspect 对照：实际 image ID，Compose project/service，network ID/IPv4/IPv6/gateway/MAC，mount type/name/source/destination/RW，内部声明端口与宿主机映射配置。
- 保持复制配置旧字段兼容；新字段只读，不返回 Env 值、driver options 或原始 Config。旧 Agent 不伪造“无关联”。
- 镜像/网络/卷精确资源标识跳转与反向关联容器；Compose 精确项目跳转，仅注册应用显示应用详情入口。
- 筛选后可显示全部；资源已删除、搜索无结果、旧 Agent、403、迟到响应不能覆盖新页面/失效权限。
- 桌面 1440/1920，断点 1045，手机 390/320；明暗主题、长 digest/IP/挂载路径、抽屉滚动、链接可点、保护资源不可操作。
- 单独视觉截图与边界测量；至少两条非理想路径：删除的资源/权限失效；另查路由快速切换和空端点。
- Release、Unit/Integration、Vitest、Playwright、lint/type/build；工作站只读 native inspect 对照，匿名拒绝，临时认证用户清理。

## 本地验收（2026-10-12）

- Release build：0 warnings / 0 errors；271 Unit + 60 Integration，全部通过。
- Vitest：42 条全部通过，包括资源 ID 精确匹配、路由迟到响应隔离和 403 清空旧行。
- Playwright：完整回归 53 条通过；补充镜像/卷/Compose 往返和注册应用详情后，本模块 12 条通过（总清单 55 条）。
- lint、type-check、生产 build、Python 验收脚本语法与 `git diff --check` 通过。
- headed Edge 截图检查 1440 明色、320 暗色抽屉：长 digest 与 bind source 完整换行；手机关联区 clientWidth/scrollWidth 均为 252，页面/viewport 均为 320。
- 关联区采用分组而非扩宽表格；复制表单沿用旧字段，结构化信息存在时不重复显示旧端口/网络/挂载摘要。资源表格只内部滚动。
- GLM Flash 只读定位，由 GPT 复核、实施与验收；无凭据/私有配置发送。工作站结果发布后补充，以上不作为真实 Docker 数据证明。
