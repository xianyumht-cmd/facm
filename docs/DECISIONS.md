# FACM 技术决策

## 2026-08-27：fix-lcu-window 能力转为 FACM 原生游戏修复，不保留第二套 LCU runtime

### 决策

`LeagueTavern/fix-lcu-window` 继续作为历史来源与行为参考，但正式 FACM 不再通过 `Fix-LCU-Window.exe --mode 1..4` 执行 LOL 客户端修复。四类能力迁入 FACM 自有模块：

- `LeagueGameRepairModule / LeagueGameRepairService` 成为游戏运行期修复 owner；
- `立即修复窗口` 使用 FACM 原生 Win32 窗口检测/恢复，目标显示器取 `Screen.FromHandle`，不再固定主屏；
- 窗口尺寸异常判断使用容差与工作区可见性，不再用精确 `16:9` 浮点比较；恢复优先最近一次合理尺寸，其次保留可信宽/高，最后才按当前显示器与 LCU zoom 安全回退，不再无条件重置为 `1280×720×zoom`；
- `自动修复窗口` 使用 `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` + debounce/cooldown，只在真实窗口变化后检查，不再启动独立 console 或每 1500ms 永久轮询；
- `跳过卡结算` 复用现有 Gate 6 post-game writer 的 `/lol-lobby/v2/play-again`，不增加新 session；
- `重启客户端界面` 使用新的最小 `ILeagueClientUxRepairWriteApi`，对调用方不暴露任意 path，唯一允许行为是 `POST /riotclient/kill-and-restart-ux`；
- `LeagueClientModule + LeagueClientSessionProvider` 继续是唯一 LCU discovery/auth/session owner；
- ToolBundle 不再嵌入旧 Fix-LCU-Window EXE 与 mode scripts；遗留 launcher 只保留明确退役兼容入口，不再是正式 UI 运行路径。

### 原因

- 上游实现把自己的一套进程发现、LCU client、窗口恢复和常驻轮询都带进 FACM，会破坏“唯一 League session owner”和低后台占用边界；
- `Screen.PrimaryScreen + 1280×720×zoom` 无法正确覆盖多显示器、混合 DPI 和用户原本较大客户端窗口，上游公开 issue 也已经暴露“修复后回到最小 720 大小”的问题；
- 游戏修复已经成为 LOL 工作台的一等功能，继续把按钮解释成“启动一个第三方 console mode”不利于诊断、测试和长期维护；
- `play-again` 与 `kill-and-restart-ux` 都是明确写操作，必须继续通过窄 writer 边界而不是通用 LCU 请求器。

### 后果

- 后续 LOL 窗口修复缺陷只在 `LeagueGameRepairService` 内演进，不重新引入独立 Fix-LCU-Window 进程；
- 自动修复默认仍为关闭，开启状态只存在于本次 FACM 进程会话，模块释放时必须解除 WinEvent hook 和 Timer；
- 新修复逻辑必须继续通过离线尺寸规划、多屏负坐标和 writer allowlist smoke，不能靠“能启动”作为验收；
- 旧 2026-08-27“UI 重组时暂不现代化 fix-lcu”的决定只描述上一任务的边界；本条决定正式取代其中关于 fix-lcu 运行实现的部分。

## 2026-08-27：控制中心只负责启动，环境恢复与游戏运行修复分流，全局主题统一

### 决策

本轮把 FACM 用户入口与视觉所有权进一步收束：

- 控制中心只保留 `清理与修复 / LOL 工作台 / 个性化 / 更多设置` 四个桌面式入口，不再把游戏目录、清理状态和步骤说明堆在主页；
- `清理与修复` 只负责环境级恢复：驱动修复与环境清理先后不限，完成后明确引导 `重启电脑 → WEGAME → 英雄联盟 → 修复游戏`；FACM 不把 WEGAME 外部步骤伪装为可验证完成状态；
- 游戏已运行时的大厅/客户端异常归入 `LOL 工作台 → 自动化 → 游戏修复`，当前只复用既有 `fix-lcu` mode 1～4 与进程级结束游戏动作；`fix-lcu-window` 内部现代化明确留到下一独立任务；
- LOL 工作台用户分区名称固定为 `比赛 / 攻略 / 自动化`，自动化下包含 `快捷工具 / 游戏修复 / 在线状态`；
- LOL 工作台不再保留重复的内容区标题/提示条，动态页面提示进入 FACM 自绘标题栏副标题；
- `ThemeCatalog` 继续是唯一主题目录，`FacmThemeRuntime` 成为进程级当前主题状态；`FacmDesignSystem` 与 `FacmWindowChrome` 从同一主题读取语义颜色，个性化中的主题按“FACM 全局主题”理解；
- 系统文件选择器、UAC 等 Windows 所有窗口不强行套 FACM 主题。

### 原因

- 控制中心应该回答“我要打开什么”，而不是同时承担业务状态面板、教程和流程页；
- 环境恢复与游戏运行期异常是两种不同心智模型，混在一个修复菜单里会让用户分不清何时使用；
- `跳过卡结算`、`自动回大厅`、`一键结束游戏` 属于不同动作，必须用真实行为命名，不能因历史文案复用而混淆；
- 3.5.14 已统一普通顶层窗口自绘外壳，如果主题仍只影响控制中心，会形成明显的产品割裂；
- UI 重组不应借机改写稳定的 fix-lcu 内部实现或扩大 LCU session/writer/网络/轮询边界。

### 后果

- 新的一级功能默认进入四个产品入口之一，不以实现模块为理由扩展控制中心；
- 新的环境修复步骤放进 `清理与修复`，新的游戏运行期客户端/大厅修复动作优先放进 `LOL 工作台 → 自动化 → 游戏修复`；
- `LeagueHubModule` 可以依赖 `ToolsModule` 以复用既有动作入口，但仍不得拥有第二套 LCU runtime；
- 全局主题新增控件应优先使用语义 token，不复制硬编码 RGB 或另建主题管理器；
- 本轮不触发正式发布，`release/request.json` 与 `online/version.json` 不变；生产版本继续以 3.5.14 为准。

## 2026-08-16：League 产品入口收束为一个按钮、一个中心窗口

### 决策

FACM 的英雄联盟功能不再按 Dashboard / Player / Live / OP.GG / Efficiency 等内部模块向用户暴露多个 Shell 按钮。正式信息架构固定为：

- 托盘与控制中心只保留一个 `英雄联盟` 主入口；
- 点击后只打开一个 `英雄联盟中心` 窗口；
- Hub 内只保留三个普通用户能理解的分区：`对局 / 推荐 / 效率`；
- 已验收的 Dashboard、玩家主页、实时对局、海斗、OP.GG 对局助手、一键应用、推荐装备、游戏效率继续复用原有 Form/service，不为了统一外壳重写运行逻辑；
- `LeagueHubModule` 成为 Shell 层唯一 League UI navigation owner；业务模块不再自行注册 Shell 子按钮；
- Hub 只保留当前页，切页正常关闭旧页并执行原有 FormClosed 清理，禁止把访问过的页面全部隐藏常驻；
- `ShellMenuGroups.AddLeagueAction` 作为兼容 no-op，防止旧 UiBridge 或未来新模块把 League submenu 再长回来。

### 原因

- 当前 League 功能数量并不需要多个并列入口；多个按钮只是把内部模块边界暴露给用户，增加寻找和理解成本；
- 用户明确要求“英雄联盟的功能集中到一个按钮里、一个面板上”；
- FACM 已经形成面向小白、低打扰的产品路线，继续堆入口会逐步退化成高级工具箱式 UI；
- 运行层已经按模块拥有明确 service/controller 边界，UI 收束无需把稳定业务代码重新揉成万能模块；
- 单页懒加载 + 切页释放可以维持 FACM 的低占用优势，避免“统一面板”变成多个旧窗体同时后台运行。

### 后果

- 新 League 功能原则上先判断应归入 `对局 / 推荐 / 效率` 哪一组，不新增 Shell 顶层或 League submenu 按钮；
- 如果未来出现真正不同的产品域，再单独评估新的顶层入口，不能以“实现方便”为理由突破单入口契约；
- `ShellUxSmokeTest / LeagueHubNavigation.ValidateForSmokeTest / FacmHostSmokeTest` 必须共同守住单入口、三分区、唯一 League runtime ownership；
- League Hub 是 navigation/composition 层，不新增 LCU session、gameflow monitor 或 writer；
- Issue #120 / PR #121 实现该决策；它与已腾讯验收的 #119 Gate7 修复在下一个正式版本中一起发布。

> 2026-08-27 的决策保留 2026-08-16 的“单 League 入口 / Hub 只做导航组合 / 不新增第二套 runtime”核心边界，但把面向用户的分区术语从历史 `对局 / 推荐 / 效率` 更新为 `比赛 / 攻略 / 自动化`。

## 2026-08-13：FACM 3.2 采用轻量 Modular Host，分层实施并在整轮完成后集中实机验收

### 决策

FACM 3.2 的架构主线定为 **lightweight modular host / 模块化单体**：

- 保留 `FACM.exe` 的 .NET Framework 4.8 / WinForms 主程序边界；
- 保留 `FACM.PetHost.exe` 的 .NET 8 x64 / WPF 独立子进程边界；
- 正常产品模式新增 `FacmHost` 作为应用级组合根，把业务生命周期从 `Program` / `MainForm` 中迁出；
- 功能按模块拥有自己的 state / controller / settings / lifecycle 边界，依赖必须显式；
- Host 负责模块注册、缺失/重复/循环依赖检测、拓扑初始化、反向释放和启动耗时日志；
- 技术实现仍按依赖层次拆成 Host、Shell、Settings、Online、Pets、Mayhem 等内部 Phase，便于自动验证和回归定位；
- **内部 Phase 不逐轮要求用户 Windows 实机测试**：编译、deterministic smoke、AppLog 和 Actions 成功后继续推进，等既定后端重构整体收口并形成单一候选包后再集中实机验收一次；
- 不做大爆炸技术栈重写，也不把“每个内部 Phase 都停下来让用户确认”当成安全策略。

不照搬 League Akari 的 Electron/Vue/TypeScript/Shard 实现细节，也不为了“现代化”默认引入 Autofac、Unity 等大型 DI 容器。只有未来出现轻量 Host 无法解决的真实需求时，才重新评估。

### 原因

- FACM 当前的可靠性工程已经比较成熟，真正的增长瓶颈是应用层组织：`Program` / `MainForm` 承担越来越多业务 orchestration，static manager 和直接 new 让依赖关系隐式；
- 后续计划增加 League Client、账号、Gameflow、ChampSelect、战绩、自动化等长期功能，如果继续把状态和生命周期挂到主窗体，耦合会快速扩大；
- League Akari 的长期价值主要来自“模块所有权 + 显式依赖 + 生命周期 + 状态边界 + 可观测性”，这些原则可以在 FACM 现有技术栈内吸收，不需要复制它的 Electron 多进程和 renderer IPC 成本；
- FACM 已经有 PetHost、在线更新、海斗、多种 smoke、Shell 等经过 Windows 实机验收的成熟链路，大爆炸重写会把产品风险从“架构债务”扩大成“所有稳定功能同时回归”；
- 内部按依赖层次实施能让自动测试清楚定位失败根因；但让用户每完成一个内部模块都下载、启动、逐项验收会造成不必要的人肉测试成本，不能替代自动化质量门禁。

### 后果

- 新功能原则上不得继续把业务生命周期直接堆进 `Program` / `MainForm`；优先在对应模块内形成明确所有者；
- 已验收实现先通过 adapter/facade 接入模块，再根据真实收益决定是否消除旧 static manager，不为了形式美观重写；
- Issue #53 单实例 AutoResetEvent、Flying Runtime、VPet/PetHost、海斗多源策略、在线发布事务、`settings.ini` 等稳定契约在架构阶段默认冻结；
- 每个内部 Phase 都必须有自动验证证据，但不以用户实机测试作为继续下一 Phase 的默认前置条件；
- 整轮重构结束时必须生成单一 Windows 候选，集中回归 Shell、二次启动、桌宠、海斗、清理、更新入口和真实交互；
- 架构目标与当前事实必须在 `docs/ARCHITECTURE.md` 中明确区分；
- 正式 3.2.0 发布仍需独立用户授权，架构分支/CI 测试包本身不等同发布。

## 2026-08-13：普通模式二次启动视为“唤醒现有 FACM”

### 决策

FACM 普通模式继续保持单实例，但第二次启动不再只弹“FACM 已经在运行”后退出，而是把它解释为用户的**恢复入口/打开控制中心意图**：

- 第一实例在当前 Windows 会话建立本地命名 AutoResetEvent 激活通道；
- 第二实例发现普通 Mutex 已被占用后，有限重试寻找该事件并通知第一实例；
- 第一实例收到通知后只确保控制中心已打开并置前：未打开则创建，已打开则 BringToFront/Activate，不 Toggle 关闭；
- Flying 桌宠与 VPet 不停止、不切换，控制中心沿当前桌面形态的既有定位规则出现；
- 如果通知发生在第一实例 message loop 完全就绪前，用 pending flag 在 `Shown` 后消费，避免启动竞态丢唤醒；
- `--cleanup` 继续使用独立 elevated cleanup Mutex，不参加普通实例激活；smoke/test 模式继续使用各自独立 Mutex。

### 原因

- FACM 已从“后台小工具”变成 Shell / 桌宠常驻产品，用户再次双击 EXE 很可能是在找回入口，而不是想创建第二个实例；
- 只提示“已经在运行”不能解决悬浮入口被遮挡、桌宠飞出屏幕、用户想快速打开控制中心等真实场景；
- 本地命名事件不需要网络端口、服务或复杂 IPC，足以表达单一“激活”信号，维护面小；
- 有限重试能覆盖第一实例刚拿到 Mutex、激活事件尚未创建的窄竞态，同时避免损坏实例永久阻塞第二次启动。

### 后果

- 普通用户可以把“再双击一次 FACM.exe”当作稳定的控制中心恢复入口；
- 单实例 Mutex 仍是实例所有权边界，命名事件只传递无参数激活信号，不承担配置/命令传输；
- 如果激活通道在有限重试内不可用，第二实例才回退原“FACM 已经在运行”提示；
- 后续若需要把带参数命令转发给主实例，应单独设计有版本/校验的 IPC，不扩展当前无参数事件为隐式命令协议。

## 2026-08-12：轻量桌宠收敛为 Flying Runtime，旧贴地 Sprite 只保留兼容

### 决策

FACM 的轻量桌宠主路线统一为会飞的动物/昆虫：

- 新轻量桌宠统一使用 Flying Runtime；
- 运行层把 **桌面运动轨迹 / 360° 身体朝向 / 翅膀动画** 三层解耦；
- 素材统一以“朝右”为 0° 母版，实际显示根据真实速度向量计算目标角度并平滑旋转，不再要求每个动物准备 8 方向 Sprite 行；
- 每种动物只通过 Flying Profile 定义速度区间、改向周期、停悬概率、速度响应、朝向响应和 jitter，不复制窗口移动代码；
- 当前推荐轻量桌宠为：绿苍蝇、蜜蜂、蜻蜓、蝴蝶、飞蛾；VPet Core 继续作为独立高精度路线；
- 猫、狗、蜘蛛、蚂蚁、旧灰苍蝇、旧胡蜂、小鸟等已有 Sprite ID 不删除，旧 `settings.ini` 继续可解析，但新桌宠选择器不再推荐这些 Legacy 项；
- 绿苍蝇作为运动回归基线：已实机验收的速度、改向周期、jitter、自由出屏行为不得因为运行层重构而变化。

### 原因

- 飞行动物不依赖落脚点、步频、地面接触和脚部动画，能避免贴地角色最容易出现的 foot sliding、倒着走、步态与位移不同步问题；
- 通过 360° 旋转一个标准母版即可连续转向，不再需要维护 8 方向素材映射；
- 飞行“性格”主要由轨迹 Profile 决定，素材和运动逻辑可以独立迭代；
- 用户实际体验更认可苍蝇的随机飞行轨迹，因此应保留成熟轨迹而不是为了高清素材重做运动系统；
- 统一运行层能让后续增加飞行动物时只新增 Profile + 素材，降低维护和回归成本。

### 后果

- 新的轻量桌宠素材必须朝右建模/绘制，并保证各翅膀帧的身体锚点稳定；
- Flying Runtime 不使用屏幕硬边界，桌宠允许自然飞出所有屏幕，恢复由“复位桌面位置”承担；
- 轨迹参数、朝向平滑和翅膀 FPS 分别测试，不把任何一层的改动伪装成另一层优化；
- 旧 Sprite 兼容路线不再继续投入画质/动作升级，除非为了修复影响已有配置的兼容性缺陷；
- Issue #33 的 Q 版蜘蛛 Gate 方案保留为独立长期探索，不再是当前轻量桌宠主路线。

## 2026-08-12：默认显示 FACM Shell，桌宠作为可选桌面形态

### 决策

FACM 的默认桌面体验从“仅托盘常驻”调整为：

- 启动后立即显示一个轻量、低干扰的 FACM Shell 悬浮入口；
- 默认 Shell 使用约 56×56 的透明窗口，实际主体约 46px，采用深色圆角方形、细边框、单一品牌标记和轻量 Hover，不使用持续霓虹、呼吸、环绕光点等常驻动画；
- `AnimalPetEnabled=false` 表示使用 FACM Shell，而不是“桌面没有任何入口”；
- 桌面宠物属于用户主动选择的可选桌面形态，只有启用后才准备/启动对应运行层；
- VPet/PetHost 加载期间 FACM Shell 保持可用，PetHost 真正 `ready` 后才由桌宠接管桌面入口；失败则继续保留 FACM Shell；
- 控制中心与托盘不再并列暴露“面板主题”和“桌面宠物”两个顶层入口，统一收进一个「主题」入口，其中再区分“面板外观”和“桌面形态”；
- 统一的是产品入口和用户概念，不强行把已有 `ThemeId` 与 `AnimalPetEnabled/PetStyleId` 合并成一个配置枚举，避免为 UI 收口破坏既有配置兼容。

### 原因

- 仅托盘常驻虽然安静，但普通用户双击程序后桌面没有任何反馈，容易误判为没有启动；
- 让 VPet 充当启动反馈会把可选重型子系统变成默认启动依赖，首次解包、杀软扫描和资源加载都会放大启动体感；
- FACM 自己的轻量 Shell 可以即时提供可见反馈和控制入口，同时保持主窗口默认不弹出；
- 桌宠与面板主题本质都属于“FACM 在桌面上如何呈现”，合并信息架构后更容易理解；
- 默认路径不加载 PetHost，能保留轻量常驻定位，用户选择桌宠后再承担相应成本。

### 后果

- 新用户默认会看到 FACM Shell，而不是只有托盘图标；
- 不得为了“预热”在 `AnimalPetEnabled=false` 时主动解包或扫描 PetHost；
- 默认 Shell 必须始终能单击打开控制中心、拖动定位、右键进入托盘菜单；
- 桌宠准备未完成或失败时不能先隐藏 Shell；
- 未来新增其它桌面形态应继续放在「主题 → 桌面形态」下，而不是增加新的顶层按钮。

## 2026-08-09：VPet 保持独立子进程，不为单 PID 强迁主程序

### 决策

FACM 3.1 继续采用：

`FACM.exe (net48 WinForms)` → `FACM.PetHost.exe (net8 x64 WPF/VPet)`

PetHost 启动后尝试加入 FACM 创建的 Windows Job Object，并启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`。PetHost 自身的 `--parent-pid` 检测继续保留。

### 原因

- 两个组件当前使用不同 CLR 与 UI 技术栈；
- 把 WPF/VPet 直接塞回 net48 主进程不可行，把 FACM 整体迁到 .NET 8 又会同时扩大清理提权、ToolBundle、更新器、签名、WinForms UI 的回归面；
- 独立 PetHost 提供崩溃隔离，VPet/WPF 出错时 FACM 仍能恢复默认悬浮球；
- Job Object + parent-pid 已解决“FACM 退出后遗留孤儿 PetHost”的产品问题。

### 后果

任务管理器仍会看到 FACM 主 PID 和 PetHost 子 PID，但它们是一棵受 FACM 管理的产品进程树。若未来整体迁移 FACM 到 .NET 8，可在独立架构版本重新评估单进程托管。

## 2026-08-09：海斗按字段多源容灾，官方公告不充当完整状态库

### 决策

海斗查询的数据职责固定为：

- Hexdata：国内优先的英雄胜率、排名和前十；
- ARAMMayhem.com：完整当前英雄平衡状态、选用率、海外排行备用；
- OP.GG：技能加点/核心装备等攻略字段，可失败；
- 腾讯 LOL 官网：国服 Patch 与本版本海克斯大乱斗官方改动；
- LCU / Data Dragon / CommunityDragon：Riot 静态英雄、技能、装备和图标元数据。

### 原因

- OP.GG 在中国大陆存在访问不稳定，不能作为排行榜整体成功条件；
- 单个第三方页面结构/WAF/限流随时可能变化；
- 腾讯版本公告只列本版本增量，无法单独回答“一个英雄现在所有 Buff/Debuff 是什么”；
- 用户需要的是当前生效状态，因此完整状态必须带 Patch 语义，并与当前国服版本核对。

### 后果

- 单一海外来源失败时，已有核心字段继续返回；
- 每个来源使用独立短超时预算；
- 完整平衡状态 Patch 落后于腾讯当前 Patch 时，FACM 不展示旧数值；
- 官方公告可以显示明确的本版本改动，但在没有完整状态时必须标注“非完整当前状态”；
- 核心 CI 只验证离线解析 fixture，真实站点健康继续由独立 live probe 监控。

## 2026-08-30：FlyingSprite 与 VPet 保持独立 host，并按 .NET 10 analyzer 合同配置 DPI

### 决策

- FlyingSprite 继续走 `WindowsFlyingPetRuntime -> FACM.FlyingHost` 独立 bundle；VPetCore 继续走 `WindowsVPetRuntime -> FACM.PetHost`；
- router 切换固定为 clear active -> stop non-target -> set target active -> start target；两条链的 prepare、启动、pipe、命令、ready、退出都必须有界；
- WPF/WinForms host 使用 `ApplicationHighDpiMode=PerMonitorV2`，不在 manifest 中重复声明旧 DPI 节点；FlyingHost 使用独立 `FACM.FlyingHost.app` identity。

### 原因

将 FlyingSprite 塞回 VPet PetHost 会重新引入已纠正的架构耦合；而 .NET 10 `WFAC010` 已证明旧 manifest DPI 声明与当前 analyzer 合同冲突。分离 ownership 并采用 SDK 支持的 DPI 配置可以保留行为边界，同时让 warnings-as-errors 构建诚实失败/通过。

## 2026-08-30：Batch P 统一桌宠 Host 的 activate/show/stop 生命周期

### 决策

- Host 进程先建立 Dispatcher 和 IPC reader，但不在 `Program` 中预先显示窗口；只有收到 `activate` 后才在 Dispatcher 上 `Show()`，随后按 `Loaded -> ready` 完成接管。
- 客户端命令写入必须把取消令牌传入 `StreamWriter.WriteLineAsync` 和 `FlushAsync`；超时后的 transport 进入 poisoned 状态，直接清理并等待/杀进程，不再尝试不可靠的 graceful stop。
- FlyingHost 与 PetHost 保留各自 payload/Runtime ownership，但共享同一套阶段诊断字段和无异常清理语义；路由仍按 stop-before-start 串行切换。

### 原因

真实 Win10 证据显示旧实现会在 activate 写入超时后留下多个 Host，同时预显示的窗口绕过了“桌宠接管悬浮入口”的时序。把窗口显示和 IPC 命令消费绑定，并让写入取消真正到达底层 StreamWriter，能在不改变 750 ms/ready timeout 预算和既有 Runtime 分离的前提下消除这两个生命周期缺口。

## 2026-08-30：Live League 可靠性先做边界与证据，不做猜测性性能重构

### 决策

- Workbench UI 只能在 Dispatcher 上读取/写入 WinUI 状态；后台刷新保留现有共享 Gateway/Gameflow owner，不增加第二轮询、Limiter、全局 Cache 或 Debounce。
- 已知 LCU session-shaped 404 通过现有 Gameflow 快照做阶段分类；所属阶段的 404 保留为 UnexpectedFailure，非所属阶段记录为 ExpectedUnavailable，未知端点不做宽松归类。
- Diagnostics Runtime Snapshot 只读取现有 session、Gameflow 和 Gateway 的当前事实，不成为新的状态 owner。

### 原因

真实复现显示卡顿日志对应的是后台 PropertyChanged 跨线程 COMException，而非已证实的 FACM 进程退出。当前性能数据最大并发为 2、Workbench refresh 为 56 ms，尚无证据支持引入新的调度/缓存机制。自然 ReadyCheck 证据回来前，必须保留现有 Auto Accept 的一次性写入和可观察失败边界。

## 2026-08-30：Morphing Surface 作为单一 FACM 主界面，行为先于视觉升级冻结

### 决策

- 默认 FACM 只创建一个持久 `MainWindow` 主宿主；Orb、ControlMatrix、FeatureSurface、LeagueSurface、ChampSelectStrip 和 HiddenInGame 是同一窗口内的展示模式，不是六个窗口。
- `FacmSurfaceStateMachine` 只负责展示状态、转换原因、耗时和失败 telemetry；ViewModel、League session/Gateway/Gameflow、settings、automation 和桌宠 runtime ownership 保持原有边界。
- 旧 `FloatingWindow` / `CompactLauncherWindow` 路由保留为 `FACM_SHELL_EXPERIENCE=legacy` fallback，便于诊断和回归对照，但默认体验不再依赖多个并行 FACM shell。
- UI Upgrade 只允许在这份行为契约之内进行视觉替换；outside-click、modal suppression、single-instance、tray、桌宠切换、InGame 隐藏、Lobby 回 Orb、settings persistence、update flow 和 League polling/cache 不得因视觉重构改变。

### 原因

用户可见的“桌宠/悬浮入口/控制中心/功能页”必须表现为一个可预测的 FACM surface，避免切换时残留多个宿主或桌宠与悬浮入口并存。保留 legacy fallback 可以在真机视觉复核期间提供可逆对照，而不重新引入第二套业务 owner。

### 后果

本地候选先完成状态机、几何、宿主路由、适配层和确定性 smoke；Diagnostics、Logs、Repair、Cleanup、Settings、Maintenance、Personalization、Pet Picker、Workbench 的完整视觉迁移仍是后续工作。未完成真实多屏/DPI/辅助功能和截图复核前，不得把候选视为 release-ready，也不得移动正式 P7。

## 2026-08-31：Morphing Orb 使用平台层最小跟踪尺寸适配

### 决策

- 保持 `MainWindow` 的既有 `AppWindow.MoveAndResize` 呈现路径，不引入第二个浮窗、全局锁、重试或连续 watchdog；
- 由现有 `WindowsFloatingSurfacePlatform` 仅为 Morphing `MainWindow` HWND 安装生命周期绑定的窗口过程适配，在 `WM_GETMINMAXINFO` 中把 `MinTrackSize` 放宽到 `1×1`，其它消息先转发给原窗口过程；
- 该适配只解决 Win10 本机将无边框 Orb 外框钳制到 `136×39` 的平台约束，不改变 Orb、ControlMatrix、Feature/League、ChampSelect、outside-click 或 League owner 契约。

### 原因

MS9.1 真实日志证明所有呈现失败都在共享 `invariant-check`，而不是 XAML、Dispatcher、AppWindow API 异常或 League；首个候选实际外框 `136×39` 与目标 `36×36` 不符。`PreferredMinimumWidth/Height` 与 CompactOverlay 均未解除该本机最小跟踪尺寸，平台层窗口过程适配后最终候选真实达到 `36×36`。

### 后果

真实 Orb↔ControlMatrix 100 次循环和 Feature/League 回 Orb 已通过，后续 UI Upgrade 仍必须遵守冻结行为契约，并继续由用户完成 outside-click、ChampSelect/Lobby、modal、tray、桌宠和多屏/DPI 的真机验收。

## 2026-08-31：Bench 快捷换人使用单一候选源并直接呈现头像

### 决策

- `LeagueBenchRuntimeObserver` 是 Compact/Strip 自动呈现的唯一进程级 Bench 状态 owner；它复用唯一 `LeagueGameflowMonitor.Observed` 心跳和唯一 `LeagueBenchQuickPickService`。详细 Workbench 仍可刷新自己的 `Live` 页面，但不再是自动呈现的前置条件。
- `LeagueBenchRuntimeSnapshot` 是 Compact/Strip 的唯一候选事实来源；它保留 ChampSelect context generation、Bench 状态、候选列表、锁存状态和 source freshness，详细 Workbench 与该运行时都不各自维护自动呈现判定。
- 新增的 `LeagueBenchCandidate` / `LeagueBenchCandidatePresentation` 只负责把已观察到的 ID 映射为名称、头像源和动作状态；交换仍调用既有 `ILeagueBenchQuickPickService.TrySwapAsync`，不新增 HTTP 写路径。
- 只有首次观察到 `ChampSelect + BenchEnabled + 至少一个正数候选` 时才锁存并把同一个 `MainWindow` 变为横向 strip；同一 ChampSelect context 中候选暂时变为 0 或读取暂不可用时，保留细条等待态，不退回 Orb。InGame/Lobby 结束 context 并清理锁存。
- strip 采用 56 DIP 高度、44 DIP 头像格、280–600 DIP 内容宽度；F 区域是唯一拖动区，头像按钮只处理点击/键盘激活；hover/focus 使用短提示，不以 `#37` / `#236` 作为主控件标签。
- BenchStrip 没有正常折叠按钮；桌面空白、League 客户端点击、候选点击和 F 句柄简单点击均保持 Strip，F 句柄仍可拖动。普通 expanded surface 继续使用 outside-click 折叠，modal 只抑制自动激活并在关闭后重新评估。InGame、single-instance、tray、桌宠与 MS9 窗口约束保持原契约。

### 原因

用户需要在已有随机模式英雄台候选出现时直接看头像并单击交换，而不是打开 Workbench 解释数字 ID。复用现有 Live、身份缓存和一次写入/有界回读边界，可以减少操作路径而不引入新的 League 性能 owner。

### 后果

这是行为等价线上的窄功能改进，不是完整 UI Upgrade。真实 LCU ARAM 会话、真实 portrait 渲染、outside-click/modal、键盘/辅助功能和跨 DPI 仍需在新候选上由用户完成验收；在此之前不宣称完整 P7 或 release-ready。

## 2026-08-31：BOOT-1 使用原生 thin bootstrapper + app-local multi-file Core

### 决策

- 以小型原生 Win32 `FACM.exe` 作为启动/解析层；它不引入 .NET、WinUI 或 Windows App SDK，
  只负责读取最小 active state、验证受控版本目录、设置 modular root 环境并启动当前 Core。
- 4.0 Core 的新 profile 使用 app-local self-contained multi-file `win-x64` 发布；legacy single-file
  profile 保留并继续默认嵌入桌宠 payload，便于兼容回归和可逆对照。
- Core、PetHost、FlyingHost 使用明确 component id；桌宠可用性在 App composition root 只读探测，
  缺失时 fail-soft、恢复 launcher、记录诊断，不改写用户的 enabled/style 选择。
- `.facm\state\active.json`、`.facm\versions`、`.facm\components`、`.facm\staging`、logs 和
  runtime/cache 构成稳定 modular data-root contract；state 和 staging 的失败路径必须保留已知
  active 版本。
- BOOT-1 只做本地 package/source 和 manifest/hash/staging 原型；review pack 采用 ZIP 并可独立校验，
  但 native ZIP extraction 和网络 provisioning 留待后续明确授权的 provisioning 阶段。

### 原因

单文件 Core 把大桌宠 payload 与核心启动耦合在一起，也让后续组件更新、版本切换和数据路径隔离变得
不透明。原生薄启动器可以把启动 fast path、active rollback 和 Core 生命周期从托管 UI 中分离，同时保留
legacy profile 作为行为对照；可选桌宠 fail-soft 则避免组件缺失导致 FACM 主界面消失或持久化偏好被误改。

### 后果

review candidate 已具备离线启动、active switch/rollback、pack hash verification 和 no-pet Core
边界，但它仍不是 release package。任何真实机器安装、网络下载、签名、ZIP extraction、桌宠跨进程
回归和 production cutover 都必须另行验证，不能由 deterministic smoke 自动替代。

## 2026-08-31：BOOT-2 使用 CAB 原生组件包与显式本地信任模式

### 决策

- BOOT-2 的网络组件包采用 Windows Cabinet（MSZIP）而不是继续把 ZIP 解包交给 PowerShell、7-Zip 或
  WinRAR；native bootstrapper 通过 Cabinet FDI 回调完成受控解包。
- 从实际 app-local publish 输出建立三类更新单元：FACM app、.NET managed runtime、Windows UI/runtime。
  每个源路径只有一个 owner；组合阶段使用 fresh staging copy，重复目标路径直接失败，不使用未经审计的
  symlink/hardlink 共享。
- 正常启动只读本地 `active.json` 与入口文件，不同步抓取远端 manifest，也不执行全量 installed hash；
  网络清单和组件评估属于首次缺失供给或显式 `--update` 路径。
- 下载缓存使用完整包与 `.partial` 两态；支持 HTTP Range 续传、主地址/镜像有界切换，包在 SHA-256
  和大小通过前不得转正。解包另校验 extracted file count、installed size 和 content digest。
- 当前 deterministic mirror 只使用 `unsigned-local` + 显式本地 HTTP 开关，作为开发/验证模式；没有
  生产签名密钥或真实 CDN，因此不宣称 production trust，也不进入 release/cutover。

### 原因

BOOT-1 的 expanded-source/ZIP 原型无法证明真实网络供给、断点续传、独立组件更新或原生安装安全边界。
CAB 可由 Windows 自带 Cabinet API 解包，且能在不引入 managed framework 的前提下与 thin bootstrapper
配合；按更新节奏拆分后，app-only 更新不会重新下载两个 runtime，Windows runtime 也可独立保持不变。

### 后果

本地 BOOT-2 candidate 已能证明组件供给、组合、回滚保护和增量下载关系，但仍需真实 HTTPS/CDN、生产
签名验证、真实 Win10/11、升级中断/空间压力、自然 League/桌宠和完整 Gate13 evidence。unsigned-local
镜像不得直接作为用户 release。

## 2026-08-31：BOOT3-A 使用 bootstrapper-local exact-byte manifest trust

### 决策

- 生产应用和组件清单使用 detached RSA-2048 PKCS#1/SHA-256 签名，签名输入是实际传输/读取的精确字节，
  不引入新的 JSON canonicalization 规则。
- bootstrapper 只内嵌固定生产 `keyId` 与公钥，不读取配置、系统任意证书根或用户提供的 keyring 来扩大
  生产信任；应用清单与组件清单必须使用同一受信任 key identity。
- 应用签名认证组件清单 URL、清单字节 SHA-256、CAB size/hash 和 extracted size/fileCount/contentDigest；
  组件清单再签名并逐字段对比，包哈希和解包摘要在安装前后都验证。
- `unsigned-local` 只保留给显式 loopback HTTP 开发测试，必须同时开启两个 local 开关，且生产模式下这些
  开关永远不是签名绕过。

### 原因

现有 Authenticode/updater verifier 只适合 PE 文件签名者身份与 WinVerifyTrust，无法直接表达 JSON/CAB
manifest 的 exact-byte detached trust。Windows CNG 可由无 .NET/WinUI 依赖的 native bootstrapper 验证，
并能把包与解包内容的身份纳入同一组件元数据链。

### 后果

BOOT3-A 获得了可审计的 native trust boundary 和失败更新保护，但真实 release key custody、HTTPS hosting、
签名包发布、密钥轮换与真实机器 update/cutover evidence 仍属于 BOOT3-B；本地测试私钥不能用于正式发布。

## 2026-08-31：BOOT3-B 采用外部 signer request 与离线 bundle validation

### 决策

- 普通构建机只生成三类 BOOT-2 CAB、exact-byte schema-3 清单、release index 和 unsigned signing request；不接触正式 release private key。
- signer request 用相对路径、精确 payload bytes、SHA-256、key ID、算法和期望 `.sig` 路径固定授权边界；response apply 重新校验请求输入，只写 detached signature。
- offline validator 先检查 artifact topology、ownership、HTTPS、默认三组件、hash/size/contentDigest 和 secret material，再调用 native CNG trust bundle verifier。
- 同一 BOOT-2 package/source 输入的 bundle metadata 必须 byte-identical；任何 manifest post-sign 修改、signature replay、未知/计划 key、unsigned bundle、metadata/package/downgrade 异常都必须 fail closed。

### 原因

该拆分让“生成要签什么”和“持有/使用私钥”成为两个可审计边界。当前环境没有正式 production signer 的证据，不能用本地 validation key 冒充生产签名系统；同时 native verifier 继续是签名和 CAB 解包的权威执行点。

### 后果

BOOT3-B 可以在无生产私钥的前提下产出可审计的签名请求，并在测试中完成签名响应、离线校验和负向覆盖。真实 signer、immutable HTTPS/CDN/mirror、生产发布和 Windows update/cutover 仍需 BOOT3-C/后续授权。

## 2026-08-31：BOOT3-B 将 release key custody 与构建/签名流程隔离

### 决策

- `facm-production-r1` 只视为当前候选 bootstrapper 的 embedded public identity，不视为已经存在的正式生产 release credential。
- release private key 永远不进入 Git、bootstrapper、fixtures、review artifacts、日志、CI artifacts 或命令行；普通构建机只产生 exact-byte signing request，外部 signer 返回 detached signatures。
- 运行时只信任源码编译进 native bootstrapper 的固定 key table；`tools/release/facm-keyring-policy.json` 只是 review metadata，不能扩大 runtime trust roots。
- key rotation 通过 reviewed bootstrapper source change 明确激活，支持有界 overlap；planned/retired/revoked/unknown key ID 和 downgrade fail closed。

### 原因

当前环境证明了 BOOT3-A 公钥表示与本地验证 key 的数学对应关系，但没有证明正式生产 HSM/KMS 或 signing service 已部署。显式 external-signer boundary 可以让构建、审计和持钥职责分离，同时避免虚构生产 secret storage。

### 后果

BOOT3-B 可以生成可审计的 unsigned signing request 和可验证的 signed bundle，但正式 production key custody、授权记录和 release publication 仍需 release owner 提供真实外部证据。local validation key 只用于测试，不能直接触发生产发布。

## 2026-08-31：BOOT3-C 使用签名清单控制 HTTPS 主站/镜像回退

### 决策

- 应用清单增加签名覆盖的 `manifestMirrors`；组件清单增加签名覆盖的 `componentManifestMirrors`，包地址继续使用已认证的 `primaryUrl`/`mirrors`。
- bootstrapper 只把 `bootstrap.json` 的 primary + fixed mirror 作为首次发现候选；WinHTTP 显式禁用重定向，生产地址必须 HTTPS。
- 地址切换只按 manifest 声明顺序执行；传输失败或包精确 hash/size 不匹配时可尝试下一个已声明地址，但任何来源都必须通过 embedded key、exact-byte signature、metadata、package 和 extraction checks。
- 更新前以包/partial、解包暂存、组合版本和 64 MiB 余量估算目标卷峰值空间；active/known-good 版本不作为清理对象。
- 本地 TLS origin 只用于验证 WinHTTP 的真实证书链路；测试证书和 local validation private key 必须在仓库外并在测试结束删除。

### 原因

BOOT3-B 的 offline bundle validator 已证明静态签名链，但不能证明真实 HTTPS、主站故障、镜像回退、重定向拒绝、断点恢复和磁盘压力。将 fallback metadata 纳入签名 payload 能保持 origin 与 release identity 分离，同时不引入任意第三方 trust root。

### 后果

BOOT3-C 可以在本地 production-like TLS origin/mirror 上验证失败关闭、恢复和状态保护；它仍不能证明正式 CDN、生产 signer、发布授权或真实 Win10/11 PASS。正式 production pointer、Formal P7 和 Gate13 必须由后续明确授权的任务处理。

## 2026-08-31：FREE-DIST-1 使用 GitHub canonical origin 加固定免费传输候选

### 决策

- 将 GitHub Release 作为唯一 canonical artifact origin；签名应用/组件清单只写标准 GitHub Release 下载路径，
  不把公共代理地址写进 signed metadata。
- 对 canonical GitHub Release URL 固定尝试 `ghfast.top`、`gh-proxy.com`、`gh.llkk.cc`，最后回退 direct GitHub；
  对非 canonical/local-development URL 不自动套用这些代理。
- 保留 WinHTTP redirect policy = never，只允许有限深度的 HTTPS GitHub release/CDN redirect；HTTP、任意主机、
  user-info 和异常链路 fail closed。
- 传输候选不能获得信任权。清单 exact-byte detached signature、embedded `facm-production-r1`、package SHA-256、
  extraction digest、downgrade、activation 和 rollback 规则全部不变。

### 原因

当前目标是零付费、零新增服务器的可审计分发候选。公共免费代理的可用性不稳定，不能成为 canonical origin
或 release trust；把它们限定为顺序 transport candidates，既能覆盖受限网络，又能让 direct GitHub 保持最终
可用路径。实测支持 Range 的候选才进入固定列表，忽略 Range、TLS 失败或落到 HTTP 的地址不纳入。

### 后果

FREE-DIST-1 可以在本地生成和验证 release-compatible bundle 与 launcher-only candidate，但不能证明公共免费
代理的 SLA，也不能替代 GitHub Release 发布授权、真实 signer、真实机器 acceptance 或 Gate13。发布后必须重新
验证所有候选、清洁机器首启、断点续传、恶意/损坏内容拒绝和二次启动零下载。
