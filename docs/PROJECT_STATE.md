# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。当前主线是 **FACM 3.2 后端架构升级**。Phase 1 / 2 / 3 已合并；Phase 4 Cleanup ownership 在聊天 UI 超时期间曾发生一次远端提前合并半成品（PR #62），Issue #61 已重新打开并由 follow-up PR #64 完整修正。当前 Phase 4 行为 HEAD `16cefad9162de302de68478cde2a3d6ed9b49d0c` 的 FACM Windows Build #858 与 Mayhem Source Probe #169 均 SUCCESS。内部 Phase 不逐次要求 Windows 实机测试，整轮后端重构收口后再提供单一最终候选包集中验收。当前没有正式发布动作。

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.1.3
- GitHub Release：v3.1.3
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`
<!-- FACM_RELEASE_STATE_END -->

## 新对话优先读取

1. `AGENTS.md`：仓库强制规则；canonical branch 为 `main`，一任务一短分支，发布/部署/destructive refs 必须单独明确授权。
2. 本文件：当前已验证状态、活动 Issue/PR、CI、冻结契约和下一步。
3. `docs/ARCHITECTURE.md` / `docs/DECISIONS.md`：3.2 modular-host 架构、模块所有权与迁移边界。
4. `docs/AI_WORKSTYLE.md`：长架构重构连续推进，整轮收口后再做一次集中 Windows 实机验收。
5. `docs/PITFALLS.md`：WinForms、PetHost、单实例、Mayhem 和 modular-host 防回归规则。
6. `docs/OPERATIONS.md`：核心 CI、`--facm-host-test`、最终候选和发布边界。

---

# 一、仓库 / 发布状态

## Phase 1 已完成

- Issue #55 / PR #56：`FacmHost + IFacmModule`、依赖图、失败回滚、反向释放、成功/失败 timing、ShellModule 与 `--facm-host-test`。
- merge commit：`8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`。
- 最终行为验证：Build #842 / Probe #155 SUCCESS。
- 最终 docs-only 验证：Build #843 / Probe #156 SUCCESS。

## Phase 2 已完成

- Issue #57 / PR #58：Settings ownership 与 Shell 显式依赖。
- merge commit：`64182dddeaa8a89f8d70a31e5ca3307dd2098ba7`。
- `SettingsModule.Initialize()` 负责 `AppSettings.Load()` / `UiTextCatalog.Load()`；MainForm 不再自行加载。
- Build #845 曾因 `FloatingBallSmokeTest` 漏改旧 MainForm 构造调用而失败，已修；不是架构方案失败。
- 最终行为验证：Build #846 / Probe #159 SUCCESS。
- 最终 docs-only 验证：Build #849 / Probe #162 SUCCESS。

## Phase 3 已完成

- Issue #59 / PR #60：Shell 显式依赖 Tools / Online / Pets / Mayhem。
- merge commit：`974d2bbde73fe78b25052392adc9258c7c20493e`。
- MainForm 后端调用已经切到 `_tools / _online / _pets / _mayhem` facade。
- 保持 Shell 先绘制、约 180ms warmup head-start、PetHost 默认冷启动、Pets ready/fallback、Online prompt、Mayhem modal 和 Tool error UI。
- 行为验证：Build #851 / Probe #164 SUCCESS。
- 最终 docs-only 验证：Build #853 / Probe #166 SUCCESS。

## 当前 Phase 4：Cleanup ownership

- Issue #61：`FACM 3.2 Phase 4：Cleanup ownership 与控制中心后端解耦`，当前 OPEN（超时恢复后重新打开）。
- 分支：`refactor/cleanup-ownership-phase4-61`。
- follow-up PR #64：`fix(cleanup): complete Phase 4 ownership after timeout merge`，当前 OPEN / DRAFT，等待 docs-only CI/review 收口。
- 行为代码 HEAD：`16cefad9162de302de68478cde2a3d6ed9b49d0c`。
- FACM Windows Build #858：SUCCESS。
- FACM Mayhem Source Probe #169：SUCCESS。

Phase 4 当前完整实现：

- `CleanupModule` 承接当前真实 cleanup backend：
  - `IsConfigured`
  - `IsAdministrator`
  - `RestartElevatedForCleanup()`
  - `GetRunningRelatedProcesses()`
  - `FindGameRoot()`
  - `ResolveGameRoot(path)`
  - `IsValidGameRoot(path)`
  - `CreatePlan(gameRoot)`
  - `Execute(plan) -> CleanupResult`
- 正常 Host 注册 `CleanupModule`；
- `ShellModule` 显式依赖 Cleanup；
- 同一个 CleanupModule 经 `Shell -> MainForm -> CompactMenuForm` 注入；
- `CompactMenuForm` 不再直接调用 `CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService` backend；
- MessageBox、FolderBrowserDialog、CleanupReviewForm、状态文字等仍属于 UI；
- `SafeCleanupService` 的安全算法、reparse 防护、执行前重校验和 `BackgroundOperationDialog` worker-thread 路线完全未重写；
- `GameLocator` 自身的搜索预算、取消/进度窗口语义也未修改；
- FloatingBall smoke 与 Host dependency smoke 已同步 Cleanup 构造/依赖契约。

### 超时恢复记录：PR #62

聊天 UI 出现“消息发送超时”期间，远端 GitHub 操作仍继续执行，导致 PR #62 在前端没有显示完整过程时已经被合并到 `main`，merge commit：

`c9596de1928ca714b46916b8d3708a2b9fd92160`

但该 PR 的 HEAD 只有 `e15877ac349282f4751b261088c7ed11393ceba6`，只包含最早的 `CleanupModule.cs` 单文件草稿，并没有完成 Phase 4；其中还存在按旧计划猜测的 facade 接口。由于 PR body 使用 `Closes #61`，Issue #61 被自动关闭。

恢复策略：

- 不回滚 `main`；
- 不 reset/rebase/force-push；
- 重新打开 Issue #61；
- 继续使用同一任务分支；
- PR #64 只提交 #62 之后的修正与完整迁移。

因此 **PR #62 merged ≠ Phase 4 已完成**；Phase 4 的有效完成候选是 PR #64。

### Phase 4 CI 失败记录

Build #857：FAILED，PetHost publish/self-test 正常，net48 C# 编译失败。

根因：`CompactMenuForm` 新增构造参数命名为 `cleanup`，而构造函数原本已有 UI 卡片局部变量 `var cleanup = CreatePanel(...)`；C# 禁止参数与同一作用域局部变量重名，出现 CS0841 / CS0136。

修复仅把构造参数改为 `cleanupModule`，保留 UI 局部变量及全部业务行为。修复后 Build #858 SUCCESS。

## 线上正式版

仍是 FACM 3.1.3：

- `enabled=true`
- `version=3.1.3`
- `minimum_version=3.0.0`
- `force_update=false`
- SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`

内部架构 PR、CI artifact 和合并都不等于正式 Release 授权。

---

# 二、FACM 3.2 当前目标依赖图（Phase 4）

```text
Program
  -> SettingsModule
  -> ToolsModule
  -> OnlineModule
  -> PetsModule
  -> MayhemModule
  -> CleanupModule
  -> ShellModule(...all facades...)
  -> FacmHost.Register(...)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
       -> ToolsModule
       -> OnlineModule
       -> PetsModule
       -> MayhemModule
       -> CleanupModule
       -> ShellModule
            -> MainForm(settings, uiText, tools, online, pets, mayhem, cleanup)
                 -> CompactMenuForm(..., cleanup)
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

稳定 namespace：`FACM.AppHost` / `FACM.AppHost.Modules`。不要恢复 `FACM.Application`。

---

# 三、长重构验收节奏

用户已明确：**不要在每个内部 Phase / 模块迁移后停下来要求 Windows 实机测试。**

固定方式：

1. 按依赖顺序连续完成后端架构阶段；
2. 中间阶段由代码审查、AppLog、deterministic smoke、编译和 GitHub Actions 收敛；
3. 自动验证成功后继续下一 Phase；
4. 整轮既定后端架构重构完成并形成单一 Windows 候选包后，再让用户集中实机验收一次；
5. 只有真实 Windows 行为成为自动化无法解除的 blocker，才提前请求测试。

---

# 四、冻结契约

架构重构不得顺手改变：

- 普通实例 Mutex + 当前会话 AutoResetEvent 二次启动 Ensure Open/Activate；
- `--cleanup` 与 smoke/test 独立 Mutex；
- 五种 Flying Runtime 已验收行为；
- VPet 独立 PetHost、Job Object、parent-pid、bundle SHA、ready/fallback；
- `settings.ini` key/default/migration/write-back；
- Cleanup 白名单、reparse 防护、执行前重校验、BackgroundOperationDialog worker 语义；
- GameLocator 搜索预算、取消/进度语义；
- Mayhem 字段级多源容灾、国内优先、腾讯 Patch、LCU/DataDragon fallback；
- Online Release/manifest 事务；
- 当前用户可见 UI/交互，除非另有独立产品需求；
- 正式 Release 必须单独授权。

---

# 五、下一步：Phase 5 LeagueClient foundation

PR #64 docs/review/CI 收口并合并后，**不要求用户实机测试，直接开新的 Issue + task branch 做 LeagueClient foundation**。

当前已确认的 LCU 技术债集中在 `Mayhem/RiotGameDataService.cs`：

- `DiscoverLcuSession()` 每次从 `LeagueClientUx` / `LeagueClient` 进程找到可执行文件目录；
- 读取同目录 `lockfile`；
- 从 lockfile 解析端口、密码、protocol；
- 连接 `protocol://127.0.0.1:<port>/`；
- 使用 Basic Auth：`riot:<password>`；
- 本地 HTTPS 允许 League Client 自签证书；
- 当前每次 LCU bytes 请求都会重新 discovery 并新建 handler/client；
- Mayhem 用它读取 `/lol-game-data/assets/v1/...`，失败后回退 CommunityDragon/DataDragon。

Phase 5 目标不是立即新增账号/选人/战绩 UI，而是先建立：

```text
LeagueClientModule
├─ client/lockfile discovery
├─ session/credential ownership
├─ authenticated local HTTP boundary
├─ bounded timeout + cancellation
├─ connection diagnostics/state
└─ deterministic parser/API smoke
```

并让 `MayhemModule` 显式依赖 LeagueClient，再把该依赖传到 `MayhemLookupForm / RiotGameDataService`，移除 Mayhem 内部重复 discovery/auth 所有权。**不得为了少改代码新增全局 static LeagueClient singleton。**

腾讯/国服兼容性按源码机制 + 国服实测逐项判断，不根据 League Akari 官网“不支持腾讯服务器”的免责声明推导技术不可用。

完成 Phase 5 后，本轮“后端架构重构”进入整体候选收口，再给用户单一 Windows candidate 做一次集中实机验收。

---

# 六、给下一会话的一句话

**FACM 3.2 Phase 1/2/3 已合并；Phase 4 Issue #61 在聊天 UI 超时期间曾被 PR #62 误提前合并单文件草稿并自动关闭，现已重开，由 follow-up PR #64 / `refactor/cleanup-ownership-phase4-61` 完整修正。行为 HEAD `16cefad...` 的 Build #858 + Probe #169 SUCCESS；Cleanup backend 已通过 CleanupModule 从 CompactMenuForm 迁出，同时 SafeCleanupService 的 BackgroundOperationDialog/安全算法和 GameLocator 行为保持不变。完成 #64 docs-only CI/review 后合并，不要求用户中途实机测试，然后直接做 LeagueClient foundation；整轮完成后给单一 Windows 候选包。**
