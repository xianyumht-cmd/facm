# FACM 当前项目状态

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（生产冻结线）

- 版本：FACM 3.5.15
- GitHub Release：`v3.5.15`
- 在线更新：已启用
- `minimum_version`：3.0.0
- `force_update`：false
- Release FACM.exe SHA-256：`E3B415375E204212EE2D7A36D4A038708DC75694CD9B6FD28F2761BBF1FD01CE`
- `published_at`：2026-08-27T05:28:50.9137418+00:00
<!-- FACM_RELEASE_STATE_END -->

> 生产事实以 GitHub Release 与 `online/version.json` 为准。FACM 4.0 迁移在正式 Gate 13 release 前不得修改生产更新指向。

## 当前主线：FACM 4.0

### Gate 0 — COMPLETE

- Issue #185；PR #186。
- 已合入 `main`：`4eda40956a8f7394c1f588d441993e7eb9a4e3e3`。
- 产物：`docs/FACM-4-MIGRATION-CONTRACT.md`、隔离 WinUI deployment probe、Windows CI。
- 技术基线：.NET 10 LTS + WinUI 3 + Windows App SDK Stable 2.4.0。
- Gate 0 真实证据：unpackaged/self-contained/single-file 可 publish/启动；空壳单 EXE 227,426,290 bytes；首次 CI 启动约 2340 ms，第二次约 293 ms。
- `Environment.ProcessPath` 指向分发 EXE；`AppContext.BaseDirectory` 位于 `%TEMP%/.net/...` 自解包目录。Updater 必须替换前者，禁止把 self-extract BaseDirectory 当安装目录。

### Gate 1 — COMPLETE / delivery PR #188

Tracking：

- Issue #187：`FACM 4.0 Gate 1：并行 Solution、Core/Platform/App 骨架与架构门禁`
- branch：`feat/facm-4-gate1-foundation`
- PR #188：`FACM 4.0 Gate 1：并行 Solution 与 WinUI Foundation`
- 最终 Gate 1 head：`f869b27ef91b722ff217912833257136230c4d43`

已建立正式并行 4.0 foundation：

```text
FACM4.sln
├─ FACM.Core                  net10.0
├─ FACM.Infrastructure        net10.0 -> Core
├─ FACM.Platform.Windows      net10.0-windows -> Core
├─ FACM.App                   WinUI 3 -> Core + Infrastructure + Platform.Windows
└─ FACM.FoundationSmoke       deterministic foundation smoke
```

Gate 1 已完成：

- legacy `FACM.sln` / `src/FACM` / Updater / ToolBundle / PetHost 保持原样可构建，仍是 3.5.15 rollback baseline；
- `FACM.Core` 无 WinForms / WinUI / WPF / `System.Drawing` / package/project dependency；
- 抽离 `FacmHost` 生命周期契约：拓扑初始化、依赖缺失/循环拒绝、失败模块释放、已初始化模块反向 rollback、反向 Dispose、timing report；
- 抽离 Performance Budget / Policy，保持 Desktop/Client/Queueing/ChampSelect/InGame/Background 原预算，并保持 InGame/ChampSelect 优先于窗口可见性的规则；
- 建 3.5.15 `settings.ini` compatibility codec，保持 15 个稳定键、默认 `glass-blue` 主题、默认 `greenfly` 宠物及旧 ID fallback；
- 建框架无关 `IUiTextProvider` 与 foundation text key/default adapter；
- `FACM.Platform.Windows` 首个路径契约明确区分 distribution executable 与 self-extract base directory；
- `FACM.App` 已是一个 Window / 一个 NavigationView / 一个 Frame / 一个 ResourceDictionary foundation，不建立第二套 League runtime；
- `scripts/check-facm4-architecture.ps1` 自动拒绝 Core UI framework 依赖、错误项目引用方向、迁移分支修改生产 release controls；
- `.github/workflows/facm4-foundation.yml` 自动 restore/build/smoke/publish x64 self-contained single-file candidate。

Gate 1 最终 CI：

- `FACM 4.0 Foundation` #6：SUCCESS；
- `FACM Windows Build` #1297：SUCCESS；
- `FACM UI Text Contract` #418：SUCCESS；
- artifact：`facm4-gate1-x64`，artifact id `9636175208`，ZIP size 88,182,972 bytes，digest `sha256:e574ec965f7b3dffa3f473f01e0312ca2a5432a366e40d62aa1fd07737f5e81a`。

Gate 1 CI 过程中修正过两类 foundation 问题：

1. 架构脚本最初用 regex 判断 Windows ProjectReference 路径，误报合法 `Infrastructure -> Core`；已改为解析 XML 后比较规范化项目名。
2. `TreatWarningsAsErrors` 抓到 legacy settings normalization 两处 nullable 赋值；保留严格编译策略并修正类型安全，没有关闭 warning gate。

## Gate 2 — NEXT：Core / UI 解耦

按以下顺序直接推进，不等待用户逐 Gate 回复：

1. 把 Cleanup 的 plan/result/application intent 从 `SafeCleanupService` 中的 WinForms progress UI 分离；Core 不引用 `Application.MessageLoop` / Form。
2. 建 League framework-neutral session/read/write capability contracts；唯一 `LeagueClientModule + LeagueClientSessionProvider` 仍是 discovery/auth/session owner，不新增连接器。
3. 把 Online/update 的 manifest/check/install intent 与 UI 分离；下载/校验/替换继续归 Infrastructure/Platform/Updater owner。
4. Settings 先通过 compatibility repository contract 读写现有 INI；Gate 2 不提前切 Settings 2.0 schema。
5. WinUI Page/ViewModel 只发 intent/订阅 state，不直接找进程、读写 settings、创建 HttpClient 或构造 LCU session。
6. legacy WinForms 继续工作；必要时通过 adapter 消费新 Core，不以删除 legacy 作为解耦手段。
7. deterministic smoke 覆盖依赖方向、Cleanup orchestration、League capability allowlist、settings/text compatibility。

## Gate 3 → Gate 13 固定顺序

- Gate 3：主业务迁 `.NET 10`，专项处理 Win32/PInvoke、Registry、Process、NamedPipe、HttpClient、资源与线程模型；不顺手重画 UI。
- Gate 4：Settings 2.0，typed/versioned/validated/atomic-save/migration/defaults/module ownership；3.5.15 INI 无损迁移。
- Gate 5：Product State + Observability；统一 Application/League/Environment/Services 状态与结构化日志。
- Gate 6：WinUI 3 Design System + Shell；semantic tokens、统一控件、单 Window/TitleBar/navigation visual tree。
- Gate 7：Desktop Shell / F 悬浮球 / 全局 Theme；Anchor Placement Service 支持负坐标、多屏、混合 DPI。
- Gate 8：LOL 工作台状态驱动 UX；围绕 Gameflow 消费唯一 League runtime。
- Gate 9：诊断中心；复制摘要/导出脱敏诊断包，不含 token/cookie/password。
- Gate 10：DPI / 多屏 / Accessibility 发布门槛。
- Gate 11：Recovery / Feature Flags / 更新保障；kill switch 只能降级/关闭，不能扩大写权限或静默启用自动化。
- Gate 12：全量兼容 / 性能 / 发布矩阵；现有 deterministic smoke 必须迁移或由更强验证替代。
- Gate 13：Legacy 删除与 FACM 4.0 正式切换；只有前面 Gate 全绿且配置迁移、更新链、Windows 实机矩阵成立后才允许发布 4.0.0。

## FACM 4.0 必须持续保护的不变量

- 唯一 League discovery/auth/session owner。
- Gate2～Gate7、Bench、Matchmaking、PostGame、Presence、Client UX Repair 等写能力继续使用最小 allowlist writer；不得扩权。
- Bench 只允许用户手动快速选择，不变成自动抢英雄。
- Mayhem/OP.GG 保留 fallback、超时、正文取消、缓存与 InGame 性能预算。
- Game Repair 保留原生 Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer。
- Cleanup 保留预览、UAC、路径白名单、reparse-point 防护和规则重验证。
- Updater 保留下载限制、SHA-256、签名/package validation、validated receipt、独立替换、失败保旧版。
- Single Instance = Ensure Open，不是 Toggle；快捷键继续 RegisterHotKey，不引入低级键盘 Hook/轮询。
- PetHost 保持独立进程、IPC、Job Object/parent-pid 生命周期。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## 尚需真实 Windows 机器关闭的发布风险

这些不阻塞前面的工程 Gate，但会阻止 Gate 13 正式发布，直到证据齐全：

- 普通非管理员用户下 runas/UAC 提升、取消和重启路径；
- Defender / SmartScreen 对 self-extract 单 EXE 的首次扫描、误报与冷启动；
- Windows 10 1809/22H2 与 Windows 11 真机；
- 100/125/150/175/200% DPI、双屏、负坐标、上下/左右排列、混合 DPI；
- keyboard-only / focus / high contrast / text scaling / basic screen reader；
- updater interrupted replacement / rollback 与 3.5.15 -> 4.0 settings migration 真机升级。

## 新对话接续规则

读取 `AGENTS.md + docs/PROJECT_STATE.md` 后，优先核对最新 `main`、当前未合并 FACM 4.0 PR/Issue 与 CI。不要要求用户逐 Gate 回复“继续”；在安全门禁允许时按 Gate 顺序自动推进。生产 release、 destructive Git 操作仍需按 `AGENTS.md` 做即时安全检查。
