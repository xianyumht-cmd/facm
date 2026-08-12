# FACM 3.1 架构

> 本文前半部分记录当前 `main` 已验证的 FACM 3.1 架构。文末“FACM 3.2 目标架构”属于 Issue #55 的规划目标；在对应代码和实机验收完成前，不得把目标结构当成已经存在的运行事实。

## 进程边界

FACM 3.1 是一棵由 `FACM.exe` 管理的产品进程树，而不是强制单 PID。

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ 控制中心 / FACM Shell / 托盘
├─ 清理与内置工具
├─ 海斗查询与在线更新
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core，仅启用对应桌面形态时启动)
```

`FACM.exe` 是产品主进程和生命周期拥有者。`FACM.PetHost.exe` 仅负责高精度桌宠运行层，通过命名管道与主进程通信。

PetHost 启动后尝试加入 FACM 创建的 Windows Job Object，并使用 `KILL_ON_JOB_CLOSE`；PetHost 自身仍保留 `--parent-pid` 守护作为兼容兜底。这样既保留 WPF/VPet 崩溃隔离，又避免 FACM 结束后留下孤儿宿主。

PetHost 的内嵌包定位/释放、进程创建、最长 7 秒的 named-pipe connect 和停止等待均不得占用 WinForms UI 线程。

FACM 启动时先显示自己的轻量 Shell。只有当前配置已经启用桌宠时，才在 Shell 出现后后台预热对应内嵌 PetHost；默认 `AnimalPetEnabled=false` 不触碰 PetHost payload。预热与用户实际启用桌宠共用同一个任务，避免重复解包。PetHost 运行宿主按 **内嵌 PetHost ZIP 的 SHA-256** 隔离，而不是按 FACM 主程序集 MVID 隔离；这样 FACM-only 更新可以复用完全相同的 PetHost，而任何 PetHost payload 变化都会进入新的宿主目录。

缓存命中时只快速校验完成标记和启动所需关键文件，不再每次递归统计 self-contained runtime 的几百个文件；首次释放仍会做完整文件数/总字节统计后才写完成标记。新的 PetHost payload 第一次出现时仍必须真实释放一次；Shell 在整个准备阶段保持可用，只有 PetHost 真正发出 `ready` 后桌宠才接管桌面入口。

## 单实例与二次启动唤醒

普通 FACM 模式由 `Local\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D` Mutex 保持单实例。Mutex 负责**实例所有权**，另有一个当前 Windows 会话内的命名 AutoResetEvent 只负责**无参数激活通知**：

```text
第二次 FACM.exe
    │
    ├─ 普通 Mutex 已占用
    │
    ├─ 最多 1.6s 有限重试打开激活事件
    │
    └─ Set()
          │
          ▼
第一实例 SingleInstanceActivation
          │
          └─ MainForm.RequestExternalActivation()
                    │
                    ├─ 控制中心未开 → 创建并显示
                    └─ 控制中心已开 → BringToFront + Activate
```

如果第一实例刚取得 Mutex、WinForms message loop 尚未完全就绪，`MainForm` 先记录 pending activation，`Shown` 后再消费；因此第二次启动不会因为极短的启动竞态被静默丢弃。第二实例只有在有限重试仍无法找到激活事件时，才回退“FACM 已经在运行”的旧提示。

此激活通道不携带命令、配置或文件路径，也不使用 TCP/HTTP 端口。`--cleanup` 继续使用独立 elevated cleanup Mutex；各 smoke/test 模式也继续使用自己的 Mutex，不参与普通实例唤醒。

## FACM Shell 与控制中心

`MainForm` 是应用级入口拥有者，同时承载默认 FACM Shell；`CompactMenuForm` 是轻量弹出控制中心。

默认 Shell 使用 56×56 的透明分层窗口，实际可见主体约 46px。渲染由 `LayeredFloatingBall` 负责，采用深色圆角方形、细边框、单一品牌标记和轻量 Hover；空闲时不运行持续呼吸/环绕动画。透明层文字使用灰度抗锯齿，避免 ClearType 子像素彩边。Shell 保留：

- 左键单击打开/收起控制中心；
- 拖动调整位置并写入 `BallX/BallY`；
- 右键打开托盘菜单；
- 桌宠启动失败时作为稳定回退入口。

`AnimalPetEnabled=false` 表示使用 FACM Shell；为 true 时启动 `PetStyleId` 对应桌面宠物。桌宠进入 ready 前 Shell 不隐藏。

控制中心底部只保留 `日志 / 主题 / 海斗排行榜 / 退出` 四个入口。“面板主题”和“桌面宠物”不再并列占两个顶层按钮；统一由「主题」菜单管理：

```text
主题
├─ 面板外观…
└─ 桌面形态
   ├─ FACM 悬浮入口
   ├─ 选择桌面宠物…
   └─ 复位桌面位置
```

这里的「主题」是统一入口，不表示面板皮肤与桌面形态必须绑定为同一个枚举值：现阶段 `ThemeId` 继续控制控制中心外观，`AnimalPetEnabled/PetStyleId` 继续控制桌面形态；统一的是用户入口和概念层级，避免一次性重写已验证的配置兼容性。

底部兼容布局仍由 `CompactMenuEnhancer` 在第一条 `WM_PAINT` 前完成，避免旧 `Application.Idle` 后置重排造成首帧残影；Idle 只能作为异常情况下的兜底，不再承担正常布局职责。

控制中心关闭使用两条信号：

- 正常前台激活场景：`Deactivate`；
- PetHost 等跨进程点击打开场景：物理左键 outside-click watcher。

outside-click watcher 必须先等待打开面板的那次按键释放，再监测下一次按下，且内部 modal 对话流程由 `_dialogOpen` 抑制误关。

## 清理执行边界

清理的安全规则仍全部归 `SafeCleanupService` 所有，包括编译期白名单、重解析点阻止、预览统计和执行前再次校验。

从 WinForms 控制中心调用时，两个可能持续较久的阶段不再占用 UI 线程：

```text
CompactMenuForm
   │
   ├─ SafeCleanupService.CreatePlan
   │      └─ BackgroundOperationDialog → worker thread
   │             └─ 递归统计目标 / 大小 / reparse 检查
   │
   └─ SafeCleanupService.Execute
          └─ BackgroundOperationDialog → worker thread
                 └─ 二次安全校验 / 文件删除
```

后台化只改变执行线程，不改变允许删除的路径集合和校验顺序。正式删除阶段不给任意中断按钮，避免把“中止”误解为事务回滚；单目标失败仍按原有语义记录后继续。

非 UI/测试调用没有 WinForms message loop 时直接执行同步 core，因此服务逻辑仍可独立验证。

## 海斗查询数据流

海斗查询采用字段级多源合并，而不是“一个网站成功才算整次查询成功”。

```text
用户英雄名/别名
        │
        ├─ ChampionAliases（本地快速解析）
        │
        ├─ Hexdata（国内优先）──────────────┐
        │     排名 / 胜率 / 前十             │
        ├─ ARAMMayhem.com ─────────────────┤
        │     完整当前平衡 / 选用率 / 备用排行│
        ├─ OP.GG ──────────────────────────┤
        │     技能加点 / 核心装备等可选攻略  │
        ├─ lol.qq.com ─────────────────────┤
        │     国服当前 Patch / 本版本增量改动 │
        └─ LCU → DataDragon/CommunityDragon │
              英雄、技能、装备、强化图标      │
                                            ▼
                                  MayhemChampionResult
                                            │
                                  MayhemCardRenderer
```

来源按字段短预算并行读取。OP.GG 不再是排行整体成功条件。

腾讯公告是“本版本改了什么”的增量日志，不是完整当前状态库。只有完整平衡来源的 Patch 与腾讯当前国服 Patch 一致时，FACM 才把其 Buff/Debuff 当成当前完整状态；版本不一致时拒绝静默展示旧值，只允许显示明确标注为非完整状态的本版本官方增量。

## 海斗图片与渲染

Riot 元数据优先使用本机 League Client LCU，无法使用时回退 Data Dragon / CommunityDragon。

`MayhemImageCache` 有 10 分钟内存缓存和 6 小时便携磁盘缓存。磁盘缓存读取和 Bitmap 解码必须在后台执行，并限制为最多 4 路并发，避免“缓存越热越卡 UI”或几十张图同时争抢 CPU。

最终卡片渲染在图片异步准备完成后执行；网络正文统一受 `CancelableHttpContentReader` 的取消和大小上限保护。

## 构建与外部健康检查

`FACM Windows Build` 只承担 deterministic 核心构建与本地 smoke。真实 Hexdata、腾讯、OP.GG、ARAMMayhem、Riot CDN 的可用性由独立 `FACM Mayhem Source Probe` 检查。

公网 probe 失败不能自动证明核心程序构建失败；但正式发布候选仍需要确认主查询在国内优先源和字段级降级下可以完成用户实际查询。

## 发布边界

正式发布包继续只交付一个 `FACM.exe`。匹配版本的 self-contained PetHost publish 目录在构建时压成 `FACM.Resources.PetHost.zip` 嵌入主 EXE，运行时按该 ZIP 的 SHA-256 释放/复用到：

`runtime\pethost-host\<PET-HOST-BUNDLE-SHA256>`

正式 Release 与在线更新事务由独立发布工作流负责；发布前实机验收与普通 Actions 测试 artifact 分开。

---

## FACM 3.2 目标架构（Issue #55，规划中）

FACM 3.2 的架构升级目标不是换 Electron/Vue，也不是把 FACM 全量迁移到 .NET 8；目标是把当前已经稳定的能力组织成**模块化单体（modular monolith）**，让后续 League Client、账号、Gameflow、ChampSelect、战绩、自动化等功能有清晰的所有权和生命周期边界。

### 目标组合根

```text
Program
│
├─ 进程级职责
│  ├─ command-line mode / smoke mode
│  ├─ Mutex / SingleInstanceActivation
│  ├─ WinForms runtime 初始化
│  └─ fatal exception boundary
│
└─ FacmHost
   ├─ Infrastructure / Platform
   │  ├─ Logging
   │  ├─ Settings
   │  ├─ Paths
   │  ├─ Process / Job
   │  ├─ HTTP
   │  └─ Background Tasks
   │
   └─ Modules
      ├─ Shell
      ├─ Cleanup
      ├─ Online
      ├─ Pets
      ├─ Mayhem
      ├─ Tools
      └─ LeagueClient          # 后续阶段新增
```

`Program` 保留真正属于进程入口的职责；`FacmHost` 成为正常产品模式的应用组合根。后续不再把新的业务 orchestration 直接堆进 `Program` 或 `MainForm`。

### 模块契约

Phase 1 的模块机制采用适合 .NET Framework 4.8 的透明轻量实现，不复制 League Akari 的 decorator/reflection 细节，也不默认引入大型 DI 容器。

每个模块至少具有：

- 稳定模块 ID；
- 显式依赖列表；
- 初始化生命周期；
- 停止/释放生命周期；
- 自己拥有的 state / controller / settings 边界；
- 可测试的公共契约。

Host 负责：

- 拒绝重复模块 ID；
- 拒绝缺失依赖；
- 检测循环依赖；
- 按依赖拓扑顺序初始化；
- 关闭时按反向顺序停止/释放；
- 对初始化/释放失败写入明确诊断，不静默吞错。

### 生命周期可观测性

FacmHost 必须记录：

```text
FACM host initialized: <total ms>
Initialization order: A -> B -> C
A: <ms>
B: <ms>
C: <ms>
Slowest module: <id> (<ms>)
```

目的不是做用户可见性能面板，而是让“FACM 为什么启动变慢 / 哪个模块初始化失败”可以直接从现有日志定位。

### 纵向 feature 所有权

3.2 之后优先按 feature 组织复杂度，而不是继续把业务按技术类型散在整个主项目：

```text
Modules/Pets/
├─ PetModule
├─ PetController
├─ PetState
├─ existing Flying Runtime adapters
├─ existing VPet/PetHost adapter
└─ UI adapters

Modules/Online/
├─ OnlineModule
├─ OnlineController
├─ OnlineState
├─ existing OnlineService / UpdateInstaller adapters
└─ UI adapters
```

这里的“adapter”意味着**先包住已经验收的实现，再逐步收回所有权**，不是为了目录好看重写成熟代码。

### MainForm 的长期目标

`MainForm` 继续作为 FACM Shell 的 WinForms 表现层，但长期只应主要承担：

- 显示/隐藏 Shell；
- 鼠标拖动与 UI 事件；
- 绑定应用状态；
- 把用户操作转成模块命令；
- 呈现错误/状态反馈。

下面这些职责应逐步迁出 `MainForm`：

- Online 更新策略；
- PetHost/Flying 的业务编排；
- 应用模块 warmup 决策；
- 子功能窗口是否已打开等跨模块运行状态；
- 新增 League 功能的连接与业务状态。

### 迁移顺序

架构升级按小步迁移，不做大爆炸重写：

1. **Phase 1 / Issue #55**：`FacmHost + Module` 基础层、依赖解析、生命周期与启动可观测性；只接一个低风险样板模块。
2. Shell/Application lifecycle orchestration。
3. Settings。
4. Online。
5. Pets facade：先包住现有 `AnimalPetManager`，不改 Flying Runtime / VPet 行为。
6. Mayhem。
7. 建立真正的 LeagueClient module。
8. 在新架构上增加账号 / Gameflow / ChampSelect / 战绩等产品能力。

### 迁移期间必须保持的稳定契约

架构重构不得借机修改已经验收的产品行为：

- Issue #53 的 Mutex + AutoResetEvent 二次启动唤醒保持不变；
- Flying Runtime 的已验收轨迹、尺寸、素材/Profile 行为保持不变；
- VPet 继续由独立 `.NET 8 x64 / WPF` PetHost 承载；
- `settings.ini` 继续兼容；
- 海斗字段级多源容灾保持；
- Online Release/manifest 事务保持；
- `--cleanup` 和现有 smoke/test mode Mutex 语义保持；
- 架构阶段不自动触发新的正式 Release。

任何一项如果确实需要改变，应单独立 Issue、给出用户价值和迁移/回滚方案，而不是作为“架构整理”的顺带修改。