# SillyTavern 酒馆功能清单

核对日期：2026-10-30。用户现已明确要求完整酒馆前后端纳入 Whale Deck：C# 重构服务端编排，Vue 原生集成调用与配置。本清单是当前功能范围依据，已纳入现有59个主任务的子计划；不是已实现或验收证明。此前“仅讨论、不纳入范围”的限制已被本次决定覆盖。

实施、权限、安全、依赖与真实页面验收见 [AI_TAVERN_MASCOT.md](../AI_TAVERN_MASCOT.md)；本清单全部272个唯一ST ID（206核心/随包能力+66目录扩展）逐项映射见 [AI_TAVERN_ACCEPTANCE.md](../AI_TAVERN_ACCEPTANCE.md)。14个随包模块不再重复计数，66个扩展全部纳入核实与迁移/兼容验收；目前只核实目录存在，不能声称已逐个审计或兼容。

## 范围与标记

- 核心与随包目录基线：稳定版 [1.19.0](https://github.com/SillyTavern/SillyTavern/releases/tag/1.19.0)，提交 `7e8663cd9c184a550b37238218bdd32c6efc68e9`。
- 功能解释参考官方滚动文档，其仓库快照为 `ed5423d809c6b10b8c1e9ef4a0c8ddc7f2ef7f3d`；不能把滚动文档的所有细项都当成已逐一验证的稳定版行为。移植时还需逐项对照固定源码和测试。
- 【核心】：酒馆主程序能力。【随包】：稳定版内置的扩展，可启停。【可安装】：另行安装的扩展，可能由官方或社区作者维护。【外部】：需要相应模型、API、浏览器能力或外部服务。
- 下文按用户可使用的能力拆项；不把每个按钮、宏、斜杠命令和每家供应商的模型版本分别算成一个功能。完整命令随扩展变化，可在运行实例用 `/help slash` 查询。
- 所有第三方仓库构成开放集合，无法给出永久穷尽的总表；本清单覆盖核心功能域、全部 14 个稳定版随包扩展，以及所固定的官方资源目录中全部 66 个可安装扩展条目。资源目录收录不等于官方作者或兼容性背书。

## 01 角色卡与角色库【核心】

来源：[角色管理](https://docs.sillytavern.app/usage/characters/)、[角色设计](https://docs.sillytavern.app/usage/core-concepts/characterdesign/)、[标签](https://docs.sillytavern.app/usage/core-concepts/tags/)。

- ST-CHAR-01：创建、编辑、删除、复制、重命名角色。
- ST-CHAR-02：角色卡文件导入、导出、替换更新，以及支持来源的链接导入。
- ST-CHAR-03：角色头像与名称管理。
- ST-CHAR-04：人物描述、性格摘要、场景设定。
- ST-CHAR-05：开场白及多条备选开场白。
- ST-CHAR-06：示例对话。
- ST-CHAR-07：角色备注及其插入深度、消息角色。
- ST-CHAR-08：角色级主提示词、历史后指令覆盖。
- ST-CHAR-09：创作者、版本、备注等卡片元数据。
- ST-CHAR-10：角色收藏、标签、筛选、搜索与排序。
- ST-CHAR-11：角色与世界书绑定、角色卡内嵌主世界书。
- ST-CHAR-12：角色 token 占用和使用统计查看。
- ST-CHAR-13：角色转换为用户 Persona。
- ST-CHAR-14：角色关联图库与媒体资源【随包 Gallery】。

## 02 用户身份 Persona【核心】

来源：[Personas](https://docs.sillytavern.app/usage/core-concepts/personas/)。

- ST-PER-01：多 Persona 创建、切换、编辑与删除。
- ST-PER-02：各自的名称、头像、描述与管理标题。
- ST-PER-03：指定描述注入位置、深度及消息角色。
- ST-PER-04：默认 Persona。
- ST-PER-05：绑定当前聊天或指定角色。
- ST-PER-06：Persona 专属世界书。
- ST-PER-07：历史消息身份保留，以及同步重新归属。
- ST-PER-08：Persona 配置备份与恢复；图片和聊天绑定并非全部包含在该配置备份中。

## 03 对话与消息操作【核心】

来源：[聊天操作](https://docs.sillytavern.app/usage/chatting/)、[用户设置](https://docs.sillytavern.app/usage/user-settings/)。

- ST-CHAT-01：发送文本与接收回复。
- ST-CHAT-02：流式显示、停止生成【外部：后端支持】。
- ST-CHAT-03：重新生成与多候选答案 swipe 切换。
- ST-CHAT-04：继续未完成的回复。
- ST-CHAT-05：让模型代写用户发言（Impersonate）。
- ST-CHAT-06：编辑、复制、删除、移动消息。
- ST-CHAT-07：批量删除消息。
- ST-CHAT-08：消息排除出上下文及重新纳入。
- ST-CHAT-09：消息附件、图片及媒体显示【含随包扩展】。
- ST-CHAT-10：Markdown、代码块与引用等消息格式。
- ST-CHAT-11：回复耗时、token 等消息信息显示。
- ST-CHAT-12：自动继续、自动 swipe 等生成辅助设置【后端条件】。
- ST-CHAT-13：输入快捷键、自动补全、消息显示与滚动设置。

## 04 会话档案与分支【核心】

来源：[聊天文件管理](https://docs.sillytavern.app/usage/core-concepts/chatfilemanagement/)。

- ST-HIST-01：每个角色或群组的多个聊天档案。
- ST-HIST-02：新建、选择、重命名、删除聊天。
- ST-HIST-03：从某条消息创建分支并切换。
- ST-HIST-04：创建命名检查点，保留父聊天关联。
- ST-HIST-05：从检查点返回父聊天。
- ST-HIST-06：JSONL 导出与重新导入，保留聊天元数据；不包含所有图片和附件。
- ST-HIST-07：纯文本导出；该格式不能完整还原聊天。
- ST-HIST-08：导入若干其他聊天工具的记录；Character.AI 导入另需 CAI Tools。

## 05 多角色群聊【核心】

来源：[群聊](https://docs.sillytavern.app/usage/core-concepts/groupchats/)。

- ST-GROUP-01：建立群聊、添加/移除/排序成员。
- ST-GROUP-02：成员静音及强制指定发言。
- ST-GROUP-03：手动选择发言者。
- ST-GROUP-04：自然顺序：名字提及、健谈程度与随机选择。
- ST-GROUP-05：按成员列表顺序发言。
- ST-GROUP-06：池化顺序，优先选择本轮未发言成员。
- ST-GROUP-07：自动对话及允许角色回应自身。
- ST-GROUP-08：群组场景覆盖。
- ST-GROUP-09：轮换注入当前发言者卡片，或拼接成员卡片。
- ST-GROUP-10：拼接时配置静音成员参与、前后缀；共享群聊历史。

## 06 世界书 / Lorebook / World Info【核心；部分扩展联动】

来源：[世界书](https://docs.sillytavern.app/usage/core-concepts/worldinfo/)。

- ST-WI-01：世界书与条目创建、编辑、排序、导入、导出。
- ST-WI-02：全局、角色、Persona、单次聊天四类绑定范围。
- ST-WI-03：条目内容、标题/备注、启用开关。
- ST-WI-04：多个关键词、正则表达式关键词。
- ST-WI-05：辅助过滤词及 AND/ALL/NOT 等匹配逻辑。
- ST-WI-06：常驻条目、关键词激活及概率触发。
- ST-WI-07：大小写与完整词匹配。
- ST-WI-08：扫描消息深度、是否包含名字、附加匹配来源。
- ST-WI-09：递归激活、最大递归步骤。
- ST-WI-10：插入顺序、位置、深度及 system/user/assistant 角色。
- ST-WI-11：Outlet 命名插槽注入。
- ST-WI-12：多来源世界书的合并优先策略。
- ST-WI-13：token/百分比预算、最少激活数、溢出提示。
- ST-WI-14：包含组、优先级、权重与组评分。
- ST-WI-15：角色/标签过滤、生成类型限制。
- ST-WI-16：持续、冷却、延迟等时效规则。
- ST-WI-17：向量相似度激活【随包 Vector Storage + 外部 embedding】。
- ST-WI-18：Automation ID 触发 Quick Reply 脚本【随包联动】。

## 07 提示词与作者注释【核心】

来源：[Prompt Manager](https://docs.sillytavern.app/usage/prompts/prompt-manager/)、[作者注释](https://docs.sillytavern.app/usage/core-concepts/authors-note/)。

- ST-PROMPT-01：主提示词、辅助提示词、历史后指令。
- ST-PROMPT-02：角色、Persona、世界书、示例对话、历史的组合。
- ST-PROMPT-03：提示词模块添加、编辑、启停和排序。
- ST-PROMPT-04：模块消息角色、插入深度与相同深度排序。
- ST-PROMPT-05：按普通生成、继续、代写、swipe、重生成等场景配置触发。
- ST-PROMPT-06：场景、性格、世界书等内容的格式包装。
- ST-PROMPT-07：新聊天、群聊、示例对话的分隔提示。
- ST-PROMPT-08：群聊发言者引导、继续提示与后缀。
- ST-PROMPT-09：空消息替代与角色名字处理策略。
- ST-PROMPT-10：聊天专属与默认作者注释，配置位置、深度、注入频率。

## 08 文本格式、上下文与 tokenizer【核心；部分依赖后端】

来源：[高级格式](https://docs.sillytavern.app/usage/core-concepts/advancedformatting/)、[Instruct](https://docs.sillytavern.app/usage/core-concepts/instructmode/)、[Tokenizer](https://docs.sillytavern.app/usage/prompts/tokenizer/)。

- ST-CTX-01：Text Completion 与 Chat Completion 两种提示词组织方式。
- ST-CTX-02：Context Template / Story String。
- ST-CTX-03：Instruct 模板、输入/输出序列和分隔符。
- ST-CTX-04：系统提示词模板及自动匹配相关设置。
- ST-CTX-05：回复预填前缀、自定义停止字符串。
- ST-CTX-06：上下文与回复 token 预算。
- ST-CTX-07：历史裁剪、示例对话保留策略及可见上下文范围。
- ST-CTX-08：tokenizer 自动匹配和手动选择。
- ST-CTX-09：本地/API 分词与 token padding【按后端可用性】。
- ST-CTX-10：角色、消息、完整提示词 token 统计与组成查看。

## 09 生成参数与采样【核心设置；实际效果依赖模型后端】

来源：[Common Settings](https://docs.sillytavern.app/usage/common-settings/)、[CFG](https://docs.sillytavern.app/usage/prompts/cfg/)。

- ST-SAMPLE-01：Temperature 与动态温度。
- ST-SAMPLE-02：Top-K、Top-P、Min-P、Typical-P、Top-A。
- ST-SAMPLE-03：重复惩罚及范围/斜率、DRY。
- ST-SAMPLE-04：Tail Free、平滑采样、Epsilon/Eta Cutoff。
- ST-SAMPLE-05：XTC、Mirostat、Beam Search、Top-nsigma。
- ST-SAMPLE-06：Logit Bias 及其他提供商支持的参数。
- ST-SAMPLE-07：采样顺序与生成参数预设【后端条件】。
- ST-SAMPLE-08：CFG 正/负提示及混合设置【仅部分后端】。

## 10 推理输出与生成检查【核心；依赖后端】

来源：[Reasoning](https://docs.sillytavern.app/usage/prompts/reasoning/)、[用户设置](https://docs.sillytavern.app/usage/user-settings/)、[1.19.0 发布说明](https://github.com/SillyTavern/SillyTavern/releases/tag/1.19.0)。

- ST-DEBUG-01：后端返回的推理内容或摘要显示、折叠和计时；不保证能取得隐藏推理。
- ST-DEBUG-02：推理格式解析、手动添加、编辑与上下文处理设置。
- ST-DEBUG-03：Reasoning effort 等提供商特定选项。
- ST-DEBUG-04：Prompt Itemization，检查最终提示词组成和 token。
- ST-DEBUG-05：Logprobs/候选 token 概率及受支持的局部续写操作。
- ST-DEBUG-06：生成与扩展的调试工具、缓存和统计检查。

## 11 模型连接与配置【核心 + 随包 Connection Profiles】

来源：[API Connections](https://docs.sillytavern.app/usage/api-connections/)、[OpenAI 兼容接口](https://docs.sillytavern.app/usage/api-connections/openai/)、[连接配置](https://docs.sillytavern.app/usage/core-concepts/connection-profiles/)。

- ST-API-01：本地模型服务器、云模型服务及聚合服务适配。
- ST-API-02：Chat Completion 与 Text Completion 连接。
- ST-API-03：服务器地址、密钥选择、模型发现或手动模型 ID。
- ST-API-04：连接状态检测与测试消息。
- ST-API-05：自定义 OpenAI-compatible 请求头、请求字段及后处理。
- ST-API-06：消息角色合并、严格格式、单用户消息等转换。
- ST-API-07：连接配置保存、更新、重载、删除和快速切换。
- ST-API-08：配置关联模型、密钥选择、预设、模板、分词器、停止字符串与推理格式；不等于复制所有预设内部值。
- ST-API-09：支持的多模态输入/输出和模型工具功能选项【具体依提供商】。

## 12 资料库与 RAG【随包 Data Bank / Vector Storage】

来源：[Data Bank](https://docs.sillytavern.app/usage/core-concepts/data-bank/)、[历史向量化](https://docs.sillytavern.app/extensions/chat-vectorization/)。

- ST-RAG-01：全局、角色、聊天三个资料作用域。
- ST-RAG-02：单条消息附件。
- ST-RAG-03：手动编写/编辑文本资料。
- ST-RAG-04：TXT、Markdown、HTML、ePUB、文本 PDF 等文件转换。
- ST-RAG-05：网页正文提取、YouTube 字幕来源；可用性受来源限制。
- ST-RAG-06：文档分块、向量化、相似度检索及检索内容注入。
- ST-RAG-07：从聊天历史召回相关消息。
- ST-RAG-08：embedding 来源选择【外部模型或本地模型运行能力】。
- ST-RAG-09：Office/LibreOffice 文档转换【另需服务器插件】。

## 13 总结与翻译【随包；依赖模型或翻译服务】

来源：[Summarize](https://docs.sillytavern.app/extensions/summarize/)、[Translation](https://docs.sillytavern.app/extensions/translation/)。

- ST-MEM-01：聊天自动/手动总结。
- ST-MEM-02：总结提示词、更新频率、摘要注入与编辑。
- ST-MEM-03：使用主模型等支持的总结后端。
- ST-MEM-04：用户输入与 AI 回复的手动/自动翻译。
- ST-MEM-05：聊天文本翻译与多翻译服务配置。

## 14 正则文本处理【随包 Regex】

来源：[Regex](https://docs.sillytavern.app/extensions/regex/)。

- ST-REGEX-01：正则脚本创建、编辑、导入、导出和删除。
- ST-REGEX-02：全局规则与角色卡作用域规则互转。
- ST-REGEX-03：规则排序与启停。
- ST-REGEX-04：输入、AI 输出、命令、世界书、推理等应用位置。
- ST-REGEX-05：深度限制、宏替换及仅显示/出站提示词等作用方式。
- ST-REGEX-06：输入/输出对照测试与 STscript 联动。

## 15 宏、变量、斜杠命令【核心】

来源：[Macros](https://docs.sillytavern.app/usage/core-concepts/macros/)、[Slash Commands](https://docs.sillytavern.app/usage/core-concepts/slashcommands/)。

- ST-MACRO-01：用户名、角色名、角色字段与 Persona 字段替换。
- ST-MACRO-02：历史消息、运行状态等上下文宏。
- ST-MACRO-03：时间、日期、随机选择等宏。
- ST-MACRO-04：聊天局部变量、全局变量与读写操作。
- ST-MACRO-05：参数、嵌套、注释与转义等宏语法；新宏引擎细项按固定版本验证。
- ST-MACRO-06：命令和宏自动补全、帮助查询。
- ST-MACRO-07：通过斜杠命令操作聊天、角色、世界书、模型连接、媒体和扩展。

## 16 STscript 与快捷回复【核心 + 随包 Quick Replies】

来源：[STscript](https://docs.sillytavern.app/usage/st-script/)。

- ST-SCRIPT-01：命令批处理与管道传值。
- ST-SCRIPT-02：命名/位置参数、变量、数组和对象。
- ST-SCRIPT-03：条件判断、循环及中断。
- ST-SCRIPT-04：闭包、作用域、参数及过程调用。
- ST-SCRIPT-05：数学、文本处理和 token 裁剪。
- ST-SCRIPT-06：输入框、弹窗、选择按钮与提示消息。
- ST-SCRIPT-07：调用模型、临时角色、提示词注入。
- ST-SCRIPT-08：读写聊天消息、角色与世界书内容。
- ST-SCRIPT-09：快捷回复按钮和预设管理。
- ST-SCRIPT-10：快捷回复执行文本、命令或脚本，支持上下文菜单与自动执行场景。
- ST-SCRIPT-11：脚本调试及扩展命令调用。

## 17 工具调用与扩展宿主【核心；部分依赖后端】

来源：[Function Calling](https://docs.sillytavern.app/for-contributors/function-calling/)、[UI 扩展](https://docs.sillytavern.app/for-contributors/writing-extensions/)、[Server Plugins](https://docs.sillytavern.app/for-contributors/server-plugins/)。

- ST-EXT-01：扩展注册函数工具与参数 schema。
- ST-EXT-02：模型请求工具、执行扩展逻辑、回传结果【支持的模型/API】。
- ST-EXT-03：工具调用递归轮数限制与相关推理上下文设置。
- ST-EXT-04：扩展事件、上下文 API、聊天元数据及设置持久化。
- ST-EXT-05：注册宏、斜杠命令和 UI。
- ST-EXT-06：消息格式化阶段钩子。
- ST-EXT-07：扩展安装、启停、更新、删除与更新通知。
- ST-EXT-08：从资源目录或 Git URL 安装扩展，支持用户级/全局安装条件。
- ST-EXT-09：服务器插件加载、增加 HTTP 路由及退出清理。
- ST-EXT-10：从资源目录下载角色卡、背景、声音等素材【随包 Assets】。

## 18 图片、视觉与图库【随包；部分依赖外部服务】

来源：[图像生成](https://docs.sillytavern.app/extensions/stable-diffusion/)、[图像描述](https://docs.sillytavern.app/extensions/captioning/)。

- ST-IMG-01：图片上传、图像描述与描述模板。
- ST-IMG-02：调用支持的视觉模型理解图片；具体多模态输入能力取决于连接。
- ST-IMG-03：本地/云端绘图服务连接。
- ST-IMG-04：按角色、用户、故事、最近消息、背景或自由提示绘图。
- ST-IMG-05：绘图提示词生成与编辑、负面提示、角色前缀及风格预设。
- ST-IMG-06：命令/交互/函数工具触发绘图【相应条件】。
- ST-IMG-07：图片候选切换、聊天内显示及图库浏览。
- ST-IMG-08：ComfyUI 工作流、参数占位符和自定义工作流配置【外部 ComfyUI】。

## 19 立绘、视觉小说与看板娘【核心 + 随包 + 可安装】

来源：[Visual Novel](https://docs.sillytavern.app/usage/user-settings/visual-novel/)、[Character Expressions](https://docs.sillytavern.app/extensions/expression-images/)、[Live2D](https://docs.sillytavern.app/extensions/live2d/)、[VRM](https://docs.sillytavern.app/extensions/vrm/)。

- ST-AVATAR-01：聊天背景图片与视觉小说显示模式【核心】。
- ST-AVATAR-02：角色表情立绘、上传和 ZIP 导入【随包】。
- ST-AVATAR-03：手动表情与基于分类/模型的自动表情【随包；外部模型条件】。
- ST-AVATAR-04：自定义情绪、默认回退、同表情多图与素材文件夹覆盖。
- ST-AVATAR-05：Live2D 模型绑定、缩放、位置和拖动【可安装 + 模型资源】。
- ST-AVATAR-06：Live2D 视线、口型、动作/表情映射及点击交互【模型条件】。
- ST-AVATAR-07：VRM 模型、动作、视线、眨眼、口型与命中区域交互【可安装 + 模型资源】。
- ST-AVATAR-08：视觉小说增强、动态音乐与文字音效【可安装扩展】。

## 20 语音与音频【随包 + 可安装；外部能力】

来源：[TTS](https://docs.sillytavern.app/extensions/tts/)、[Speech Recognition](https://docs.sillytavern.app/extensions/speech-recognition/)、[RVC](https://docs.sillytavern.app/extensions/rvc/)。

- ST-VOICE-01：文本朗读、多 TTS 服务与系统语音选择【随包】。
- ST-VOICE-02：按角色分配声音与回退声音。
- ST-VOICE-03：自动朗读/手动播放、播放控制及文本处理设置。
- ST-VOICE-04：麦克风语音识别与文本输入【可安装；浏览器或外部 STT】。
- ST-VOICE-05：RVC 声音转换【可安装；另需适用服务，注意旧 Extras 依赖】。
- ST-VOICE-06：背景音乐、环境音与文字音效【可安装】。

## 21 界面、设置与可访问性【核心】

来源：[UI Customization](https://docs.sillytavern.app/usage/core-concepts/uicustomization/)、[User Settings](https://docs.sillytavern.app/usage/user-settings/)、[Hotkeys](https://docs.sillytavern.app/usage/hotkeys/)。

- ST-UI-01：主题保存、复制、切换、导入和导出。
- ST-UI-02：颜色、字体比例、聊天宽度、模糊、阴影与自定义 CSS。
- ST-UI-03：头像形状、平铺/气泡/文档聊天样式。
- ST-UI-04：媒体列表/图库显示方式。
- ST-UI-05：移动端布局、面板位置及输入体验设置。
- ST-UI-06：减少动画、关闭模糊与阴影等选项。
- ST-UI-07：界面语言、设置搜索、提示位置和消息通知音。
- ST-UI-08：键盘快捷键、输入格式辅助与自动补全。
- ST-UI-09：消息序号、时间、生成耗时、操作按钮等显示控制。
- ST-UI-10：调试菜单、缓存/存储清理及用户数据统计。

## 22 账户、数据与服务器管理【核心】

来源：[多用户](https://docs.sillytavern.app/administration/multi-user/)、[配置文件](https://docs.sillytavern.app/administration/config-yaml/)、[SSO](https://docs.sillytavern.app/administration/sso/)、[Docker](https://docs.sillytavern.app/installation/docker/)。

- ST-ADMIN-01：可选多用户账户与每用户数据、设置和扩展。
- ST-ADMIN-02：管理员/普通用户、账号创建/停用等管理；不是细粒度企业 RBAC。
- ST-ADMIN-03：登录界面、隐藏用户列表、账户密码及基本个人资料。
- ST-ADMIN-04：可信反向代理条件下的 Authelia/Authentik SSO 自动登录。
- ST-ADMIN-05：用户数据备份下载、设置快照/恢复及设置重置。
- ST-ADMIN-06：聊天/设置备份、缩略图和遗留数据清理。
- ST-ADMIN-07：数据根目录、用户内容初始化与资源管理。
- ST-ADMIN-08：config.yaml、命令行参数与环境变量配置。
- ST-ADMIN-09：监听、TLS、白名单、可信代理、CORS、速率限制等设置。
- ST-ADMIN-10：请求代理、缓存、性能、日志和缩略图设置。
- ST-ADMIN-11：扩展和服务器插件开关与安装策略。
- ST-ADMIN-12：Docker、远程访问、反向代理和更新渠道支持。

## 稳定版全部 14 个随包扩展目录

以固定 tag 的 `public/scripts/extensions/*/manifest.json` 为依据，下面不是另外增加的 14 个业务功能，而是上文能力的实际模块归属。

| 目录 | 显示名 |
| --- | --- |
| assets | Assets |
| attachments | Data Bank (Chat Attachments) |
| caption | Image Captioning |
| connection-manager | Connection Profiles |
| expressions | Character Expressions |
| gallery | Gallery |
| memory | Summarize |
| quick-reply | Quick Replies |
| regex | Regex |
| stable-diffusion | Image Generation |
| token-counter | Token Counter |
| translate | Chat Translation |
| tts | TTS |
| vectors | Vector Storage |

源码：[随包扩展目录](https://github.com/SillyTavern/SillyTavern/tree/7e8663cd9c184a550b37238218bdd32c6efc68e9/public/scripts/extensions)。

## 旧组件与范围边界

- Extras 已于 2024-04 停止维护，文档保留旧安装和模块说明，不能算成当前默认运行所需组件。[官方状态](https://docs.sillytavern.app/extensions/extras/)
- 旧 Smart Context、Talkinghead 或某些 RVC 配置可能涉及 Extras，单列为遗留兼容，不默认承诺移植。
- Codex CLI / SillyTavern-Subscriptions 属于本项目此前讨论的第三方桥接路线，不是上面 14 个酒馆随包扩展，也不在本次固定资源目录的 66 个条目中。
- 语音、绘图、视觉、搜索、embedding、工具调用、推理和采样器均有各自能力条件；接通一个文本后端并不能使所有功能自动可用。
- 酒馆的原生账户/服务器管理功能在此列出供盘点，不意味着应替换 Whale Deck 的 Authentik/BFF 体系。

## 官方资源目录的全部可安装扩展条目

下表按 2026-10-30 获取的不可变目录提交 `72acaca394237790106fa7b33e57ee50ea197127` 生成，共 66 个 `type=extension` 条目。列出原名及项目链接；未逐个安装或验证。它们不属于稳定版主仓库的 14 个随包模块，兼容性与外部服务条件需分别核对。

来源：[固定资源目录](https://github.com/SillyTavern/SillyTavern-Content/blob/72acaca394237790106fa7b33e57ee50ea197127/index.json)。

| 编号 | 扩展原名 | 项目 |
| --- | --- | --- |
| ST-ADDON-01 | Prome Visual Novel Extension | [仓库](https://github.com/Bronya-Rand/Prome-VN-Extension) |
| ST-ADDON-02 | Guinevere UI Extension | [仓库](https://github.com/Bronya-Rand/Guinevere-UI-Extension) |
| ST-ADDON-03 | ProbablyTooManyTabs | [仓库](https://github.com/IceFog72/SillyTavern-ProbablyTooManyTabs) |
| ST-ADDON-04 | Group Greetings | [仓库](https://github.com/SillyTavern/Extension-GroupGreetings) |
| ST-ADDON-05 | Duplicate Finder | [仓库](https://github.com/SillyTavern/Extension-DupeFinder) |
| ST-ADDON-06 | Group SendAs | [仓库](https://github.com/SillyTavern/SillyTavern-GroupSendAs) |
| ST-ADDON-07 | Dynamic Audio | [仓库](https://github.com/SillyTavern/Extension-Audio) |
| ST-ADDON-08 | Blip | [仓库](https://github.com/SillyTavern/Extension-Blip) |
| ST-ADDON-09 | Idle | [仓库](https://github.com/SillyTavern/Extension-Idle) |
| ST-ADDON-10 | Speech Recognition | [仓库](https://github.com/SillyTavern/Extension-Speech-Recognition) |
| ST-ADDON-11 | Timelines | [仓库](https://github.com/SillyTavern/SillyTavern-Timelines) |
| ST-ADDON-12 | RVC | [仓库](https://github.com/SillyTavern/Extension-RVC) |
| ST-ADDON-13 | Live2d | [仓库](https://github.com/SillyTavern/Extension-Live2d) |
| ST-ADDON-14 | Objective | [仓库](https://github.com/SillyTavern/Extension-Objective) |
| ST-ADDON-15 | D&D Dice | [仓库](https://github.com/SillyTavern/Extension-Dice) |
| ST-ADDON-16 | HypeBot | [仓库](https://github.com/SillyTavern/Extension-HypeBot) |
| ST-ADDON-17 | Parameter Randomizer | [仓库](https://github.com/SillyTavern/Extension-Randomizer) |
| ST-ADDON-18 | Web Search | [仓库](https://github.com/SillyTavern/Extension-WebSearch) |
| ST-ADDON-19 | EmulatorJS | [仓库](https://github.com/SillyTavern/SillyTavern-EmulatorJS) |
| ST-ADDON-20 | Variable Viewer | [仓库](https://github.com/LenAnderson/SillyTavern-Variable-Viewer) |
| ST-ADDON-21 | VRM | [仓库](https://github.com/SillyTavern/Extension-VRM) |
| ST-ADDON-22 | Quick Persona | [仓库](https://github.com/SillyTavern/Extension-QuickPersona) |
| ST-ADDON-23 | Silence Player | [仓库](https://github.com/SillyTavern/Extension-Silence) |
| ST-ADDON-24 | Code Runner | [仓库](https://github.com/SillyTavern/Extension-CodeRunner) |
| ST-ADDON-25 | Push Notifications | [仓库](https://github.com/SillyTavern/SillyTavern-PushNotifications) |
| ST-ADDON-26 | Chat Top Bar | [仓库](https://github.com/SillyTavern/Extension-TopInfoBar) |
| ST-ADDON-27 | Emoji Picker | [仓库](https://github.com/SillyTavern/Extension-EmojiPicker) |
| ST-ADDON-28 | Notebook | [仓库](https://github.com/SillyTavern/Extension-Notebook) |
| ST-ADDON-29 | Chess | [仓库](https://github.com/SillyTavern/SillyTavern-Chess) |
| ST-ADDON-30 | Screen Share | [仓库](https://github.com/SillyTavern/Extension-ScreenShare) |
| ST-ADDON-31 | Prompt Inspector | [仓库](https://github.com/SillyTavern/Extension-PromptInspector) |
| ST-ADDON-32 | Image Metadata Viewer | [仓库](https://github.com/SillyTavern/Extension-ImageMetadataViewer) |
| ST-ADDON-33 | WebLLM | [仓库](https://github.com/SillyTavern/Extension-WebLLM) |
| ST-ADDON-34 | Mermaid | [仓库](https://github.com/SillyTavern/Extension-Mermaid) |
| ST-ADDON-35 | Weather | [仓库](https://github.com/SillyTavern/Extension-Weather) |
| ST-ADDON-36 | RSS | [仓库](https://github.com/SillyTavern/Extension-RSS) |
| ST-ADDON-37 | LaTeX | [仓库](https://github.com/SillyTavern/Extension-LaTeX) |
| ST-ADDON-38 | Inject Manager | [仓库](https://github.com/SillyTavern/Extension-InjectManager) |
| ST-ADDON-39 | CodeMirror | [仓库](https://github.com/SillyTavern/Extension-CodeMirror) |
| ST-ADDON-40 | Sampler Commands | [仓库](https://github.com/SillyTavern/Extension-SamplerCommands) |
| ST-ADDON-41 | Message Limit | [仓库](https://github.com/SillyTavern/Extension-MessageLimit) |
| ST-ADDON-42 | Lorem Ipsum | [仓库](https://github.com/SillyTavern/Extension-LoremIpsum) |
| ST-ADDON-43 | Typing Indicator | [仓库](https://github.com/SillyTavern/Extension-TypingIndicator) |
| ST-ADDON-44 | Custom Scenario | [仓库](https://github.com/bmen25124/SillyTavern-Custom-Scenario) |
| ST-ADDON-45 | Script Events | [仓库](https://github.com/SillyTavern/Extension-ScriptEvents) |
| ST-ADDON-46 | Message Summarize | [仓库](https://github.com/qvink/SillyTavern-MessageSummarize) |
| ST-ADDON-47 | Magic Translation | [仓库](https://github.com/bmen25124/SillyTavern-Magic-Translation) |
| ST-ADDON-48 | Character Creator | [仓库](https://github.com/bmen25124/SillyTavern-Character-Creator) |
| ST-ADDON-49 | World Info Recommender | [仓库](https://github.com/bmen25124/SillyTavern-WorldInfo-Recommender) |
| ST-ADDON-50 | Roadway | [仓库](https://github.com/bmen25124/SillyTavern-Roadway) |
| ST-ADDON-51 | Flowchart | [仓库](https://github.com/bmen25124/SillyTavern-Flowchart) |
| ST-ADDON-52 | WTracker | [仓库](https://github.com/bmen25124/SillyTavern-WTracker) |
| ST-ADDON-53 | Custom Sliders | [仓库](https://github.com/SillyTavern/Extension-CustomSliders) |
| ST-ADDON-54 | Quick Context Buttons | [仓库](https://github.com/SillyTavern/Extension-QuickContextSize) |
| ST-ADDON-55 | Spotify | [仓库](https://github.com/SillyTavern/Extension-Spotify) |
| ST-ADDON-56 | Guided Generations | [仓库](https://github.com/Samueras/GuidedGenerations-Extension) |
| ST-ADDON-57 | World Info Bulk Mover | [仓库](https://github.com/leandrojofre/SillyTavern-WI-Bulk-Mover) |
| ST-ADDON-58 | Pronouns | [仓库](https://github.com/Wolfsblvt/SillyTavern-Pronouns) |
| ST-ADDON-59 | Message Collapser | [仓库](https://github.com/Crystaria/SillyTavern-MessageCollapser) |
| ST-ADDON-60 | World Info Function Call | [仓库](https://github.com/Culpeo/SillyTavern-WI-FunctionCall) |
| ST-ADDON-61 | Memory Books | [仓库](https://github.com/aikohanasaki/SillyTavern-MemoryBooks) |
| ST-ADDON-62 | World Info Info | [仓库](https://github.com/aikohanasaki/SillyTavern-WorldInfoInfo) |
| ST-ADDON-63 | Character Locks | [仓库](https://github.com/aikohanasaki/SillyTavern-CharacterLocks) |
| ST-ADDON-64 | World Info Locks | [仓库](https://github.com/aikohanasaki/SillyTavern-WorldInfoLocks) |
| ST-ADDON-65 | Lorebook Ordering | [仓库](https://github.com/aikohanasaki/SillyTavern-LorebookOrdering) |
| ST-ADDON-66 | Model Injection | [仓库](https://github.com/aikohanasaki/SillyTavern-ModelInjection) |
