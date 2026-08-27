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

> 生产事实以 GitHub Release 与 `online/version.json` 为准。FACM 4.0 在 Gate 13 release 前不得修改生产更新指向。

## FACM 4.0 总进度

- Gate 0：COMPLETE，Issue #185 / PR #186，合入 `main@4eda40956a8f7394c1f588d441993e7eb9a4e3e3`。
- Gate 1：COMPLETE，Issue #187 / PR #188，合入 `main@22c6f55d5c84ff3b55720653dacbac6d49aa0934`。
- Gate 2：COMPLETE ON VERIFIED PR HEAD，Issue #189 / PR #190，最终实现 head `f11f4830507fa17d93de79816cd1066c2f8d25c3`；等待本 PR canonical 文档提交的 latest-head CI 后合入。
- Gate 3～13：按既定顺序连续推进，不要求用户逐 Gate 回复“继续”。

## Gate 0 — Migration Contract / Deployment Probe

已冻结 3.5.15 迁移不变量并建立 `docs/FACM-4-MIGRATION-CONTRACT.md`。技术路线：.NET 10 LTS + WinUI 3 + Windows App SDK Stable 2.4.0，先 x64。

Gate 0 真实 deployment probe 证明 unpackaged + self-contained + single-file 可 publish/启动；同时证明：

- `Environment.ProcessPath` 指向分发 EXE；
- `AppContext.BaseDirectory` 位于 `%TEMP%/.net/...` self-extract 目录；
- Updater、settings、cache、PetHost、runtime 数据不得把 self-extract BaseDirectory 当稳定安装目录。

## Gate 1 — Parallel Foundation

4.0 正式并行 solution：

```text
FACM4.sln
├─ FACM.Core                  net10.0
├─ FACM.Infrastructure        net10.0 -> Core
├─ FACM.Platform.Windows      net10.0-windows -> Core
├─ FACM.App                   WinUI 3 -> Core + Infrastructure + Platform.Windows
└─ FACM.FoundationSmoke       deterministic smoke
```

Gate 1 已建立：

- framework-neutral `FacmHost` 生命周期；
- Performance Budget / Policy；
- 3.5.15 15 键 `settings.ini` compatibility codec；
- `IUiTextProvider` foundation adapter；
- WinUI 单 Window / 单 NavigationView / 单 Frame / 单 ResourceDictionary；
- `WindowsExecutablePathProvider`；
- architecture check 与 4.0 CI；
- legacy `FACM.sln` / WinForms 3.5.15 构建链继续 green。

Gate 1 最终 merge 前 latest-head 验证：Foundation #12、Windows Build #1300、UI Text #421 全 SUCCESS。

## Gate 2 — Core / UI Decoupling：COMPLETE ON VERIFIED PR HEAD

Tracking：

- Issue #189：`FACM 4.0 Gate 2：Core/UI 解耦与业务能力契约`
- branch：`feat/facm-4-gate2-decouple`
- PR #190：`FACM 4.0 Gate 2：Core/UI 解耦与业务能力契约`
- verified implementation head：`f11f4830507fa17d93de79816cd1066c2f8d25c3`

### 已完成边界

Cleanup：

- `FACM.Core.Cleanup` 拥有 `CleanupPlan / CleanupResult / CleanupProgress`；
- `CleanupApplicationService` 只做 preview/confirmed execute orchestration；
- 未确认执行确定性拒绝；Core 不引用 WinForms progress/dialog/filesystem implementation。

League：

- `FACM.Core.League` 建立 session/read/write capability contracts；
- 写请求从 `LeagueWriteCapability` 映射到固定 method/path；调用方不能传任意 LCU URL/path；
- 当前 Gate 2 capability 只覆盖现有 my-selection / perk-page writer 范围；Bench/Matchmaking/PostGame/Presence/Client UX Repair 等旧窄 writer 不被合并成 generic writer；
- legacy `LeagueClientModule + LeagueClientSessionProvider` 仍是唯一实际 discovery/auth/session owner，Gate 2 没有创建第二连接器。

Online / Settings：

- Core 拥有 update manifest snapshot / decision / installer intent；
- `ISettingsRepository` + `IniSettingsRepository` 继续读写 3.5.15 15 键格式；Gate 2 不切 Settings 2.0；
- WinUI composition root 使用 `WindowsExecutablePathProvider.ExecutablePath` 的目录定位 `settings.ini`，保持 3.5.15 当前“分发 EXE 同目录”路径语义；明确禁止使用 `AppContext.BaseDirectory` 持久化配置；
- Gate 2 使用 `UnavailableUpdateManifestSource` 明确表示网络 transport 尚未迁入，而不是从 ViewModel 偷建 HttpClient。

WinUI intent boundary：

- `ControlCenterViewModel` 只依赖 Core `ISettingsRepository / IUpdateManifestSource` contract；
- `App.xaml.cs` 是 composition root，负责具体 Infrastructure / Platform adapter 注入；
- architecture gate 自动拒绝 `src/FACM.App/ViewModels` 直接引用 Infrastructure、Platform.Windows、HttpClient、System.IO、Process、Registry、具体 League session 或 URL。

### Gate 2 deterministic evidence

Foundation smoke 新增并已验证：

- Cleanup explicit-confirmation orchestration；
- League write capability exact allowlist；
- Online update decision；
- INI settings repository round-trip。

verified implementation head `f11f4830...`：

- `FACM 4.0 Foundation` #23：SUCCESS；
- `FACM Windows Build` #1305：SUCCESS；
- `FACM UI Text Contract` #426：SUCCESS；
- artifact `facm4-gate1-x64` id `9636874721`，ZIP size `88,192,643` bytes，digest `sha256:90000506bb5b8b32ca8ca4bd2ade71aacf5522668d4b82ccb24c27ebf4b3ce60`。

## Gate 3 — NEXT：.NET 10 Runtime / Transport Migration

Gate 2 合入后从最新 main 新开单独 Issue/branch/PR。Gate 3 只迁 runtime/transport/platform implementation，不顺手重画 UI、不切 Settings 2.0、不发布 4.0。

优先顺序：

1. League session descriptor/parser + Windows process/lockfile discovery adapter，保持唯一 owner 模型；
2. authenticated LCU read/write transport consume Core contracts，写 transport 必须再次校验 capability target；
3. Online update manifest HTTP adapter：有限 timeout、cancellation、大小上限、manifest validation；mirror/download/replace 继续分层；
4. Runtime path layout 统一从 distribution executable 推导稳定目录；禁止 `.net/...` self-extract 路径泄漏；
5. Windows-only process/registry/native handle 代码归 `FACM.Platform.Windows`；network/file persistence 归 Infrastructure；
6. deterministic smoke 覆盖 lockfile/command-line parser、auth header、writer capability fence、manifest parser/size/timeout、stable runtime paths；
7. legacy 3.5.15 Build/UI Text 继续 green。

## Gate 4 → Gate 13 固定顺序

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

## 持续保护的不变量

- Exactly one League discovery/auth/session owner。
- 所有 League writer 保持最小 capability allowlist；Bench 仍为用户手动动作。
- Mayhem/OP.GG 保留 fallback、timeout、body cancellation、cache、Performance Budget。
- Game Repair 保留 native Win32、多屏/负坐标、WinEvent debounce/cooldown、窄 restart-ux writer；不恢复旧 Fix-LCU runtime。
- Cleanup 保留 preview、explicit confirm、UAC、path allowlist、reparse-point guard、执行前规则重验证。
- Updater 保留 size limit、SHA-256、signature/package validation、validated receipt、独立 replacement、失败保旧版。
- Single Instance = Ensure Open；快捷键 = RegisterHotKey；不引入低级 Hook/轮询。
- PetHost 保持独立进程与 parent/job 生命周期。
- Performance Contract、UI Text Contract、deterministic smoke 不得静默删除。

## Gate 13 前必须补齐的真实 Windows 证据

这些不阻塞前面工程 Gate，但会阻止正式 4.0 cutover：

- 普通非管理员 runas/UAC 提升与取消；
- Defender / SmartScreen 对 self-extract EXE；
- Windows 10 1809/22H2 + Windows 11；
- 100/125/150/175/200% DPI、双屏、负坐标、上下/左右排列、混合 DPI；
- keyboard-only / focus / high contrast / text scaling / basic screen reader；
- updater interrupted replacement / rollback；
- 3.5.15 -> 4.0 settings 真机升级。

## 新对话接续规则

读取 `AGENTS.md + docs/PROJECT_STATE.md`，核对最新 `main`、当前 FACM 4.0 Issue/PR/CI 后直接从当前 Gate 继续；不要要求用户逐 Gate 回复“继续”。生产 release 与 destructive Git 操作仍按 `AGENTS.md` 做即时安全检查。
