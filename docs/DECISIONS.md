# FACM 技术决策

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