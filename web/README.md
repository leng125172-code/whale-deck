# Whale Deck 前端

Vue3/TypeScript/Element Plus管理界面，复用Whale Deck的BFF Cookie、CSRF/Origin、服务端授权与错误状态。产品范围与完成标准见[当前目标](../docs/GOAL_P0_P5.md)，页面契约见[页面清单](../docs/PAGES.md)。普通用户不会因前端菜单变化取得管理权限。

## 固定工具链与本地验证

使用`package.json`声明的`pnpm@12.10.1`及`pnpm-lock.yaml`；Node版本须满足manifest的engines。不用npm install生成另一套锁文件。

```bash
corepack pnpm install --frozen-lockfile
corepack pnpm dev
```

相关修改完成后按范围执行：

```bash
corepack pnpm type-check
corepack pnpm lint
corepack pnpm format:check
corepack pnpm test:unit
corepack pnpm build
corepack pnpm test:layout
```

浏览器测试需要已安装的Chromium；可以为测试进程设置`PLAYWRIGHT_EXECUTABLE_PATH`使用现有浏览器，或按Playwright工具安装。布局用例采用测试会话/接口，只证明布局与状态，不能代替真实Authentik登录、实际依赖与生产写操作验收。截图保存在忽略的`test-results/`；表格在卡片内横滚，页面在320–1920px不横向溢出。

## 当前追加计划（均待实现/真实验收）

- [视觉规范](../docs/UI_STYLE_GUIDE.md)：用户参考的明暗紧凑指标卡/变更摘要，保留顶栏、主题、响应式和减少动画。
- [C#酒馆与看板娘](../docs/AI_TAVERN_MASCOT.md)：Vue原生聊天/角色/世界书/完整配置，全部看板娘AI通过酒馆。
- [统一供应商与AI驱动Live2D](../docs/AI_PROVIDERS_LIVE2D.md)：统一模型配置/用途绑定，正常对话页必须Live2D，AI结构化动作由C#校验后播放，含停止/睡眠/重连去重/真实口型及16个子门槛。

不把视频/静图回退、模拟模型响应或按钮存在视为功能通过；凭据/OIDC token/私有聊天不放浏览器localStorage。工作站已安装版本与历史证据见[实施状态](../docs/IMPLEMENTATION_STATUS.md)，本说明不是部署完成声明。
