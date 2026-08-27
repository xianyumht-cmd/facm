# FACM 架构

## 1. 当前双轨架构

FACM 正处于 3.5.15 -> 4.0 的受控迁移期。生产实现与 4.0 foundation 并行存在，直到 Gate 13 才允许删除 legacy。

```text
Production / rollback baseline
FACM.exe (.NET Framework 4.8 / WinForms)
├─ Shell / Floating Ball / Tray
├─ Settings / Online / Cleanup / Tools
├─ Theme / Window Chrome / UI Text
├─ Performance Contract
├─ League Client / Dashboard / Player / Live / OP.GG / Automation / Game Repair / Hub
└─ Mayhem

FACM.PetHost.exe (.NET 8 x64 / WPF / VPet Core)
└─ 独立辅助进程

FACM 4.0 parallel foundation
FACM.App (.NET 10 + WinUI 3)
├─ one Window
├─ one NavigationView / Frame visual tree
├─ Pages / dialogs / ViewModel adapters
└─ Theme ResourceDictionary
        ↓ intents / state
FACM.Core (net10.0, UI-framework-free)
├─ module lifecycle
├─ performance policy
├─ settings/text contracts
├─ application intents/results
├─ League capability/state contracts
├─ cleanup contracts
└─ update/product-state contracts
        ↓
FACM.Infrastructure (net10.0)          FACM.Platform.Windows (net10.0-windows)
├─ persistence/cache                   ├─ process/window/monitor/DPI
├─ HTTP/public data                    ├─ filesystem/registry/WMI
├─ online/update download              ├─ UAC/single-instance/hotkey
└─ structured diagnostics              └─ child process/replacement integration
```

`FACM4.sln` 是 4.0 并行 solution；旧 `FACM.sln` 在 Gate 13 前持续作为可构建回滚线。

## 2. 依赖方向

固定方向：

```text
FACM.App -> FACM.Core
FACM.App -> FACM.Infrastructure
FACM.App -> FACM.Platform.Windows
FACM.Infrastructure -> FACM.Core
FACM.Platform.Windows -> FACM.Core
FACM.Core -> nothing UI/platform-specific
```

禁止：

- Core -> WinUI / WinForms / WPF / System.Drawing；
- Core -> Windows process/filesystem/registry implementation；
- Infrastructure -> App/WinUI；
- Platform.Windows -> App/WinUI；
- Page/Form 自行创建第二套 League session、HttpClient、settings store 或 updater runtime。

`scripts/check-facm4-architecture.ps1` 与 `FACM 4.0 Foundation` workflow 自动守这条边界。

## 3. Core 模块生命周期

`FACM.Core.Application.IFacmModule`：

```text
Id
Dependencies
Initialize()
Dispose()
```

`FacmHost` 保持 3.5.15 已验收语义：依赖拓扑、重复/缺失/循环拒绝、逐模块 timing、失败模块释放、已初始化模块反向 rollback、正常反向 Dispose。UI 不拥有模块生命周期。

## 4. Performance owner

`FACM.Core.Performance` 是 4.0 性能预算 owner。预算保持：

| State | Network | Image | Disk | CPU | Prefetch | Poll |
|---|---:|---:|---:|---:|---:|---:|
| Desktop | 4 | 2 | 2 | 2 | 20 | 15s |
| League Client | 3 | 2 | 2 | 2 | 12 | 20s |
| Queueing | 2 | 1 | 1 | 1 | 4 | 30s |
| Champ Select | 2 | 1 | 1 | 1 | 0 | 45s |
| In Game | 1 | 1 | 1 | 1 | 0 | 60s |
| Background | 1 | 1 | 1 | 1 | 0 | 60s |

优先级：`InGame > ChampSelect > hidden/background > Queueing > Client > Desktop`。窗口最小化绝不能成为游戏中增加后台工作的理由。

## 5. Settings / UI Text

Gate 1 保持 3.5.15 compatibility，不提前切 Settings 2.0：

- `LegacySettingsCodec` 读取/序列化 15 个稳定 `settings.ini` 键；
- 默认主题 `glass-blue`；默认宠物 `greenfly`；旧合法 ID 继续识别；
- `IUiTextProvider` 是框架无关文字入口；legacy `[Text]` override 由 Infrastructure adapter 解析；
- Gate 4 才引入 versioned typed schema/atomic save/migration。

## 6. League ownership

### 唯一连接所有者

4.0 继续只有一个 League discovery/auth/session owner。迁移期间 legacy owner 仍为 `LeagueClientModule + LeagueClientSessionProvider`；后续移动实现时只能移动 owner，不能复制 owner。

所有模块必须消费同一个 session/runtime：Dashboard、Player、Live、OP.GG、Mayhem、Efficiency、Game Repair、Matchmaking/PostGame/Presence 等不得各自发现进程、读 lockfile 或创建长期 LCU client。

### Writer capability

LCU 写操作始终通过最小 allowlist capability：Gate2～Gate7 writer、Bench、Matchmaking、PostGame、Presence、Client UX Repair 等边界不得合并成任意 path request API。

Bench 仍是用户点击触发的手动快速选择，不允许升级为后台自动抢英雄。

### Game Repair

正式实现保持原生 Windows 方案：实际显示器/working area、多屏/负坐标、WinEvent location-change + debounce/cooldown；play-again 复用 post-game writer；restart UX 使用专用最小 writer。禁止恢复第二个 fix-lcu runtime。

## 7. Cleanup ownership

3.5.15 `SafeCleanupService` 当前仍混有 WinForms progress UI，这是 Gate 2 的明确拆分点。最终边界：

```text
Page/Form -> Cleanup Intent -> Core application service
                           -> platform filesystem/elevation adapter
                           -> Cleanup Plan / Result / Progress
```

清理不变量：先预览再确认、游戏根目录验证、路径白名单、reparse-point 防护、UAC、执行前规则重验证、失败逐项记录。

## 8. Online / Update ownership

Core 只理解 update manifest/state/intents；Infrastructure 负责 HTTP/mirror/download；Platform/Updater 负责替换。

Gate 0 已证明 single-file self-extract 下：

- `Environment.ProcessPath` = 分发 EXE；
- `AppContext.BaseDirectory` = `%TEMP%/.net/...` extraction directory。

Updater 替换目标只能来自 distribution executable path。更新安全继续保留 size limit、SHA-256、signature/package validation、validated receipt、wait/replace/rollback/keep-old-version。

## 9. Shell 信息架构

控制中心固定四入口：

```text
清理与修复
LOL 工作台
个性化
更多设置
```

LOL 工作台继续面向用户组织为 `比赛 / 攻略 / 自动化`，不按内部 class/module 名称堆菜单。WinUI Shell 采用单 Window / 单 TitleBar owner / 单 navigation visual tree，禁止复制旧 Form-in-Form 模式。

## 10. Desktop Shell / PetHost

- 默认 `F` 悬浮入口后续由 Gate 7 纳入全局 Theme Resources；
- Anchor Placement Service 必须按所在显示器/边缘/working area 放置，支持负坐标和混合 DPI；
- Single Instance 语义固定为 **Ensure Open / Activate**，不是 toggle；
- 快捷键使用 RegisterHotKey，不引入低级键盘 Hook/永久轮询；
- PetHost 保持独立进程、IPC、Job Object/parent-pid 生命周期，不因 WinUI 迁移并入主 UI 进程。

## 11. 状态与可观测性目标

Gate 5 引入统一 Product State：Application / League / Environment / Services。League 至少覆盖：

`NotRunning / Connecting / Lobby / Matchmaking / ReadyCheck / ChampSelect / InGame / PostGame / ClientError`。

页面订阅 state，不重复轮询。结构化诊断至少携带 `ActionId / Module / Duration / Result / Reason / LeagueState / ClientVersion`，供 Gate 9 诊断中心消费。

## 12. 测试与发布边界

迁移期间同时维护：

- legacy `FACM Windows Build`；
- `FACM UI Text Contract`；
- `FACM 4.0 Foundation`；
- 各业务 deterministic smoke。

已有 smoke 只能迁移或被等价/更强验证替代，不能静默删除。

Gate 13 之前 `online/version.json` / `release/request.json` 保持生产 3.5.15。只有 Gates 0～12 全绿、3.5.15 配置迁移验证、Updater rollback、Windows 10/11 + DPI/多屏/accessibility 实机矩阵通过后，才允许退休 legacy 并切 FACM 4.0.0。
