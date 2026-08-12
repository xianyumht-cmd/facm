# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。当前主线是 **FACM 3.2 后端架构升级**。Phase 1（#55/#56）与 Phase 2（#57/#58）均已合并到 `main`；Phase 3（Issue #59 / PR #60）已完成 Tools / Online / Pets / Mayhem 的 Shell 显式依赖迁移。Phase 3 当前行为代码 HEAD `10a81d38a530e99eb77eab1a7d2f1c19c46e9279` 的 FACM Windows Build #851 与 Mayhem Source Probe #164 均 SUCCESS。内部 Phase 不逐次要求 Windows 实机测试，整轮后端重构收口后再提供单一最终候选包集中验收。当前没有正式发布动作。

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
- `ShellModule` 显式依赖 Settings，并创建 `MainForm(settings, uiText, ...)`。
- Build #845 曾因 `FloatingBallSmokeTest` 漏改旧 MainForm 构造调用而失败，已修；不是架构方案失败。
- 最终行为验证：Build #846 / Probe #159 SUCCESS。
- 最终 docs-only 验证：Build #849 / Probe #162 SUCCESS。

## 当前 Phase 3

- Issue #59：`FACM 3.2 Phase 3：Shell 显式依赖 Tools / Online / Pets / Mayhem`。
- PR #60：`refactor(shell): inject Tools Online Pets Mayhem modules`，当前 OPEN / DRAFT。
- 分支：`refactor/shell-feature-facades-phase3-59`。
- 当前行为代码 HEAD：`10a81d38a530e99eb77eab1a7d2f1c19c46e9279`。
- FACM Windows Build #851：SUCCESS。
- FACM Mayhem Source Probe #164：SUCCESS。

Phase 3 已实现：

- 正常 Host 注册 `ToolsModule / OnlineModule / PetsModule / MayhemModule`；
- `ShellModule` 显式依赖 enhancer + Settings + Tools + Online + Pets + Mayhem；
- MainForm 构造函数显式接收这四个 facade；
- `ToolRunner / ToolBundleLoader` direct calls 改走 `ToolsModule`；
- `OnlineService` direct calls 改走 `OnlineModule`；
- `AnimalPetManager / PetHostBundleLoader` direct calls 改走 `PetsModule`；
- `new MayhemLookupForm()` 改走 `MayhemModule.CreateLookupForm()`；
- `FloatingBallSmokeTest` 同步到新构造契约；
- `--facm-host-test` 锁定真实 Shell feature dependency 顺序。

### Phase 3 保持的时序

- Shell 仍先显示；
- background warmup 仍先等待约 180ms，让消息循环先绘制；
- ToolBundle 仍后台准备；
- 只有启动时 `AnimalPetEnabled=true` 才预热 PetHost；
- Pets ready/fallback、Online prompt、Mayhem modal 和 Tool error UI 不变；
- MainForm 退出/关闭时仍主动 `_pets.Stop()`，Host 的 `PetsModule.Dispose()` 只作为最终生命周期兜底。

## 线上正式版

仍是 FACM 3.1.3：

- `enabled=true`
- `version=3.1.3`
- `minimum_version=3.0.0`
- `force_update=false`
- SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`

内部架构 PR、CI artifact 和合并都不等于正式 Release 授权。

---

# 二、FACM 3.2 当前正常启动依赖图

```text
Program
  -> SettingsModule
  -> ToolsModule
  -> OnlineModule
  -> PetsModule
  -> MayhemModule
  -> ShellModule(...all facades...)
  -> FacmHost.Register(...)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> SettingsModule
       -> ToolsModule
       -> OnlineModule
       -> PetsModule
       -> MayhemModule
       -> ShellModule
            -> MainForm(settings, uiText, tools, online, pets, mayhem)
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
- Mayhem 字段级多源容灾、国内优先、腾讯 Patch、LCU/DataDragon fallback；
- Online Release/manifest 事务；
- 当前用户可见 UI/交互，除非另有独立产品需求；
- 正式 Release 必须单独授权。

---

# 五、下一步

Phase 3 canonical docs/review/CI 收口并合并后，**不要求用户实机测试，直接继续 Phase 4：Cleanup ownership**。

Phase 4 目标：

- 建立 `CleanupModule`，把 `ProcessGuard / ElevationService / SafeCleanupService / GameLocator` 等后端调用从 `CompactMenuForm` 迁入明确 facade；
- UI 确认、FolderBrowserDialog、CleanupReviewForm、状态文字继续留在控制中心表现层；
- 不复制或重写 `SafeCleanupService` 安全算法；
- 保持管理员重启、后台预览/删除、路径白名单与 reparse 防护。

Phase 4 之后：

1. 建立真正的 `LeagueClientModule` foundation；
2. 统一客户端发现、LCU session、HTTP/API 连接所有权；
3. 复用现有 RiotGameDataService 中已验证的 LCU 发现/授权经验，不在 Mayhem 里继续复制新的客户端连接逻辑；
4. 架构重构整体收口后生成单一 Windows 候选包集中实机验收；
5. 候选接受后，再增加账号 / Gameflow / ChampSelect / 战绩等产品能力。

---

# 六、给下一会话的一句话

**FACM 3.2 Phase 1/2 已合并；Phase 3 为 Issue #59 / Draft PR #60 / `refactor/shell-feature-facades-phase3-59`，行为代码 HEAD `10a81d3...` 的 Build #851 + Probe #164 SUCCESS。MainForm 的 ToolRunner/ToolBundleLoader/OnlineService/AnimalPetManager/PetHostBundleLoader/MayhemLookupForm 后端依赖已切到 Tools/Online/Pets/Mayhem module facades，warmup 与 UI 行为保持。不要要求用户中途实机测试；Phase 3 合并后直接做 Cleanup ownership，再做 LeagueClient foundation，整轮完成后给单一最终 Windows 候选包。**