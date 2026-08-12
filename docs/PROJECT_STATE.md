# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。当前产品主线是 **FACM 3.2 后端架构升级**。Issue #55 / PR #56 的 Phase 1 行为代码已完成自动验证：`FacmHost + Module` 基础层、依赖解析、失败模块自清理、反向 rollback、启动/失败耗时诊断和 Shell 低风险样板均已实现；最终代码 HEAD `9b6b1a7da0829b8c18e8cd5a9ca5d0e169e54447` 的 FACM Windows Build #842 与 Mayhem Source Probe #155 均 SUCCESS。按用户明确的长重构验收节奏，中间 Phase 不逐次要求 Windows 实机测试，整轮后端重构收口后再提供一个最终候选包集中验收。当前没有正式发布动作。

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
3. `docs/ARCHITECTURE.md` / `docs/DECISIONS.md`：FACM 3.1 verified architecture 与 3.2 modular-host architecture/迁移边界。
4. `docs/AI_WORKSTYLE.md`：长架构重构期间依赖自动验证连续推进，整轮收口后再让用户做一次集中 Windows 实机验收。
5. `docs/PITFALLS.md`：WinForms、PetHost、Flying Runtime、海斗、单实例和 modular-host 防回归规则。
6. `docs/OPERATIONS.md`：构建、`--facm-host-test`、最终候选与发布边界。

---

# 一、仓库 / 发布状态

## canonical main 基线

Issue #55 开始时的最新 `main`：`639b80c8f92f8f3551598faac8ce3de8ff547b7e`。

该基线已包含并完成 Windows 实机验收：

- PR #44：高清绿苍蝇基线；
- PR #46：统一 Flying Runtime；
- PR #48：蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾精修；
- PR #50：发布工作流 `PROJECT_STATE` marker 修复；
- PR #52：产品化桌宠选择器；
- PR #54：普通模式二次启动唤醒现有 FACM 控制中心。

## 当前架构任务

- Issue：#55 `FACM 3.2 Phase 1：建立 FacmHost 与模块生命周期基础层`
- PR：#56 `refactor(core): establish FACM 3.2 modular host foundation`
- 分支：`feat/facm-host-phase1-55`
- PR：OPEN / Ready for review；Phase 1 行为代码与 canonical docs 已收口，等待最终 docs-only CI/review fresh-check 后合并。
- 最终经 CI 验证的行为代码 HEAD：`9b6b1a7da0829b8c18e8cd5a9ca5d0e169e54447`。
- 行为代码验证：FACM Windows Build #842 SUCCESS；Mayhem Source Probe #155 SUCCESS。

## 线上正式版

仍是 FACM 3.1.3：

- `enabled=true`
- `version=3.1.3`
- `minimum_version=3.0.0`
- `force_update=false`
- SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`

架构 PR、CI artifact 和内部 Phase 合并均不等于正式 Release 授权。不要自行创建 v3.2.0 / v3.1.4 或修改线上 manifest。

---

# 二、FACM 3.2 架构方向

目标不是复制 League Akari 的 Electron/Vue/TypeScript 技术栈，而是吸收其长期桌面应用架构原则：

- 模块有稳定 ID 和明确所有权；
- 依赖显式；
- 生命周期统一；
- 初始化依赖顺序可验证；
- 状态/设置逐步归属各 feature；
- 启动顺序、耗时和失败可从日志直接定位；
- UI 逐步退回表现/命令转发层；
- 复杂度封装在纵向 feature 内，而不是继续向 `Program` / `MainForm` 聚集。

FACM 采用适合现有 `.NET Framework 4.8 / WinForms` 的 lightweight modular monolith，不默认引入大型 DI 容器，不为了“成熟”整体迁移 .NET 8，也不改变 PetHost 独立进程边界。

目标组合根：

```text
Program
├─ process concerns
│  ├─ command line / smoke modes
│  ├─ Mutex / SingleInstanceActivation
│  ├─ WinForms runtime
│  └─ fatal exception boundary
│
└─ FacmHost
   ├─ Settings
   ├─ Tools
   ├─ Online
   ├─ Pets
   ├─ Mayhem
   ├─ Cleanup
   ├─ Shell
   └─ future LeagueClient
```

---

# 三、Phase 1 已实现

## FacmHost

新增 lightweight Host：

- `IFacmModule`：稳定 `Id`、显式 `Dependencies`、`Initialize()`、`Dispose()`；
- `FacmHost`：注册模块、依赖图解析、初始化、失败回滚和反向释放；
- 拒绝重复 module ID 并写日志；
- 拒绝缺失依赖并写 graph validation 日志；
- 检测循环依赖、输出 dependency chain 并写日志；
- 初始化顺序由依赖关系决定，不依赖散落的调用顺序约定；
- Host 关闭时按已初始化顺序反向 Dispose；
- 模块 Initialize 中途失败时，先给失败模块自身一次 Dispose 清理机会，再反向释放此前成功初始化的模块；
- 失败模块 Dispose 再失败时继续记录日志，不覆盖原初始化异常。

宿主 namespace 使用 `FACM.AppHost` / `FACM.AppHost.Modules`。文件目录仍可位于 `src/FACM/Application/`，但**不要改回 `FACM.Application` namespace**；原因见 `docs/PITFALLS.md`。

## 生命周期可观测性

`FacmHostReport` / AppLog 对成功和失败路径都记录：

- planned initialization order；
- 每个实际尝试 module 的初始化耗时；
- succeeded / failed；
- Host 总初始化耗时；
- slowest attempted module；
- 初始化失败模块/异常。

因此即使第一个模块就失败，日志也不会只剩一个堆栈而丢失 Host timing 摘要。

## deterministic smoke

新增：`FACM.exe --facm-host-test`，使用独立 `-FacmHostTest` Mutex，不参与普通实例激活。

覆盖：

- 正常依赖初始化顺序；
- 反向 Dispose；
- missing dependency；
- duplicate ID；
- circular dependency；
- 初始化失败模块自身 Dispose + prior modules rollback；
- 第一个模块就失败时的 timing / slowest report；
- 成功 timing/report 基本完整性。

该 smoke 已加入 `FACM.csproj` 的 deterministic CI build target，并位于其它现有 smokes 之前。

## 第一只低风险样板

`CompactMenuEnhancerModule` 接管原 `Program` 中的 `CompactMenuEnhancer.Install()` 调用；其已有 WinForms 兼容行为未重写。

`ShellModule` 已由 Host 管理并负责创建当前 `MainForm(startCleanup)`。因此正常产品启动现在是：

```text
Program
  -> create ShellModule
  -> FacmHost.Register(...)
  -> FacmHost.Initialize()
       -> CompactMenuEnhancerModule
       -> ShellModule -> MainForm
  -> SingleInstanceActivation listener
  -> Application.Run(shell.MainForm)
  -> Host reverse Dispose
```

`Program` 不再直接执行 `CompactMenuEnhancer.Install()` 或直接 `new MainForm(...)`。

## 后续 facade groundwork

已经建立但尚未在 Phase 1 强行注入 MainForm 的轻量 facade：

- `SettingsModule`
- `ToolsModule`
- `PetsModule`
- `OnlineModule`
- `MayhemModule`

这些文件是下一阶段迁移所有权的承载面。Phase 1 为避免跨阶段硬接，MainForm 内部仍保持原有 static/direct-new 调用，下一 Issue 再按显式依赖迁移。

---

# 四、Phase 1 CI / 失败记录

## Build #821 — FAILED，已定位并修复

PetHost publish/self-test 成功，FACM 在 C# 编译阶段失败。

根因：新建 namespace `FACM.Application` 后，根 namespace `FACM` 内旧源码中的未限定 `Application` 被编译器优先解析为 `FACM.Application`，遮蔽 `System.Windows.Forms.Application`，因此同时出现：

- `Application.Run`
- `Application.OpenForms`
- `Application.MessageLoop`
- `Application.EnableVisualStyles`
- `Application.SetCompatibleTextRenderingDefault`
- `Application.ExecutablePath`

等大量 CS0234。

修复：**只把新宿主 namespace 改为 `FACM.AppHost` / `FACM.AppHost.Modules`，不修改旧业务文件来绕过冲突。**

## Build #832 / Probe #145 — SUCCESS

`12d32c973e50b8aaf696f7b62cb4fe6efc37f3ee` 已验证 namespace 修复、基础 Host、Shell 样板与最初的 `--facm-host-test`。

## Build #842 / Probe #155 — 最终行为代码验证 SUCCESS

HEAD：`9b6b1a7da0829b8c18e8cd5a9ca5d0e169e54447`

已 fresh-check：

- FACM Windows Build #842：`completed / success`；
- FACM Mayhem Source Probe #155：`completed / success`。

该 HEAD 在 #832 基础上进一步补齐：

- duplicate / missing / circular dependency 诊断日志；
- 初始化失败模块自身 Dispose；
- prior modules reverse rollback；
- failed-host timing / slowest report；
- first-module failure deterministic smoke。

Build #842 已成功完成：

- tools 输入验证；
- PetHost publish + self-test + bundle；
- FACM net48 Release build；
- 新 `--facm-host-test`；
- 原有 floating-ball / single-instance / animal-pet / game-locator / Mayhem cancellation / Tencent patch / ARAM balance / embedded PetHost smokes；
- FACM.exe 验证；
- 签名步骤；
- package creation；
- artifact upload。

因此 Phase 1 行为代码当前没有自动验证 blocker。

---

# 五、长重构验收节奏

用户已明确：**不要在每个内部 Phase / 模块迁移后停下来要求 Windows 实机测试。**

执行方式固定为：

1. 按既定技术 Phase 连续重构；
2. 每个内部阶段由代码审查、日志、deterministic smoke、编译和 GitHub Actions 自行收敛；
3. 自动验证成功后继续下一架构阶段；
4. 整轮既定后端架构重构完成并形成单一 Windows 候选包后，再让用户集中实机验收一次；
5. 只有自动验证无法判断、且必须真实 Windows 环境才能解除的 blocker，才提前请求用户测试。

这只改变验收节奏，不降低测试要求，也不改变架构技术计划。

---

# 六、冻结契约

架构重构不得顺手改变以下已验收行为：

## 单实例

- 普通实例 Mutex 所有权保持；
- 当前会话 AutoResetEvent 只传无参数 activation；
- 二次启动语义是 Ensure Open/Activate，不是 Toggle；
- `--cleanup` 和 smoke/test Mutex 保持独立。

## Flying Runtime / Pets

- 五种 Flying Runtime 已验收轨迹、尺寸、素材/Profile、自由出屏保持；
- VPet 继续在独立 `.NET 8 x64 / WPF` PetHost；
- Job Object、parent-pid、bundle SHA、ready/fallback 保持；
- `AnimalPetEnabled` / `PetStyleId` / 旧 settings 继续兼容。

## Mayhem

- 字段级多源容灾保持；
- 国内优先 / 腾讯 Patch / LCU → DataDragon fallback 保持；
- live probe 与 deterministic core CI 保持分离。

## Online / Release

- Release/manifest 事务不变；
- 当前正式交付仍是单 FACM.exe + 内嵌匹配 PetHost bundle；
- 架构 Phase 不触发正式发布。

## UI

- 这一轮目标主要是后端架构和稳定性；没有独立产品需求时，不为了后端重构改变现有用户可见交互。

---

# 七、下一步

Phase 1 最终 docs-only CI/review 收口并合并后，**不要求用户实机测试，直接继续下一架构 Issue**。

优先迁移顺序：

1. Settings ownership：保留 `settings.ini` 和已有 key，把加载/保存归属从 MainForm/static 调用迁入明确模块边界；
2. Shell orchestration：MainForm 改为显式接收 Settings/UiText 等依赖，不再自己 `AppSettings.Load()` / `UiTextCatalog.Load()`；
3. Tools / Online / Pets / Mayhem：用 module facade 替换 MainForm 的 direct static/direct-new 依赖；
4. Cleanup ownership；
5. 建立真正的 LeagueClient module；
6. 之后再加入账号 / Gameflow / ChampSelect / 战绩等产品能力。

迁移过程中保留相同用户行为，自动验证通过就继续，不做每 Phase 用户验收停顿。

---

# 八、给下一会话的一句话

**FACM 3.2 modular-host Phase 1 行为代码已完成自动验证：Issue #55 / PR #56 / `feat/facm-host-phase1-55`，最终代码 HEAD `9b6b1a7...` 的 Build #842 + Probe #155 SUCCESS；Host 负责依赖解析、成功/失败 timing、失败模块自 Dispose、prior-module rollback 和反向释放，Shell/CompactMenuEnhancer 已作为低风险样板接入；Build #821 的 `FACM.Application` 命名冲突已通过 `FACM.AppHost` 修复。完成 docs-only CI/review 后合并 #56，不要求用户中途实机测试，随后直接继续 Settings/Shell/Online/Pets/Mayhem 后端迁移，整轮收口后再给一个最终 Windows 候选包。**