# FACM 当前项目状态

> 2026-08-13：**FACM 3.2 既定后端架构重构 Phase 1～5 已全部完成并合并到 `main`。** 当前不再新增架构 Phase；下一步是从本次纯文档收口合并后的最新 `main` 获取一个 Windows candidate，集中做一次实机验收。线上正式版仍是 FACM 3.1.3，当前没有正式 Release 授权。

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

1. `AGENTS.md`：仓库强制规则；canonical branch=`main`，一任务一短分支，Release/部署/destructive refs 必须单独授权。
2. 本文件：当前已验证状态、候选验收边界和下一步。
3. `docs/ARCHITECTURE.md`：FACM 3.2 最终 modular-host / LeagueClient 依赖图。
4. `docs/DECISIONS.md`：模块化单体、显式依赖、集中实机验收等决策。
5. `docs/PITFALLS.md`：WinForms、PetHost、单实例、Mayhem、Host 防回归规则。
6. `docs/OPERATIONS.md`：核心 CI、smoke、候选与发布边界。
7. `docs/AI_WORKSTYLE.md`：长重构期间依赖自动验证连续推进，整轮后再让用户集中实机验收。

---

# 一、当前 canonical main 基线

Phase 5 PR #65 已合并：

- merge commit：`56a3130febc059aae035124ee51041f037fe0993`
- Issue #63：completed
- main FACM Windows Build #866：SUCCESS
- main FACM Mayhem Source Probe #177：SUCCESS
- `online/version.json`：仍为 3.1.3，没有改线上版本、没有创建 Release/tag

Build #866 已完成 PetHost publish/self-test、net48 Release build、全部 deterministic smoke、FACM.exe 验证、签名步骤、package 与 artifact upload。它证明 Phase 1～5 的**行为代码整合基线**已经在 canonical `main` 上通过。

本 Issue #66 是纯文档 post-merge 收口。它不改变任何行为代码；合并后应使用**该最终 docs merge commit 自己触发的 main Windows Build artifact**作为集中实机验收候选，避免“代码已最终、canonical docs 仍停在上一阶段”的状态不一致。

---

# 二、FACM 3.2 后端架构 Phase 1～5 已完成

## Phase 1 — FacmHost foundation

- Issue #55 / PR #56
- merge：`8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`
- 建立 `IFacmModule / FacmHost`
- duplicate / missing / circular dependency 校验
- dependency-topological initialization
- failing-module Dispose + prior-module rollback
- reverse Dispose
- success/failure timing 与 slowest module 诊断
- `FACM.exe --facm-host-test`
- 稳定 namespace：`FACM.AppHost / FACM.AppHost.Modules`

Build #821 曾证明 `FACM.Application` 会遮蔽 WinForms `Application`；最终已固定为 `FACM.AppHost`。

## Phase 2 — Settings ownership

- Issue #57 / PR #58
- merge：`64182dddeaa8a89f8d70a31e5ca3307dd2098ba7`
- `SettingsModule.Initialize()` 统一加载 `AppSettings / UiTextCatalog`
- Shell 显式依赖 Settings
- MainForm 不再自行 `AppSettings.Load()` / `UiTextCatalog.Load()`
- `settings.ini` key/default/migration/write-back 保持兼容

## Phase 3 — Tools / Online / Pets / Mayhem ownership

- Issue #59 / PR #60
- merge：`974d2bbde73fe78b25052392adc9258c7c20493e`
- Host 注册 Tools / Online / Pets / Mayhem
- MainForm 后端调用改为 `_tools / _online / _pets / _mayhem`
- 保留 Shell 先显示、约 180ms background warmup head-start
- 默认不预热 PetHost
- Pets ready/fallback、Online prompt、Mayhem modal、Tool error UI 保持

## Phase 4 — Cleanup ownership

- Issue #61 / PR #64
- 完整 merge：`58c27db74d5d9e794872615ad1b78569a040f99b`
- Host 注册 `CleanupModule`
- Shell → MainForm → CompactMenuForm 显式传递同一个 Cleanup facade
- CompactMenuForm 不再直接拥有 CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService backend 调用
- UI 继续负责 MessageBox / FolderBrowserDialog / CleanupReviewForm / 状态文字
- SafeCleanupService whitelist / reparse / execute revalidation / `BackgroundOperationDialog` worker 语义保持
- GameLocator 搜索预算、取消、进度窗口保持

### Phase 4 超时恢复教训

聊天 UI 超时期间 GitHub 写操作仍可能在远端成功。PR #62 曾提前合并一个单文件 Cleanup 草稿并误关 #61；恢复时没有 rollback/reset/rebase/force-push，而是 fresh-read 远端状态、重开 #61，再用 PR #64 完整收口。

以后遇到前端“消息发送超时”，**先读取远端真实状态，再决定续作；不能从 UI 超时推断写操作失败。**

## Phase 5 — LeagueClient foundation

- Issue #63 / PR #65
- merge：`56a3130febc059aae035124ee51041f037fe0993`
- 新增共享 `LeagueClientModule / LeagueClientSession / SessionProvider / ILeagueClientApi`
- runtime 保留已实际工作的 process → executable directory → lockfile discovery
- session / authenticated HTTP 与 feature consumer 解耦
- Host 初始化不要求 League Client 已运行；真正 discovery 按需执行
- healthy session / HttpClient 复用；失效后允许 refresh
- local LCU BaseUri 固定 loopback，Basic Auth 为 `riot:<password>`，2 秒 timeout
- password/token 不写日志
- Mayhem 显式成为第一个 consumer：`LeagueClient -> Mayhem -> Shell`
- RiotGameDataService 不再私有拥有 process/lockfile/Basic Auth/local HttpClient
- `lcu:` 图片链同样走共享 LeagueClient API
- LCU 不可用时 CommunityDragon/DataDragon/public fallback 保持
- command-line parser 只作为未来 provider deterministic contract；当前 runtime 不为了模仿 Akari 强切 WMI/native reader

腾讯/国服兼容性按实际客户端机制 + 国服实测判断；League Akari 官网“不支持腾讯服务器”的免责声明不作为技术不可用证据。

---

# 三、FACM 3.2 当前最终依赖图

```text
Program
├─ process concerns
│  ├─ args / smoke modes
│  ├─ ordinary / cleanup / test Mutex
│  ├─ SingleInstanceActivation
│  ├─ WinForms runtime
│  └─ fatal exception boundary
│
└─ FacmHost
   ├─ CompactMenuEnhancerModule
   ├─ SettingsModule
   ├─ ToolsModule
   ├─ OnlineModule
   ├─ PetsModule
   ├─ LeagueClientModule
   ├─ MayhemModule ───── depends on LeagueClient
   ├─ CleanupModule
   └─ ShellModule ────── depends on enhancer/settings/tools/online/pets/mayhem/cleanup
        └─ MainForm
             └─ CompactMenuForm
```

Shell 不直接依赖 LeagueClient；Mayhem 的依赖让 Host 自动保证 `LeagueClient -> Mayhem -> Shell` 顺序。

---

# 四、冻结契约

候选验收前不要为了“再优化一下”继续拆架构或修改以下已验收行为：

- 普通实例 Mutex + current-session AutoResetEvent 二次启动 Ensure Open/Activate
- `--cleanup` 与 smoke/test 独立 Mutex
- 五种 Flying Runtime 已验收轨迹、素材、Profile、自由出屏
- VPet 独立 PetHost、Job Object、parent-pid、bundle SHA、ready/fallback
- `settings.ini` 兼容
- Cleanup whitelist / reparse / execute revalidation / BackgroundOperationDialog / GameLocator 预算语义
- Mayhem 字段级多源容灾、国内优先、腾讯 Patch、public fallback
- Online Release/manifest 事务
- 当前用户可见 UI/交互
- 正式 Release 必须单独授权

---

# 五、当前验收节奏

本轮重构已经执行完用户要求的“**内部连续重构，整轮后一次集中实机测试**”方式：

1. Phase 1～5 中间阶段只依赖 compile + deterministic smoke + AppLog + GitHub Actions + review 收敛；
2. 没有要求用户逐 Phase 下载测试；
3. 现在不再新增架构 Phase；
4. Issue #66 只同步 post-merge canonical docs；
5. #66 合并后等待最终 main Windows Build + Mayhem Probe；
6. 下载该 final main Build artifact；
7. 再让用户集中做一次 Windows 实机验收。

集中验收至少覆盖：

- FACM Shell / 控制中心正常打开、关闭、拖动
- 第二次启动唤醒已有实例
- Flying pet 与 VPet ready/fallback
- 海斗：League Client 未运行时 public fallback
- 海斗：League Client 已运行时本地 LCU 元数据链不报错
- Cleanup：目录识别、预览、安全流程；没有必要为了验收强行删除真实文件
- Online 更新/公告入口
- 退出与 PetHost 子进程回收

候选测试通过 **不等于正式发布**。发布版本号、tag、Release、online manifest 仍等待用户单独授权。

---

# 六、当前任务 / 下一步

当前收口任务：

- Issue #66：`FACM 3.2 后端重构收口：同步 canonical docs 与最终 Windows candidate`
- branch：`docs/backend-refactor-candidate-66`
- 本任务只允许修改 canonical docs

完成标准：

1. PROJECT_STATE / ARCHITECTURE 与 Phase 1～5 已合并事实一致；
2. diff 不包含 C# / workflow / resources；
3. docs-only PR CI 通过并合并；
4. fresh 验证 main、Issue #66、online 3.1.3；
5. final main Build + Probe 成功；
6. 取 final main artifact 给用户做一次集中验收。

**之后如果出现问题，只按真实 Windows defect 新开 Issue/branch 修复；不再为了架构形式继续拆层。**
