# 酒馆完整功能逐项移植与验收账本

日期：2026-10-30。用户本次明确纳入范围；**以下全部待实现/验收**，不是功能完成证据。实施方案、权限/数据/失败恢复/真实依赖与页面要求见 [AI_TAVERN_MASCOT.md](AI_TAVERN_MASCOT.md)；原功能与来源见 [完整清单](research/SILLYTAVERN_FEATURE_INVENTORY.md)。

主任务编号保持59项，主要扩大P4-08、P0-08/P5-05和关联交付要求。272个ST ID=206个核心/随包能力+66个可安装扩展目录条目；14个随包目录是能力归属，不再加14项。不能用主任务数量掩盖新增范围。

## 逐项签收方式

每一行均须有：固定源版本与精确行为、C#用例/数据与Vue入口、权限/安全、故障恢复、真实依赖、部署版本和真实页面证据。统一执行主设计的A-IDENTITY/SEMANTICS/RECOVERY/DEPENDENCIES/UI/DELIVERY六组门禁，并补下表的领域用例；C-*只标记用例归属，不代表用例已编写或执行。每个ST ID要单独填实际结果/证据链接，不允许整域批量打勾。

跨批次能力共同完成后才签收：图库/附件/向量/自动化/语音等不能因主要批次先完成就提前通过。ST-ADMIN-01..04采用Authentik/BFF等效语义而非复制本地密码与用户资料，记录差异和身份故障结果。66扩展当前只核实目录存在，逐仓库固定版本/源码/许可/依赖和功能定义仍待；全部纳入DEV-AI-005与008最终验收，不能将“未审核”改成“已兼容”。

## 领域与特定验收

| 领域 | 条目数 | 主要批次 | 领域用例组 | 特定验证内容 |
| --- | ---: | --- | --- | --- |
| CHAR | 14 | DEV-AI-002 | C-CHAR | 角色版本/PNG及JSON卡片/绑定/图库；完整字段及边界依每条原文逐项验证 |
| PER | 8 | DEV-AI-002 | C-PER | 扮演身份/默认绑定/描述注入与导出；完整字段及边界依每条原文逐项验证 |
| CHAT | 13 | DEV-AI-002 | C-CHAT | 消息/候选/继续/代写/流式/上下文排除；完整字段及边界依每条原文逐项验证 |
| HIST | 8 | DEV-AI-002 | C-HIST | 会话/分支/检查点/JSONL及媒体引用；完整字段及边界依每条原文逐项验证 |
| GROUP | 10 | DEV-AI-002 | C-GROUP | 成员/静音/轮次/发言策略与上下文；完整字段及边界依每条原文逐项验证 |
| WI | 18 | DEV-AI-003 | C-WI | 关键词/递归/概率/时效/预算/插槽与向量激活；完整字段及边界依每条原文逐项验证 |
| PROMPT | 10 | DEV-AI-003 | C-PROMPT | 提示模块/作者注释/角色/深度/生成类型；完整字段及边界依每条原文逐项验证 |
| CTX | 10 | DEV-AI-003 | C-CTX | Text及Chat模板/分词/裁剪/停止符/预算；完整字段及边界依每条原文逐项验证 |
| SAMPLE | 8 | DEV-AI-003 | C-SAMPLE | 采样字段及后端支持/拒绝/请求映射；完整字段及边界依每条原文逐项验证 |
| DEBUG | 6 | DEV-AI-003 | C-DEBUG | 真实可返回推理/概率/提示组成/计时；完整字段及边界依每条原文逐项验证 |
| API | 9 | DEV-AI-002 | C-API | 连接/模型发现/能力矩阵/Secret及请求适配；完整字段及边界依每条原文逐项验证 |
| RAG | 9 | DEV-AI-004 | C-RAG | 转换/切块/embedding/检索/注入/作用域；完整字段及边界依每条原文逐项验证 |
| MEM | 5 | DEV-AI-004 | C-MEM | 总结/更新频率/注入/翻译及实际服务；完整字段及边界依每条原文逐项验证 |
| REGEX | 6 | DEV-AI-003 | C-REGEX | 位置/顺序/显示与提示分离/超时及输入输出；完整字段及边界依每条原文逐项验证 |
| MACRO | 7 | DEV-AI-003 | C-MACRO | 宏/变量作用域/转义/命令与补全；完整字段及边界依每条原文逐项验证 |
| SCRIPT | 11 | DEV-AI-003 | C-SCRIPT | 管道/闭包/循环/快捷回复/工具与资源限制；完整字段及边界依每条原文逐项验证 |
| EXT | 10 | DEV-AI-005 | C-EXT | 事件/API/工具/schema/生命周期/隔离及差异；完整字段及边界依每条原文逐项验证 |
| IMG | 8 | DEV-AI-004 | C-IMG | 实际视觉/绘图/工作流/参数/图库；完整字段及边界依每条原文逐项验证 |
| AVATAR | 8 | DEV-AI-004 | C-AVATAR | 表情/视觉小说/Live2D/VRM实际素材与渲染；完整字段及边界依每条原文逐项验证 |
| VOICE | 6 | DEV-AI-004 | C-VOICE | TTS/STT/声音映射/播放/转换和音频依赖；完整字段及边界依每条原文逐项验证 |
| UI | 10 | DEV-AI-006 | C-UI | 原生配置/主题/移动/可访问性/快捷键与安全CSS；完整字段及边界依每条原文逐项验证 |
| ADMIN | 12 | DEV-AI-008 | C-ADMIN | Authentik等效账户/隔离/配置/数据管理/部署恢复；完整字段及边界依每条原文逐项验证 |
| ADDON | 66 | DEV-AI-005 | C-ADDON | 逐扩展固定源码/许可/依赖审计及等效功能/隔离适配；完整字段及边界依每条原文逐项验证 |

## 206个核心/随包能力

逐项“待实现；无验收”意味着C#实现、真实依赖和Vue证据均未签收。来源能力解释以固定清单及原链接为准；未知原始字段必须补源码/上游实例对照，不在移植时静默忽略。

| ST ID | 原清单能力 | 主要批次 / 用例组 | 当前结果 / 证据 |
| --- | --- | --- | --- |
| ST-CHAR-01 | 创建、编辑、删除、复制、重命名角色。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-02 | 角色卡文件导入、导出、替换更新，以及支持来源的链接导入。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-03 | 角色头像与名称管理。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-04 | 人物描述、性格摘要、场景设定。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-05 | 开场白及多条备选开场白。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-06 | 示例对话。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-07 | 角色备注及其插入深度、消息角色。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-08 | 角色级主提示词、历史后指令覆盖。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-09 | 创作者、版本、备注等卡片元数据。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-10 | 角色收藏、标签、筛选、搜索与排序。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-11 | 角色与世界书绑定、角色卡内嵌主世界书。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-12 | 角色 token 占用和使用统计查看。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-13 | 角色转换为用户 Persona。 | DEV-AI-002 / C-CHAR | 待实现；无验收 |
| ST-CHAR-14 | 角色关联图库与媒体资源【随包 Gallery】。 | DEV-AI-002 + DEV-AI-004 / C-CHAR | 待实现；无验收 |
| ST-PER-01 | 多 Persona 创建、切换、编辑与删除。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-02 | 各自的名称、头像、描述与管理标题。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-03 | 指定描述注入位置、深度及消息角色。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-04 | 默认 Persona。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-05 | 绑定当前聊天或指定角色。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-06 | Persona 专属世界书。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-07 | 历史消息身份保留，以及同步重新归属。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-PER-08 | Persona 配置备份与恢复；图片和聊天绑定并非全部包含在该配置备份中。 | DEV-AI-002 / C-PER | 待实现；无验收 |
| ST-CHAT-01 | 发送文本与接收回复。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-02 | 流式显示、停止生成【外部：后端支持】。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-03 | 重新生成与多候选答案 swipe 切换。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-04 | 继续未完成的回复。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-05 | 让模型代写用户发言（Impersonate）。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-06 | 编辑、复制、删除、移动消息。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-07 | 批量删除消息。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-08 | 消息排除出上下文及重新纳入。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-09 | 消息附件、图片及媒体显示【含随包扩展】。 | DEV-AI-002 + DEV-AI-004 / C-CHAT | 待实现；无验收 |
| ST-CHAT-10 | Markdown、代码块与引用等消息格式。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-11 | 回复耗时、token 等消息信息显示。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-12 | 自动继续、自动 swipe 等生成辅助设置【后端条件】。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-CHAT-13 | 输入快捷键、自动补全、消息显示与滚动设置。 | DEV-AI-002 / C-CHAT | 待实现；无验收 |
| ST-HIST-01 | 每个角色或群组的多个聊天档案。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-02 | 新建、选择、重命名、删除聊天。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-03 | 从某条消息创建分支并切换。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-04 | 创建命名检查点，保留父聊天关联。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-05 | 从检查点返回父聊天。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-06 | JSONL 导出与重新导入，保留聊天元数据；不包含所有图片和附件。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-07 | 纯文本导出；该格式不能完整还原聊天。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-HIST-08 | 导入若干其他聊天工具的记录；Character.AI 导入另需 CAI Tools。 | DEV-AI-002 / C-HIST | 待实现；无验收 |
| ST-GROUP-01 | 建立群聊、添加/移除/排序成员。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-02 | 成员静音及强制指定发言。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-03 | 手动选择发言者。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-04 | 自然顺序：名字提及、健谈程度与随机选择。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-05 | 按成员列表顺序发言。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-06 | 池化顺序，优先选择本轮未发言成员。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-07 | 自动对话及允许角色回应自身。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-08 | 群组场景覆盖。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-09 | 轮换注入当前发言者卡片，或拼接成员卡片。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-GROUP-10 | 拼接时配置静音成员参与、前后缀；共享群聊历史。 | DEV-AI-002 / C-GROUP | 待实现；无验收 |
| ST-WI-01 | 世界书与条目创建、编辑、排序、导入、导出。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-02 | 全局、角色、Persona、单次聊天四类绑定范围。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-03 | 条目内容、标题/备注、启用开关。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-04 | 多个关键词、正则表达式关键词。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-05 | 辅助过滤词及 AND/ALL/NOT 等匹配逻辑。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-06 | 常驻条目、关键词激活及概率触发。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-07 | 大小写与完整词匹配。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-08 | 扫描消息深度、是否包含名字、附加匹配来源。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-09 | 递归激活、最大递归步骤。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-10 | 插入顺序、位置、深度及 system/user/assistant 角色。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-11 | Outlet 命名插槽注入。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-12 | 多来源世界书的合并优先策略。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-13 | token/百分比预算、最少激活数、溢出提示。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-14 | 包含组、优先级、权重与组评分。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-15 | 角色/标签过滤、生成类型限制。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-16 | 持续、冷却、延迟等时效规则。 | DEV-AI-003 / C-WI | 待实现；无验收 |
| ST-WI-17 | 向量相似度激活【随包 Vector Storage + 外部 embedding】。 | DEV-AI-003 + DEV-AI-004 / C-WI | 待实现；无验收 |
| ST-WI-18 | Automation ID 触发 Quick Reply 脚本【随包联动】。 | DEV-AI-003 + DEV-AI-004 / C-WI | 待实现；无验收 |
| ST-PROMPT-01 | 主提示词、辅助提示词、历史后指令。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-02 | 角色、Persona、世界书、示例对话、历史的组合。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-03 | 提示词模块添加、编辑、启停和排序。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-04 | 模块消息角色、插入深度与相同深度排序。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-05 | 按普通生成、继续、代写、swipe、重生成等场景配置触发。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-06 | 场景、性格、世界书等内容的格式包装。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-07 | 新聊天、群聊、示例对话的分隔提示。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-08 | 群聊发言者引导、继续提示与后缀。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-09 | 空消息替代与角色名字处理策略。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-PROMPT-10 | 聊天专属与默认作者注释，配置位置、深度、注入频率。 | DEV-AI-003 / C-PROMPT | 待实现；无验收 |
| ST-CTX-01 | Text Completion 与 Chat Completion 两种提示词组织方式。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-02 | Context Template / Story String。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-03 | Instruct 模板、输入/输出序列和分隔符。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-04 | 系统提示词模板及自动匹配相关设置。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-05 | 回复预填前缀、自定义停止字符串。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-06 | 上下文与回复 token 预算。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-07 | 历史裁剪、示例对话保留策略及可见上下文范围。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-08 | tokenizer 自动匹配和手动选择。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-09 | 本地/API 分词与 token padding【按后端可用性】。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-CTX-10 | 角色、消息、完整提示词 token 统计与组成查看。 | DEV-AI-003 / C-CTX | 待实现；无验收 |
| ST-SAMPLE-01 | Temperature 与动态温度。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-02 | Top-K、Top-P、Min-P、Typical-P、Top-A。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-03 | 重复惩罚及范围/斜率、DRY。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-04 | Tail Free、平滑采样、Epsilon/Eta Cutoff。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-05 | XTC、Mirostat、Beam Search、Top-nsigma。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-06 | Logit Bias 及其他提供商支持的参数。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-07 | 采样顺序与生成参数预设【后端条件】。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-SAMPLE-08 | CFG 正/负提示及混合设置【仅部分后端】。 | DEV-AI-003 / C-SAMPLE | 待实现；无验收 |
| ST-DEBUG-01 | 后端返回的推理内容或摘要显示、折叠和计时；不保证能取得隐藏推理。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-DEBUG-02 | 推理格式解析、手动添加、编辑与上下文处理设置。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-DEBUG-03 | Reasoning effort 等提供商特定选项。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-DEBUG-04 | Prompt Itemization，检查最终提示词组成和 token。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-DEBUG-05 | Logprobs/候选 token 概率及受支持的局部续写操作。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-DEBUG-06 | 生成与扩展的调试工具、缓存和统计检查。 | DEV-AI-003 / C-DEBUG | 待实现；无验收 |
| ST-API-01 | 本地模型服务器、云模型服务及聚合服务适配。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-02 | Chat Completion 与 Text Completion 连接。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-03 | 服务器地址、密钥选择、模型发现或手动模型 ID。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-04 | 连接状态检测与测试消息。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-05 | 自定义 OpenAI-compatible 请求头、请求字段及后处理。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-06 | 消息角色合并、严格格式、单用户消息等转换。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-07 | 连接配置保存、更新、重载、删除和快速切换。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-08 | 配置关联模型、密钥选择、预设、模板、分词器、停止字符串与推理格式；不等于复制所有预设内部值。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-API-09 | 支持的多模态输入/输出和模型工具功能选项【具体依提供商】。 | DEV-AI-002 / C-API | 待实现；无验收 |
| ST-RAG-01 | 全局、角色、聊天三个资料作用域。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-02 | 单条消息附件。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-03 | 手动编写/编辑文本资料。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-04 | TXT、Markdown、HTML、ePUB、文本 PDF 等文件转换。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-05 | 网页正文提取、YouTube 字幕来源；可用性受来源限制。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-06 | 文档分块、向量化、相似度检索及检索内容注入。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-07 | 从聊天历史召回相关消息。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-08 | embedding 来源选择【外部模型或本地模型运行能力】。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-RAG-09 | Office/LibreOffice 文档转换【另需服务器插件】。 | DEV-AI-004 / C-RAG | 待实现；无验收 |
| ST-MEM-01 | 聊天自动/手动总结。 | DEV-AI-004 / C-MEM | 待实现；无验收 |
| ST-MEM-02 | 总结提示词、更新频率、摘要注入与编辑。 | DEV-AI-004 / C-MEM | 待实现；无验收 |
| ST-MEM-03 | 使用主模型等支持的总结后端。 | DEV-AI-004 / C-MEM | 待实现；无验收 |
| ST-MEM-04 | 用户输入与 AI 回复的手动/自动翻译。 | DEV-AI-004 / C-MEM | 待实现；无验收 |
| ST-MEM-05 | 聊天文本翻译与多翻译服务配置。 | DEV-AI-004 / C-MEM | 待实现；无验收 |
| ST-REGEX-01 | 正则脚本创建、编辑、导入、导出和删除。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-REGEX-02 | 全局规则与角色卡作用域规则互转。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-REGEX-03 | 规则排序与启停。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-REGEX-04 | 输入、AI 输出、命令、世界书、推理等应用位置。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-REGEX-05 | 深度限制、宏替换及仅显示/出站提示词等作用方式。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-REGEX-06 | 输入/输出对照测试与 STscript 联动。 | DEV-AI-003 / C-REGEX | 待实现；无验收 |
| ST-MACRO-01 | 用户名、角色名、角色字段与 Persona 字段替换。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-02 | 历史消息、运行状态等上下文宏。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-03 | 时间、日期、随机选择等宏。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-04 | 聊天局部变量、全局变量与读写操作。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-05 | 参数、嵌套、注释与转义等宏语法；新宏引擎细项按固定版本验证。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-06 | 命令和宏自动补全、帮助查询。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-MACRO-07 | 通过斜杠命令操作聊天、角色、世界书、模型连接、媒体和扩展。 | DEV-AI-003 / C-MACRO | 待实现；无验收 |
| ST-SCRIPT-01 | 命令批处理与管道传值。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-02 | 命名/位置参数、变量、数组和对象。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-03 | 条件判断、循环及中断。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-04 | 闭包、作用域、参数及过程调用。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-05 | 数学、文本处理和 token 裁剪。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-06 | 输入框、弹窗、选择按钮与提示消息。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-07 | 调用模型、临时角色、提示词注入。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-08 | 读写聊天消息、角色与世界书内容。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-09 | 快捷回复按钮和预设管理。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-10 | 快捷回复执行文本、命令或脚本，支持上下文菜单与自动执行场景。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-SCRIPT-11 | 脚本调试及扩展命令调用。 | DEV-AI-003 / C-SCRIPT | 待实现；无验收 |
| ST-EXT-01 | 扩展注册函数工具与参数 schema。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-02 | 模型请求工具、执行扩展逻辑、回传结果【支持的模型/API】。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-03 | 工具调用递归轮数限制与相关推理上下文设置。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-04 | 扩展事件、上下文 API、聊天元数据及设置持久化。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-05 | 注册宏、斜杠命令和 UI。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-06 | 消息格式化阶段钩子。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-07 | 扩展安装、启停、更新、删除与更新通知。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-08 | 从资源目录或 Git URL 安装扩展，支持用户级/全局安装条件。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-09 | 服务器插件加载、增加 HTTP 路由及退出清理。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-EXT-10 | 从资源目录下载角色卡、背景、声音等素材【随包 Assets】。 | DEV-AI-005 / C-EXT | 待实现；无验收 |
| ST-IMG-01 | 图片上传、图像描述与描述模板。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-02 | 调用支持的视觉模型理解图片；具体多模态输入能力取决于连接。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-03 | 本地/云端绘图服务连接。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-04 | 按角色、用户、故事、最近消息、背景或自由提示绘图。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-05 | 绘图提示词生成与编辑、负面提示、角色前缀及风格预设。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-06 | 命令/交互/函数工具触发绘图【相应条件】。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-07 | 图片候选切换、聊天内显示及图库浏览。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-IMG-08 | ComfyUI 工作流、参数占位符和自定义工作流配置【外部 ComfyUI】。 | DEV-AI-004 / C-IMG | 待实现；无验收 |
| ST-AVATAR-01 | 聊天背景图片与视觉小说显示模式【核心】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-02 | 角色表情立绘、上传和 ZIP 导入【随包】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-03 | 手动表情与基于分类/模型的自动表情【随包；外部模型条件】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-04 | 自定义情绪、默认回退、同表情多图与素材文件夹覆盖。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-05 | Live2D 模型绑定、缩放、位置和拖动【可安装 + 模型资源】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-06 | Live2D 视线、口型、动作/表情映射及点击交互【模型条件】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-07 | VRM 模型、动作、视线、眨眼、口型与命中区域交互【可安装 + 模型资源】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-AVATAR-08 | 视觉小说增强、动态音乐与文字音效【可安装扩展】。 | DEV-AI-004 / C-AVATAR | 待实现；无验收 |
| ST-VOICE-01 | 文本朗读、多 TTS 服务与系统语音选择【随包】。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-VOICE-02 | 按角色分配声音与回退声音。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-VOICE-03 | 自动朗读/手动播放、播放控制及文本处理设置。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-VOICE-04 | 麦克风语音识别与文本输入【可安装；浏览器或外部 STT】。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-VOICE-05 | RVC 声音转换【可安装；另需适用服务，注意旧 Extras 依赖】。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-VOICE-06 | 背景音乐、环境音与文字音效【可安装】。 | DEV-AI-004 / C-VOICE | 待实现；无验收 |
| ST-UI-01 | 主题保存、复制、切换、导入和导出。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-02 | 颜色、字体比例、聊天宽度、模糊、阴影与自定义 CSS。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-03 | 头像形状、平铺/气泡/文档聊天样式。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-04 | 媒体列表/图库显示方式。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-05 | 移动端布局、面板位置及输入体验设置。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-06 | 减少动画、关闭模糊与阴影等选项。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-07 | 界面语言、设置搜索、提示位置和消息通知音。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-08 | 键盘快捷键、输入格式辅助与自动补全。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-09 | 消息序号、时间、生成耗时、操作按钮等显示控制。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-UI-10 | 调试菜单、缓存/存储清理及用户数据统计。 | DEV-AI-006 / C-UI | 待实现；无验收 |
| ST-ADMIN-01 | 可选多用户账户与每用户数据、设置和扩展。 | DEV-AI-001 + DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-02 | 管理员/普通用户、账号创建/停用等管理；不是细粒度企业 RBAC。 | DEV-AI-001 + DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-03 | 登录界面、隐藏用户列表、账户密码及基本个人资料。 | DEV-AI-001 + DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-04 | 可信反向代理条件下的 Authelia/Authentik SSO 自动登录。 | DEV-AI-001 + DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-05 | 用户数据备份下载、设置快照/恢复及设置重置。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-06 | 聊天/设置备份、缩略图和遗留数据清理。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-07 | 数据根目录、用户内容初始化与资源管理。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-08 | config.yaml、命令行参数与环境变量配置。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-09 | 监听、TLS、白名单、可信代理、CORS、速率限制等设置。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-10 | 请求代理、缓存、性能、日志和缩略图设置。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-11 | 扩展和服务器插件开关与安装策略。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |
| ST-ADMIN-12 | Docker、远程访问、反向代理和更新渠道支持。 | DEV-AI-008 / C-ADMIN | 待实现；无验收 |

## 66个可安装扩展：全部纳入逐项核实、迁移和兼容验收

每个原仓库都要补：固定提交、许可、原API/DOM/事件依赖、功能/数据语义、外部服务、C#/Vue迁移或隔离适配路线、已知差异、权限/失败恢复/更新/卸载及真实页面。只有固定目录证据，不能说已阅读或运行这66个扩展。原扩展名是范围锚点，现阶段不凭名字编造实现能力。

| ST ID | 扩展及原仓库 | 主要批次 / 用例组 | 当前结果 / 证据 |
| --- | --- | --- | --- |
| ST-ADDON-01 | Prome Visual Novel Extension；[仓库](https://github.com/Bronya-Rand/Prome-VN-Extension) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-02 | Guinevere UI Extension；[仓库](https://github.com/Bronya-Rand/Guinevere-UI-Extension) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-03 | ProbablyTooManyTabs；[仓库](https://github.com/IceFog72/SillyTavern-ProbablyTooManyTabs) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-04 | Group Greetings；[仓库](https://github.com/SillyTavern/Extension-GroupGreetings) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-05 | Duplicate Finder；[仓库](https://github.com/SillyTavern/Extension-DupeFinder) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-06 | Group SendAs；[仓库](https://github.com/SillyTavern/SillyTavern-GroupSendAs) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-07 | Dynamic Audio；[仓库](https://github.com/SillyTavern/Extension-Audio) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-08 | Blip；[仓库](https://github.com/SillyTavern/Extension-Blip) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-09 | Idle；[仓库](https://github.com/SillyTavern/Extension-Idle) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-10 | Speech Recognition；[仓库](https://github.com/SillyTavern/Extension-Speech-Recognition) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-11 | Timelines；[仓库](https://github.com/SillyTavern/SillyTavern-Timelines) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-12 | RVC；[仓库](https://github.com/SillyTavern/Extension-RVC) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-13 | Live2d；[仓库](https://github.com/SillyTavern/Extension-Live2d) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-14 | Objective；[仓库](https://github.com/SillyTavern/Extension-Objective) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-15 | D&D Dice；[仓库](https://github.com/SillyTavern/Extension-Dice) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-16 | HypeBot；[仓库](https://github.com/SillyTavern/Extension-HypeBot) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-17 | Parameter Randomizer；[仓库](https://github.com/SillyTavern/Extension-Randomizer) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-18 | Web Search；[仓库](https://github.com/SillyTavern/Extension-WebSearch) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-19 | EmulatorJS；[仓库](https://github.com/SillyTavern/SillyTavern-EmulatorJS) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-20 | Variable Viewer；[仓库](https://github.com/LenAnderson/SillyTavern-Variable-Viewer) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-21 | VRM；[仓库](https://github.com/SillyTavern/Extension-VRM) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-22 | Quick Persona；[仓库](https://github.com/SillyTavern/Extension-QuickPersona) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-23 | Silence Player；[仓库](https://github.com/SillyTavern/Extension-Silence) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-24 | Code Runner；[仓库](https://github.com/SillyTavern/Extension-CodeRunner) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-25 | Push Notifications；[仓库](https://github.com/SillyTavern/SillyTavern-PushNotifications) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-26 | Chat Top Bar；[仓库](https://github.com/SillyTavern/Extension-TopInfoBar) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-27 | Emoji Picker；[仓库](https://github.com/SillyTavern/Extension-EmojiPicker) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-28 | Notebook；[仓库](https://github.com/SillyTavern/Extension-Notebook) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-29 | Chess；[仓库](https://github.com/SillyTavern/SillyTavern-Chess) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-30 | Screen Share；[仓库](https://github.com/SillyTavern/Extension-ScreenShare) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-31 | Prompt Inspector；[仓库](https://github.com/SillyTavern/Extension-PromptInspector) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-32 | Image Metadata Viewer；[仓库](https://github.com/SillyTavern/Extension-ImageMetadataViewer) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-33 | WebLLM；[仓库](https://github.com/SillyTavern/Extension-WebLLM) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-34 | Mermaid；[仓库](https://github.com/SillyTavern/Extension-Mermaid) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-35 | Weather；[仓库](https://github.com/SillyTavern/Extension-Weather) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-36 | RSS；[仓库](https://github.com/SillyTavern/Extension-RSS) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-37 | LaTeX；[仓库](https://github.com/SillyTavern/Extension-LaTeX) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-38 | Inject Manager；[仓库](https://github.com/SillyTavern/Extension-InjectManager) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-39 | CodeMirror；[仓库](https://github.com/SillyTavern/Extension-CodeMirror) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-40 | Sampler Commands；[仓库](https://github.com/SillyTavern/Extension-SamplerCommands) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-41 | Message Limit；[仓库](https://github.com/SillyTavern/Extension-MessageLimit) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-42 | Lorem Ipsum；[仓库](https://github.com/SillyTavern/Extension-LoremIpsum) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-43 | Typing Indicator；[仓库](https://github.com/SillyTavern/Extension-TypingIndicator) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-44 | Custom Scenario；[仓库](https://github.com/bmen25124/SillyTavern-Custom-Scenario) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-45 | Script Events；[仓库](https://github.com/SillyTavern/Extension-ScriptEvents) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-46 | Message Summarize；[仓库](https://github.com/qvink/SillyTavern-MessageSummarize) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-47 | Magic Translation；[仓库](https://github.com/bmen25124/SillyTavern-Magic-Translation) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-48 | Character Creator；[仓库](https://github.com/bmen25124/SillyTavern-Character-Creator) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-49 | World Info Recommender；[仓库](https://github.com/bmen25124/SillyTavern-WorldInfo-Recommender) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-50 | Roadway；[仓库](https://github.com/bmen25124/SillyTavern-Roadway) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-51 | Flowchart；[仓库](https://github.com/bmen25124/SillyTavern-Flowchart) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-52 | WTracker；[仓库](https://github.com/bmen25124/SillyTavern-WTracker) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-53 | Custom Sliders；[仓库](https://github.com/SillyTavern/Extension-CustomSliders) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-54 | Quick Context Buttons；[仓库](https://github.com/SillyTavern/Extension-QuickContextSize) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-55 | Spotify；[仓库](https://github.com/SillyTavern/Extension-Spotify) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-56 | Guided Generations；[仓库](https://github.com/Samueras/GuidedGenerations-Extension) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-57 | World Info Bulk Mover；[仓库](https://github.com/leandrojofre/SillyTavern-WI-Bulk-Mover) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-58 | Pronouns；[仓库](https://github.com/Wolfsblvt/SillyTavern-Pronouns) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-59 | Message Collapser；[仓库](https://github.com/Crystaria/SillyTavern-MessageCollapser) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-60 | World Info Function Call；[仓库](https://github.com/Culpeo/SillyTavern-WI-FunctionCall) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-61 | Memory Books；[仓库](https://github.com/aikohanasaki/SillyTavern-MemoryBooks) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-62 | World Info Info；[仓库](https://github.com/aikohanasaki/SillyTavern-WorldInfoInfo) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-63 | Character Locks；[仓库](https://github.com/aikohanasaki/SillyTavern-CharacterLocks) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-64 | World Info Locks；[仓库](https://github.com/aikohanasaki/SillyTavern-WorldInfoLocks) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-65 | Lorebook Ordering；[仓库](https://github.com/aikohanasaki/SillyTavern-LorebookOrdering) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |
| ST-ADDON-66 | Model Injection；[仓库](https://github.com/aikohanasaki/SillyTavern-ModelInjection) | DEV-AI-005 / C-ADDON | 待源码审计/迁移；无验收 |

## 看板娘附加验收（不与ST条目重复计数）

| PET ID | 批次 | 必须验证 | 当前结果 / 证据 |
| --- | --- | --- | --- |
| PET-ANIM-01 | DEV-AI-006 | 真实透明动画/素材/切换/加载失败/解码与无障碍 | 待实现；无验收 |
| PET-PHYS-01 | DEV-AI-006 | 点击/拖拽/抛掷/弹簧挤压/弹弓/碰撞/手机/边界 | 待实现；无验收 |
| PET-WHISPER-01 | DEV-AI-006 | 真实模型/人设/周期与手动/去重/权限/预算/失败 | 待实现；无验收 |
| PET-CHAT-01 | DEV-AI-006 | 同一ST会话/流式/停止/恢复/附件/真实页面 | 待实现；无验收 |
| PET-BALANCE-01 | DEV-AI-007 | 实际余额/额度及单位窗口/未知不支持/隐私/失败 | 待实现；无验收 |
| PET-SLEEP-01 | DEV-AI-006 | 三段睡眠/醒来/拖动不误醒/静音/梦话/持久恢复 | 待实现；无验收 |

DEV-AI-008必须逐行确认上述272+6项证据，外部依赖未提供或未启用的行继续待条件；不能将它们计为通过或据此签收完整移植。
