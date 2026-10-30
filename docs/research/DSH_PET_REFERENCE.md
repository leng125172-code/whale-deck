# dsh-pet 看板娘参考：固定源码事实与 Whale Deck 改造

2026-10-30后续决定：本文件仍如实描述dsh-pet透明视频实现；Whale Deck看板娘**对话页现明确要求Live2D与AI动作**，按[最新配置与动作契约](../AI_PROVIDERS_LIVE2D.md)实施。dsh-pet继续作为交互/物理/节奏/睡眠参考，不能用其视频替代正常Live2D验收；所有AI调用统一走Whale Deck酒馆及集中供应商配置。

核对日期：2026-10-30。用户指定仓库：[PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)。固定提交 `3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4`；包清单标记0.3.8。本轮通过GitHub只读接口读取文档与下表源码，未安装、运行、调用模型或借用上游测试报告。实施/验收以 [AI_TAVERN_MASCOT.md](../AI_TAVERN_MASCOT.md) 与 [逐项账本](../AI_TAVERN_ACCEPTANCE.md) 为准。

## 事实与取舍

- **运行形式**：React/Cordis插件分host与client，依赖DSH服务；另有Electron桌面窗及独立模式。Whale Deck保留Vue3/Element Plus与.NET10，复用算法/状态设计要做适配，不能把npm包当Vue组件直接接入。页内看板娘无需服务器图形桌面；此次不要求独立桌面宿主。
- **动画**：上游声明100+段640×360透明视频，待机/转向/移动/点击/拖拽/工作/余额/说话/睡眠按动画池衔接。它是视频动画方案，不能把它写成已经具备Live2D骨骼或VRM物理。浏览器媒体加载有超时/取消；需要在目标浏览器验证首帧、透明度和连续播放。
- **浏览器边界**：README称Chromium/Firefox可播放VP9-alpha，Safari透明渲染受限并另提供HEVC/MOV路径。这是上游声明，本轮未跨浏览器运行。Whale Deck须按目标平台实测，并提供格式选择和诚实的无动画回退，不能仅以服务器能下载视频判兼容。
- **交互/物理**：公开纯函数包括松手速度估算、重力/地面摩擦/反弹、软限速、弹簧/挤压、弹弓和宠物互撞；步进夹取dt并细分高速度位移。Vue可保留纯TypeScript数学层，将Pointer/页面边界/动画帧生命周期重新接线；单元数值测试之外须有真实鼠标/触摸、隐藏页恢复、手机旋转及多实例碰撞证据。
- **模型**：碎碎念和对话分别选模型、人设共用，可回退当前模型。生成模块消费DSH的llm流式块后组装成结果，不能据内部使用stream就宣称页面逐token流式。C#版本用统一酒馆编排与持久generation，明确展示实际模型/回退；不把原模块简单的人设+历史当完整世界书/提示管理。
- **记忆/媒体/语音**：上游区分完整保存历史与本次带入轮数，支持图片输入、表情配图和可选TTS；不能将它的“只存不送”图片历史语义误写成多轮视觉记忆。Whale Deck按ST兼容和模型能力明确本轮/历史附件策略、引用/删除与备份，截图和麦克风由用户主动授权。
- **余额**：内置四适配：opencode-go、commandcode、deepseek-official、deepseek-account，并允许外部注册。上游按消费比例六档显示，不支持则文本提示。Whale Deck用C#受控账户/Secret引用和类型化money/points/window-quota；单有token统计不能算余额，无分母不能虚构百分比。此次不启用第三方模型路由或复制开发环境凭据。
- **睡眠**：上游host SleepStore为内存态，重启即醒；服务端整套选择start/loop/end，浏览器按三段播放。睡眠拦截打扰入口并停止随机动作/一般碎碎念/余额/待办/工作气泡；梦话复用AI请求、只弹气泡。拖拽不应被当成点击唤醒；没配完整睡眠套则明确不可睡。Whale Deck增强为持久sleep intent+版本化状态，刷新/进程重启一致；与静音/隐藏/减少动画分别配置，安全告警中心继续运行。
- **额外能力**：工作六档、待办、系统通知、多实例/角色包、右键菜单/快捷键均有文档或源码入口。Whale Deck复用已授权Job/通知事实，不从模型文本猜“操作成功”；多实例有独立资源ID。OS托盘、跨屏窗口不因上游存在而自动加入此次六类要求。
- **访问控制**：上游README说明HTTP挂在本机、无鉴权；Cordis页内服务不是本项目BFF安全边界。不能在Whale Deck暴露同样的开放控制API。上游占用锁可自动唤醒，这一外部接管权限也不能照搬；本项目走明确主体/资源/授权。
- **素材与代码**：LICENSE为MIT，README对素材/提示词/源视频另列非商用，对形象列CC BY-NC-SA4.0。参考机制、复制代码与使用形象是不同来源项；保留必要来源/NOTICE，使用用户有权提供的zilii素材。未下载或复制上游动画/形象，未生成替代美术。

上述事实用于制定移植要求，并非本项目已有功能。ST的Live2D/VRM/音视频扩展仍按独立条目验收，不能用一个视频宠物代替。

## 已读取的固定源文件

以下Git blob SHA用于定位内容；不是Whale Deck构建或运行证明。链接均固定至同一提交，未读取的同目录其他文件不算源码审计完成。

| 文件 | Git blob SHA | 本轮读取 |
| --- | --- | --- |
| [README.md](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/README.md) | `88b04929b8d422170488923886c89a2e27ca69be` | 完整文件 |
| [LICENSE](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/LICENSE) | `7d87632065ae9bbebe4fb7a275f38f3cd1c30c41` | 完整文件 |
| [dsh-pet/LICENSE](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/LICENSE) | `7d87632065ae9bbebe4fb7a275f38f3cd1c30c41` | 完整文件 |
| [dsh-pet/package.json](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/package.json) | `465c037a38f44789feadae3b16690af12ece8f65` | 完整文件 |
| [FEATURES.md](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/FEATURES.md) | `5482062b0afbec5e34997c43177713eb2553422b` | 完整文件 |
| [CONFIG.md](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/CONFIG.md) | `820a3abe5e12a64a01cb286d83f88671bbafa289` | 完整文件 |
| [API.md](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/API.md) | `05f775d5145e16f9cd16441d0822a114ac96fd04` | 完整文件 |
| [dsh-pet/src/client/pet/animation/next-animation.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/client/pet/animation/next-animation.ts) | `01769476c00e1e290e11477f7eb63ca8097c0286` | 完整文件 |
| [dsh-pet/src/client/pet/animation/asset-loader.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/client/pet/animation/asset-loader.ts) | `88156c4b51d5277af644805f423808650606709c` | 完整文件 |
| [dsh-pet/src/shared/physics.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics.ts) | `0caf9840e8eace8d44d9184551db412c12b221df` | 完整文件 |
| [dsh-pet/src/shared/physics/throw.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics/throw.ts) | `ad3eb83398e58bc2248a5bdf1d16d4f50d9d7b00` | 完整文件 |
| [dsh-pet/src/shared/physics/release.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics/release.ts) | `3f5ee3790663b11b0ac30dbda280a41d3b6ea433` | 完整文件 |
| [dsh-pet/src/shared/physics/spring.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/shared/physics/spring.ts) | `c4c6163a3da7d502a75759da6ecc0e6e7a5670d8` | 完整文件 |
| [dsh-pet/src/host/chat.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/chat.ts) | `81ff9dde9be75b158a0a550af017d44590f773ca` | 完整文件 |
| [dsh-pet/src/host/whisper.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/whisper.ts) | `ce36c99337f1f0aaaf0fff3d5d489d5969f9a4ae` | 完整文件 |
| [dsh-pet/src/host/balance/registry.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/balance/registry.ts) | `3641cb2d7bde4e77e5c55e813c70fc6e194d1881` | 完整文件 |
| [dsh-pet/src/host/balance/types.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/balance/types.ts) | `6ce18232358fbf844d0084c9b070d075697d4a9d` | 完整文件 |
| [dsh-pet/src/host/sleep.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/sleep.ts) | `96907f8c760bdf336f151d7922f939eb311f06b5` | 完整文件 |
| [dsh-pet/src/host/routes/sleep.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/routes/sleep.ts) | `e6f46a5ad5e419d0f07c5216aed08ab4810211f4` | 完整文件 |
| [dsh-pet/src/client/pet/card/sleep-machine.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/client/pet/card/sleep-machine.ts) | `ec35c3b63f2edeb69e75c4a62b90b808c668d065` | 完整文件 |
| [dsh-pet/src/host/plugin/loops.ts](https://github.com/PC2005-cloud/dsh-pet/blob/3ec5f5f20db0aac55d8bd616b7f634cfdb4b44e4/dsh-pet/src/host/plugin/loops.ts) | `dd15f9c8da2830cd27c9af66b42348272ee65d38` | 完整文件 |

## 仍需实际验证

必须取得授权角色包和模型/余额真实依赖，再验证浏览器媒体/物理/流式/睡眠/权限/恢复。66个酒馆可安装扩展只核对固定目录；这份桌宠研究不能冒充它们的源码审计。所有计划仍须逐条取得实际UI和后端证据。
