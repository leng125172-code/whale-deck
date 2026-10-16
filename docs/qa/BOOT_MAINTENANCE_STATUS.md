# 重连后的启动存储与维护状态修复

2026-10-16。优先处理真实重连暴露的 P0 故障，完整76项范围不变；通知并发/取消/未知结果组合继续留在清单。

## 先验收已冻结 UI

`eabac51` 的隔离原生 dispatch 套件实际执行1/通过1且清理，报告 `dispatch-a9597a2b6e5d42f5992af1df22bd132d.json`，00:19:23.583345–00:21:10.598963 UTC，SDK image ID `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`。

Job `1fc7b0ff-e99b-4241-a47e-6d6a8fa7a807` 调度覆盖发布，宿主更新 unit 于08:24:30 CST `Deactivated successfully`，Result success/ExecMainStatus0。真实身份→API→Valkey→Worker→Agent应用/移除与永久重放、渠道版本和发送阻断、管理员/普通/匿名、CSRF/Origin、UDS拒绝及宿主验证通过。临时渠道/任务/用户精确清理、不可变审计保留，外部发送0。8个HTTP资产与Gateway逐字节一致，包含新增私有配置页面；不将其描述成生产身份浏览器走查。

## 实机暴露的问题

MariaDB退出127、非OOM，OCI报告配置文件bind类型错误。Docker 08:04:49启动，而独立HDD的 `data.mount` 08:05:08才就绪。挂载完成后配置文件正常，原容器重新启动即恢复healthy；未改动数据库数据目录、未重建实例。`database-platform` 的 `a3f734a` 为Docker增加RequiresMountsFor启动依赖，实际连续安装2次、Docker进程启动时间不变，Requires/After已包含data.mount，配置root:root/0644；缺失文件bind的隔离原生create拒绝且无残留。源码增加create_host_path:false供以后重建生效，现有容器未因此重建。详见该仓库 `docs/qa/STORAGE_BOOT_DEPENDENCIES.md`；未擅自重启/卸载HDD，不能声称实际重启演练通过。

维护入口另有独立误报：重启清空 `/run` 后，主页和Gateway均200，但 `/maintenance/status` 因文件不存在返回503/GatewayOffline。此前Agent的缺省Ready也不作为当前可达性的证据。

## 当前修复与本地证据

- MaintenanceHost 对不存在的临时状态使用固定 `127.0.0.1:18080/health/live` 探测，不接受用户指定URL，不使用系统代理、Cookie或自动重定向；2秒超时、共享短缓存、请求取消不取消其他调用者探测。
- 显式更新/失败进度在Docker不可用时仍可读取。旧healthy状态不掩盖当前网关/API探测失败。
- 状态文件最多8KiB、JSON最大深度8，只返回已知且有界的公开状态字段；坏JSON、超长/非法状态或不可读返回稳定503，不抛500、不回显原文件内容/路径。响应no-store，维护HTML仍进行编码。
- 探测只证明网关/API可回应，不代替全部数据库/缓存/身份依赖的健康检查，后者另行验证。
- 576 Unit/73 Integration、Release全方案0警告/错误；6原生本机跳过。新增14个用例覆盖无文件/断连/重定向、畸形/超长、显式进度、旧healthy、超时/错误脱敏、并发缓存和取消隔离。
- 原生脚本将启动安装后二进制的独立回环监听和临时状态，不更改生产状态文件、不停Docker，设置无效系统代理验证绕过代理；当前仍须按实际运行结果签收。

GLM/glm-5.3-flash只做机械测试名盘点：首轮正则失败，18064ms，输入22330/缓存256/输出263；限定修正后第二轮成功，16200ms，输入23111/缓存256/输出347。总调用2次，失败1/重试1，费用与资源包扣减未知。GPT依据实机证据作优先级/安全裁决、修改和复核；未委派凭据、权限或核心架构决策。
