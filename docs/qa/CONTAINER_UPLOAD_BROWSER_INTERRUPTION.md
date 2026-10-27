# 真实浏览器部分上传中止与跨页退出

2026-10-27，`validate_container_upload_browser_interruption.py/.mjs` 最终完整执行通过，正常清理结束。程序集/HTTP仍fab6e2e；固定helper已单独安装[只读/宿主清理竞态修复](FILE_TRANSPORT_STOP_RACE.md)，SHA256为 `12ecb9ed79002a46d559f3bd9825f909aab8ad395ecac36d1a366d5ee543e74e`，原服务和11个核心容器没有重启。新文本编辑页面仍未部署。

## 实际控件、网络与独立服务器证据

使用正常可见Authentik用户名、密码和授权控件登录真实已部署页面，不拦截HTTP、不导入Cookie/storageState、不导出Token或trace。仅创建独占无网络/只读根/普通用户的测试容器；浏览器选择真实0600私有文件，每个声明67,108,871字节，通过页面计算摘要、计划与二次确认，每项只有一次实际PUT。

浏览器使用自身CDP上传限速64 KiB/s；这是网络条件，不是模拟响应。独立Python观察器在SQL Receiving、Root Receiving、实际0600 partial inode及完整请求范围一致后，检查已有32,768字节和真实内容前缀。只有观察到实际部分接收后才允许触发页面控件；握手文件不代替任何服务或任务结果。

- 点击“中止本地操作”：CDP原请求实际loadingFailed、canceled=true、net::ERR_ABORTED，没有loadingFinished；Worker最终Failed/CONTAINER_UPLOAD_INTERRUPTED，Root Discarded。页面通过“查询原任务”显示上传失败，未自动重发文件。
- 另一页正常用户菜单退出：原页面的进行中上传同样出现实际CDP取消终态；Worker最终Canceled/CONTAINER_UPLOAD_CANCELED，Root Discarded。两页回登录页，私有文件输入和上传区域清空，Whale Deck和旧Cookie401、Authentik401/403。

两个任务均没有RootStarted、PublishBarrier、PublishedVersion或目标文件，partial/data均移除；原文件完整内容不变。独立只读复核再次匹配SQL终态/审计、Root回执、实际网络结果及已安装helper摘要，测试容器/源目录已不存在。永久SQL、不可变审计和Root journal保留。

CDP观察器只接受指定完整容器ID/任务ID的原PUT，锁定实际requestId，丢弃URL/查询/headers/Cookie/正文和非标准错误文本。快照、超时或dispose都不制造网络终态；已完成请求不能被迟到失败事件改为取消。7项观察器单元全部通过，接入CI；真实门禁脚本Python/Node语法检查通过。

## 失败也保留

前两轮尚未登记上传：第一轮将64 MiB+7作为Playwright内存文件选择，被工具拒绝；改为选择真实私有文件。第二轮目录加载尚未完成就选文件，选择被清除，按钮无法执行；补齐等待实际目录条目显示。原容器/源均精确清理，没有新增上传任务。

第三轮已取得中止的实际CDP/Root/Worker证据，但下一次新上传预检暴露只读任务退出竞态，整轮不计通过。确定性24测试先复现10失败，再修正并全部通过；原子覆盖固定helper、不重启服务。第四轮完整两个场景通过。没有增加预检重试或重新发送正文来绕过故障。

两份失败输入文件在其原容器和源目录均不存在、对应SQL/Root确认未发布之后，分别核对精确私有权限/单链接/全64 MiB+7内容和inode，再只删除这四个已知QA输入；失败日志/截图/永久任务证据保留，未递归或按前缀清理未知数据。最后成功轮的两个输入也在内容核对后精确删除。

日志 `/tmp/wd-container-browser-interruption.log`、`-2.log`、`-3.log`、`-4.log`；最后一个为完整通过。私有资料在 `deploy/artifacts/container-browser-interruption-*`，0700/0600，非公开图谱资料。最终25条永久上传意图：10成功、8取消、7失败；Unknown/活动租约/发布保护均0。临时wrapper用户0，另有1个临时用户由仍在运行的两小时权限门禁持有，不予清理。

本批的页面“中止本地操作”是中断原传输，其结果是明确失败；后台显式取消接口的原连接保持场景已在另一门禁验证，二者不混称。尚需自然两小时权限降级结果、新编辑页面部署及实机编辑、发布交换故障与Worker/Agent重启恢复、容器HDD历史查询/恢复及其余59项；本批不签整个文件模块或P2。
