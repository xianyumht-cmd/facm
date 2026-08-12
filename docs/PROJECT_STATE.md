# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。当前主线 **FACM 3.2 后端架构重构** 已完成 Phase 1～4 并进入最后的 Phase 5 LeagueClient foundation 收口。Phase 5 当前行为 HEAD `244837f42c1e688fcd1a77d4d6c0b138b40d4031` 已通过 FACM Windows Build #864 与 Mayhem Source Probe #175。按用户明确要求，中间 Phase 不逐次做 Windows 实机验收；Phase 5 合并并验证 main 后，直接提供这一整轮重构的单一 Windows candidate 集中测试。当前没有正式 Release 授权。

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

1. `AGENTS.md`：仓库规则；canonical branch=`main`，一任务一短分支，Release/部署/destructive refs 必须单独授权。
2. 本文件：当前 Phase、CI、冻结契约和下一步。
3. `docs/ARCHITECTURE.md`：FACM 3.2 modular-host 与 LeagueClient 真实边界。
4. `docs/DECISIONS.md`：3.2 lightweight modular-host 与整轮后集中实机验收决策。
5. `docs/PITFALLS.md`：WinForms、PetHost、单实例、Mayhem、Host 防回归规则。
6. `docs/OPERATIONS.md`：核心 CI、smoke、候选与发布边界。
7. `docs/AI_WORKSTYLE.md`：长重构阶段依赖自动验证连续推进，不逐 Phase 拉用户验收。

---

# 一、已完成架构阶段

## Phase 1 — FacmHost foundation

- Issue #55 / PR #56。
- merge commit：`8bb44cfef3e9ac24c20390fc60fcd307b7dd612a`。
- 建立 `IFacmModule / FacmHost`、依赖图校验、拓扑初始化、失败模块自 Dispose、prior-module rollback、反向释放、成功/失败 timing 与 `--facm-host-test`。
- 稳定 namespace：`FACM.AppHost / FACM.AppHost.Modules`。
- Build #821 曾因 `FACM.Application` 遮蔽 WinForms `Application` 失败，已改为 `FACM.AppHost`。
- 最终行为 Build #842 / Probe #155 SUCCESS；docs-only #843 / #156 SUCCESS。

## Phase 2 — Settings ownership

- Issue #57 / PR #58。
- merge commit：`64182dddeaa8a89f8d70a31e5ca3307dd2098ba7`。
- `SettingsModule.Initialize()` 统一加载 `AppSettings / UiTextCatalog`；MainForm 不再自行 `Load()`。
- Shell 显式依赖 Settings，继续使用原 settings.ini key/default/migration/write-back。
- Build #845 因 FloatingBall smoke 漏改旧 MainForm 构造失败，修正测试调用点后通过。
- 最终行为 #846 / #159 SUCCESS；docs-only #849 / #162 SUCCESS。

## Phase 3 — Tools / Online / Pets / Mayhem facade ownership

- Issue #59 / PR #60。
- merge commit：`974d2bbde73fe78b25052392adc9258c7c20493e`。
- Host 注册 Tools / Online / Pets / Mayhem；MainForm 后端调用切到 `_tools / _online / _pets / _mayhem`。
- 保留 Shell 先显示、约 180ms warmup head-start、默认不预热 PetHost、Pets ready/fallback、Online prompt、Mayhem modal、Tool error UI。
- 最终行为 #851 / #164 SUCCESS；docs-only #853 / #166 SUCCESS。

## Phase 4 — Cleanup ownership

- Issue #61 / PR #64。
- **真正完整 merge commit：`58c27db74d5d9e794872615ad1b78569a040f99b`。**
- Host 注册 `CleanupModule`；Shell→MainForm→CompactMenuForm 显式传递同一个 cleanup facade。
- CompactMenuForm 不再直接拥有 CleanupProfile / ElevationService / ProcessGuard / GameLocator / SafeCleanupService backend 调用。
- MessageBox、FolderBrowserDialog、CleanupReviewForm、状态文字继续属于 UI。
- SafeCleanupService 的 whitelist / reparse 防护 / execute revalidation / `BackgroundOperationDialog` worker 路线未重写；GameLocator 搜索预算、取消、进度窗口未修改。
- 行为 #858 / #169 SUCCESS；docs-only #860 / #171 SUCCESS。

### Phase 4 超时恢复记录

聊天 UI 超时期间远端操作继续执行，PR #62 曾以 `e15877ac...` 单文件 CleanupModule 草稿提前合并，并因 `Closes #61` 误关 Issue #61。该 PR **不是 Phase 4 完成状态**。

恢复时没有 rollback/reset/rebase/force-push：重开 #61，继续原任务分支，用 PR #64 补齐完整迁移并合并。该事件说明：前端发送超时不能直接推断 GitHub 写操作没执行，恢复时必须先 fresh-read 远端状态。

---

# 二、当前 Phase 5 — LeagueClient foundation

## 任务

- Issue #63：`FACM 3.2 Phase 5：LeagueClientModule foundation 与共享 LCU session`。
- 分支：`refactor/league-client-foundation-phase5-63`。
- PR #65：`refactor(league): establish shared LeagueClient session foundation`。
- 当前 PR：OPEN / DRAFT，代码与行为 CI 已收敛，等待 canonical docs-only CI/review 后合并。
- 当前行为 HEAD：`244837f42c1e688fcd1a77d4d6c0b138b40d4031`。
- FACM Windows Build #864：SUCCESS。
- FACM Mayhem Source Probe #175：SUCCESS。

## 为什么现在做 LeagueClientModule

原 `Mayhem/RiotGameDataService.cs` 私有承担了：

```text
LeagueClientUx / LeagueClient process discovery
  -> process.MainModule executable directory
  -> <directory>/lockfile
  -> port/password/protocol
  -> 127.0.0.1 BaseUri
  -> Basic Auth riot:<password>
  -> local certificate tolerance
  -> per-request HttpClient
```

这条实现实际可用，但所有权属于 Mayhem，后续账号 / Gameflow / ChampSelect / 战绩如果继续复制就会重新形成横向耦合。

## Phase 5 当前实现

新增：

```text
FACM.League
├─ LeagueClientSession
├─ LeagueClientSessionParser
├─ ILeagueClientSessionDiscovery
├─ ProcessLockfileLeagueClientSessionDiscovery
├─ LeagueClientSessionProvider
├─ ILeagueClientApi
└─ LeagueClientApiClient

FACM.AppHost.Modules
└─ LeagueClientModule
```

### 当前运行时 discovery

继续保留 FACM 已实际工作的 lockfile 路线，不在架构重构里同时替换连接机制：

```text
Process.GetProcessesByName("LeagueClientUx" / "LeagueClient")
  -> process.MainModule.FileName
  -> executable directory
  -> lockfile
  -> <name>:<pid>:<port>:<password>:<protocol>
  -> protocol://127.0.0.1:<port>/
```

合法 session：port 必须 1..65535、password 非空、protocol 只能 http/https；malformed lockfile fail-closed。

### Session / HTTP ownership

- `LeagueClientModule.Initialize()` **不扫描、不要求 LOL 已启动**；只建立 provider/client 对象。
- 第一个真实 consumer 请求时才 on-demand discovery。
- 健康 session 缓存复用；不再每张图片/每个 bytes 请求重新枚举进程和读 lockfile。
- session 失效后允许重新 discovery；短 retry interval 防止失败瞬间多路请求重复扫进程。
- authenticated HttpClient 按 session 复用；BaseAddress 固定为 loopback，Basic Auth 为 `riot:<password>`，2 秒 timeout。
- 本地 League Client 证书 tolerance 只封装在这个 loopback client 内。
- 401/403、连接失败、非调用方取消导致的 timeout 会使 session 失效；普通 404 不把整个 session 判坏。
- password/token 不写日志；日志只记录 source/protocol/port/可选 platform。
- 未连接返回 null，由 consumer 继续 fallback，不让 Host 启动失败。

### Akari 对照吸收方式

Akari dev 把 LeagueClientUx 进程观察、credential 解析、安装检测做成独立 shard，并能从 command line 解析 `--app-port / --remoting-auth-token / --app-pid / platform / region`。

FACM 吸收的是 **discovery → session → authenticated client → feature consumer 分层**。Phase 5 同时提供 deterministic `TryParseCommandLine` 作为未来 provider 格式契约，但**当前运行时不引入 WMI/native command-line reader，也不抛弃已工作的 lockfile 路线**。

腾讯/国服兼容性继续按实际机制和国服实测判断；Akari 官网“不支持腾讯服务器”的免责声明不作为技术不可用证据。

## Mayhem 成为第一个共享 LCU consumer

当前依赖拓扑：

```text
LeagueClientModule
        ↓
   MayhemModule
        ↓
    ShellModule
```

Shell **不直接依赖 LeagueClient**；它依赖 Mayhem，Host 根据 Mayhem→LeagueClient 的依赖自动保证初始化顺序。

Mayhem 显式传递 `ILeagueClientApi`：

```text
MayhemModule
  -> MayhemLookupForm
       -> RiotGameDataService.EnrichAsync(..., leagueClient, ...)
       -> MayhemCardRenderer.RenderAsync(..., leagueClient, ...)
            -> MayhemImageCache.GetAsync(..., leagueClient, ...)
                 -> RiotGameDataService.DownloadImageAsync(..., leagueClient, ...)
```

因此原 `RiotGameDataService` 中的 Process/lockfile/Basic Auth/local HttpClient ownership 已删除，包括 `lcu:` 图片链也不再隐藏重新发现 session。

LCU 不可用时，原有 CommunityDragon/DataDragon/public fallback 保持。

## Phase 5 自动验证

`LeagueClientSmokeTest` 已并入 `--facm-host-test`，覆盖：

- 合法 lockfile parse；
- malformed / invalid port / unsupported protocol fail-closed；
- loopback BaseUri；
- Basic Auth 形成规则；
- Akari-compatible command-line parser contract；
- healthy session cache；
- invalidate → refresh；
- League 未运行/无 session 时 module 返回 null 且 Host 不失败。

`MayhemSourceSmokeTest` 使用显式 `NoLeagueClientApi`，让 live probe 只验证公网来源/图片 fallback，而不是依赖 GitHub Runner 本地存在 League Client。

### CI 记录

- Build #862：FAILED；产品代码未报错，原因是 `MayhemSourceSmokeTest` 两个旧方法签名没有补 `ILeagueClientApi` 参数。
- 修复：live source smoke 使用 `NoLeagueClientApi`，同时锁定“本地 LCU 缺席仍走 public fallback”的契约。
- Build #863 / Probe #174：SUCCESS。
- 随后清理两处非功能 diff：删除未发布的 `FACM/3.2` local LCU User-Agent，并还原 Renderer 一行无关 named-argument 格式变化。
- **最终行为 HEAD `244837f...`：Build #864 / Probe #175 SUCCESS。**

---

# 三、FACM 3.2 当前完整依赖图

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

Host 仍负责 topology、失败回滚、reverse Dispose 和 timing/report。

---

# 四、冻结契约

整轮架构重构不能顺手改变：

- 单实例 Mutex + current-session AutoResetEvent Ensure Open/Activate；
- `--cleanup` / smoke 独立 Mutex；
- Flying Runtime 已验收轨迹/素材/Profile；
- VPet 独立 PetHost、Job Object、parent-pid、bundle SHA、ready/fallback；
- `settings.ini` 兼容；
- Cleanup whitelist / reparse / revalidation / BackgroundOperationDialog / GameLocator 预算；
- Mayhem 字段级多源容灾、国内优先、腾讯 Patch、public fallback；
- Online Release/manifest 事务；
- 当前用户可见 UI/交互；
- 正式 Release 必须单独授权。

---

# 五、用户验收节奏

用户已明确：不要每个内部 Phase 都下载/实机测试。

执行固定为：

1. Phase 内靠 compile + deterministic smoke + AppLog + Actions + code review 收敛；
2. 自动验证过后继续下一层；
3. **Phase 5 是本轮既定后端架构重构最后一层**；
4. PR #65 docs-only CI/review 通过后合并；
5. fresh 验证 `main` 的完整整合状态；
6. 使用 main 的单一 Windows Build artifact 作为候选包；
7. 此时才让用户集中实机测试一次。

最终集中测试至少覆盖：

- FACM Shell / 控制中心正常开关；
- 第二次启动唤醒现有实例；
- Flying 桌宠与 VPet ready/fallback；
- 海斗查询（LOL 客户端未开时 public fallback）；
- 海斗查询（LOL 客户端已开时本地 LCU 元数据链不报错）；
- Cleanup 目录识别、预览与安全执行流程；
- Online 更新/公告入口；
- 退出和子进程回收。

该候选通过 ≠ 自动发布。正式 3.2.0 / 其它版本号仍需用户单独授权。

---

# 六、下一步

**当前不再新增架构 Phase。**

1. 完成 PR #65 canonical docs-only CI/review；
2. 合并 #65，确认 Issue #63 completed；
3. fresh 验证 main、online/version.json 仍为 3.1.3；
4. 等 main Windows Build + Mayhem Probe 全绿；
5. 获取 main Build artifact，作为这整轮重构的单一 Windows candidate；
6. 再请用户集中实机验收一次。

如果候选出现真实 Windows 回归，按具体 defect 新开 Issue/branch 修复；不再为了架构形式继续拆层。

---

# 七、给下一会话的一句话

**FACM 3.2 后端架构 Phase 1～4 已合并，Phase 5 Issue #63 / PR #65 正在最终收口。当前行为 HEAD `244837f...` 的 Windows Build #864 + Mayhem Probe #175 SUCCESS；新增 LeagueClientModule/shared session/API，运行时保留 process→lockfile discovery，Host 初始化不要求 LOL 已启动，Mayhem 是第一个显式 consumer，RiotGameDataService 已移除私有 LCU discovery/auth ownership，No-LCU live probe 保证公网 fallback 独立。完成 docs-only CI/review 后合并 #65，不再加架构 Phase，直接用 main artifact 给用户做一次整轮集中 Windows 实机验收；未授权 Release。**
