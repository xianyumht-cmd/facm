# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。当前主线是 **FACM 3.2 后端架构升级**。Phase 1（Issue #55 / PR #56）已合并到 `main`；Phase 2（Issue #57 / PR #58）正在把 Settings / UiText 所有权从 `MainForm` 迁入 `SettingsModule`。Phase 2 当前行为代码 HEAD `235299eda170835d13c4035efa617d433db306a3` 的 FACM Windows Build #846 与 Mayhem Source Probe #159 均 SUCCESS。内部 Phase 不逐次要求 Windows 实机测试，整轮后端重构收口后再提供单一最终候选包集中验收。当前没有正式发布动作。

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

- Issue #55：`FACM 3.2 Phase 1：建立 FacmHost 与模块生命周期基础层`，已 closed / completed。
- PR #56：`refactor(core): establish FACM 3.2 modular host foundation`，已 merged。
- merge commit：`8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`。
- 最终行为代码 HEAD：`9b6b1a7da0829b8c18e8cd5a9ca5d0e169e54447`。
- 行为验证：FACM Windows Build #842 SUCCESS；Mayhem Source Probe #155 SUCCESS。
- 最终 docs-only HEAD：`7e0a589e0d5537a9d698d837e27bb9b80f401ae4`。
- docs-only 验证：FACM Windows Build #843 SUCCESS；Mayhem Source Probe #156 SUCCESS。
- Phase 1 建立 `FacmHost / IFacmModule`、依赖图、失败回滚、反向释放、成功/失败 timing、ShellModule 与 `--facm-host-test`。
- 旧任务分支 `feat/facm-host-phase1-55` 未自动删除；分支删除需要用户明确意图。

## 当前 Phase 2

- Issue #57：`FACM 3.2 Phase 2：Settings ownership 与 Shell 显式依赖`。
- PR #58：`refactor(settings): move settings ownership into FacmHost`，当前 OPEN / DRAFT。
- 分支：`refactor/settings-shell-phase2-57`。
- 当前行为代码 HEAD：`235299eda170835d13c4035efa617d433db306a3`。
- FACM Windows Build #846：SUCCESS。
- FACM Mayhem Source Probe #159：SUCCESS。

Phase 2 已实现：

- `SettingsModule` 由正常产品 Host 注册；
- `ShellModule` 显式依赖 `CompactMenuEnhancerModule + SettingsModule`；
- `SettingsModule.Initialize()` 负责 `AppSettings.Load()` 与 `UiTextCatalog.Load()`；
- `ShellModule` 在 Settings ready 后创建 `MainForm(settings, uiText, startCleanup)`；
- `MainForm` 不再自行调用 `AppSettings.Load()` / `UiTextCatalog.Load()`；
- MainForm 原有 `_settings.Save()` 时机、`settings.ini` key/default/migration 全部保持；
- `--facm-host-test` 锁定 Shell→Settings dependency contract。

### Phase 2 已修复失败

初版 HEAD `1d388ab165719df434a3c917d8202a66c34c0333` 的 Build #845 在 FACM 编译阶段失败：`FloatingBallSmokeTest` 仍使用旧 `new MainForm(false)` 构造函数，产生 CS7036。PetHost publish/self-test 当时成功。

修复没有恢复 MainForm 隐式加载，而是把 deterministic test 也改为显式注入：

```text
new MainForm(new AppSettings(), UiTextCatalog.Load(), false)
```

修复后 Build #846 / Probe #159 全绿。这个教训记录到 `docs/PITFALLS.md`：把构造依赖显式化时必须同时搜索产品与 deterministic test 的所有实例化点。

## 线上正式版

仍是 FACM 3.1.3：

- `enabled=true`
- `version=3.1.3`
- `minimum_version=3.0.0`
- `force_update=false`
- SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`

架构 PR、CI artifact 和内部 Phase 合并均不等于正式 Release 授权；不要自行创建 v3.2.0 / v3.1.4 或修改线上 manifest。

---

# 二、FACM 3.2 当前架构

目标是吸收 League Akari 的模块所有权、显式依赖、统一生命周期、状态/设置归属和可观测性原则，但保留 FACM 的 .NET Framework 4.8 / WinForms 主程序以及独立 .NET 8 x64 / WPF PetHost。

当前已落地的正常启动骨架：

```text
Program
  -> SettingsModule
  -> ShellModule(SettingsModule)
  -> FacmHost.Register(CompactMenuEnhancer, Settings, Shell)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
       -> ShellModule -> MainForm(settings, uiText)
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

稳定 namespace：`FACM.AppHost` / `FACM.AppHost.Modules`。不要恢复 `FACM.Application`，Build #821 已证明其会遮蔽 `System.Windows.Forms.Application`。

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

- 普通实例 Mutex + 当前会话 AutoResetEvent 二次启动 Ensure Open/Activate 语义；
- `--cleanup` 与各 smoke/test 独立 Mutex；
- 五种 Flying Runtime 已验收轨迹、素材/Profile、自由出屏；
- VPet 独立 PetHost、Job Object、parent-pid、bundle SHA、ready/fallback；
- `settings.ini` 现有键、默认值、迁移与写回兼容；
- Mayhem 字段级多源容灾、国内优先、腾讯 Patch、LCU/DataDragon fallback；
- Online Release/manifest 事务；
- 当前用户可见 UI/交互，除非另有独立产品需求；
- 正式 Release 必须单独授权。

---

# 五、下一步

Phase 2 canonical docs/review/CI 收口并合并后，**不要求用户实机测试，直接继续 Phase 3**：

1. Host 注册 `ToolsModule / OnlineModule / PetsModule / MayhemModule`；
2. `ShellModule` 显式依赖这些模块；
3. MainForm 构造函数接收这些 facade；
4. 用 facade 替换 `ToolRunner / ToolBundleLoader / OnlineService / AnimalPetManager / PetHostBundleLoader / new MayhemLookupForm()` 等 direct static/direct-new 依赖；
5. 保持后台 warmup 的 180ms head-start 和“仅已启用桌宠时才预热 PetHost”语义；
6. 之后再做 Cleanup ownership；
7. 最后建立真正的 LeagueClient module foundation，再形成整轮架构重构的单一 Windows 候选包。

---

# 六、给下一会话的一句话

**FACM 3.2 Phase 1 已通过 PR #56 合并到 main（`8bb44cf...`）；Phase 2 为 Issue #57 / Draft PR #58 / `refactor/settings-shell-phase2-57`，当前代码 HEAD `235299e...` 的 Build #846 + Probe #159 SUCCESS。Settings/UiText 已由 SettingsModule 所有并显式注入 Shell/MainForm；Build #845 仅因 FloatingBallSmokeTest 漏改旧构造函数失败，已修。不要要求用户中途实机测试；Phase 2 合并后直接继续 Tools/Online/Pets/Mayhem 显式依赖，整轮后端重构结束后再给单一最终 Windows 候选包。**