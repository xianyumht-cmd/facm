# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。Issue #53 / PR #54“二次启动唤醒现有 FACM 控制中心”已经完成 CI、Windows 实机验收并合并。当前主线已转入 **FACM 3.2 架构基础阶段**：Issue #55 `FACM 3.2 Phase 1：建立 FacmHost 与模块生命周期基础层` 已建立，任务分支为 `feat/facm-host-phase1-55`。本阶段只建立应用宿主/模块边界，不发布新正式版，不重写已验收功能。

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

1. `AGENTS.md`：仓库强制规则；canonical branch 为 `main`，一任务一短分支，发布/部署和 destructive refs 必须单独明确授权。
2. 本文件：当前已验证状态、正在进行的 Issue/branch、冻结契约和下一步。
3. Issue #55：当前 FACM 3.2 Phase 1 的范围、明确不做和验收条件。
4. `docs/ARCHITECTURE.md`：3.1 已验证架构 + 3.2 目标架构；注意区分“当前事实”和“规划目标”。
5. `docs/DECISIONS.md`：记录为什么采用 lightweight modular host、为什么不做 Electron/.NET 8/大型 DI 的大爆炸迁移。
6. `docs/OPERATIONS.md` / `docs/PITFALLS.md`：现有 CI、发布、单实例、PetHost、Flying Runtime、海斗等防回归约束。

---

# 一、当前仓库 / 分支 / 发布状态

## canonical main

当前任务开始时最新 `main`：`639b80c8f92f8f3551598faac8ce3de8ff547b7e`。

该 main 已包含并验收：

- PR #44：高清绿苍蝇基线；
- PR #46：统一 Flying Runtime；
- PR #48：蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾素材与 Flying Profile 精修；
- PR #50：发布工作流 `PROJECT_STATE` marker 修复；
- PR #52：产品化桌宠选择器；
- PR #54：普通模式二次启动唤醒现有 FACM 控制中心。

Issue #53 / PR #54 的 merge commit：`6147851ee9b28bdb432c17809ac657f46d9ed23f`。随后 `639b80c...` 只完成 post-merge 项目状态收口。

## 当前进行中：Issue #55

- Issue：#55 `FACM 3.2 Phase 1：建立 FacmHost 与模块生命周期基础层`
- 状态：open
- 任务分支：`feat/facm-host-phase1-55`
- 当前阶段：**架构规格已开始固化，尚未进入业务迁移/产品功能开发**。
- 目标：在 .NET Framework 4.8 / WinForms 现有主程序上建立 lightweight `FacmHost + Module` 应用宿主层。
- Phase 1 只要求：模块注册、显式依赖、缺失/重复/循环依赖检测、确定性 init/dispose 顺序、启动耗时观测，以及一个低风险样板模块。
- Phase 1 不要求一次迁移 Settings / Online / Pets / Mayhem / LeagueClient。

## 在线正式版

当前线上仍保持：

- `enabled=true`
- `version=3.1.3`
- `minimum_version=3.0.0`
- `force_update=false`
- SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`

**Issue #55、架构分支、CI 测试包都不等于发布授权。** 不自动创建 v3.2.0 / v3.1.4 Release，不改线上 manifest，除非用户后续明确要求正式发布。

---

# 二、为什么当前主线转向 FACM 3.2 架构基础

FACM 当前不是“功能代码不可用”，相反已有多条经过 CI 和 Windows 实机验收的稳定链路。新的问题是：随着产品从小型工具成长为常驻 Shell / 桌宠 / 海斗 / 在线更新平台，`Program` / `MainForm` 正逐步承担过多应用级 orchestration，部分子系统通过 static manager、直接 `new` 和隐式全局状态连接。

如果后续继续加入 League Client、账号、Gameflow、ChampSelect、战绩、自动化等长期功能，而不先建立所有权边界，新增能力会继续挂到主窗体和入口代码上，回归范围越来越大。

本阶段参考 League Akari 等成熟长期桌面应用的**架构原则**，而不是照搬技术栈：

- 模块拥有自己的职责和状态；
- 依赖显式；
- 生命周期统一；
- 复杂度按 feature 纵向封装；
- 启动顺序和耗时可观测；
- UI 逐步退回表现层角色。

FACM 继续保留自己的 WinForms、PetHost、发布链、国服/第三方数据策略和轻量便携定位。

---

# 三、FACM 3.2 Phase 1 目标

目标组合根：

```text
Program
│
├─ 进程级职责
│  ├─ command-line / smoke modes
│  ├─ Mutex / SingleInstanceActivation
│  ├─ WinForms initialization
│  └─ fatal exception boundary
│
└─ FacmHost
   ├─ Infrastructure / Platform
   └─ Modules
      ├─ Shell
      ├─ Cleanup
      ├─ Online
      ├─ Pets
      ├─ Mayhem
      ├─ Tools
      └─ future LeagueClient
```

Phase 1 的原则：

1. `Program` 仍负责真正的进程入口语义，不把 Mutex / `--cleanup` / smoke mode 硬塞进模块框架。
2. `FacmHost` 负责正常产品模式的应用级模块注册、依赖解析、初始化和释放。
3. 模块机制保持小型、透明、可调试；当前没有证据需要 Autofac / Unity 等大型容器。
4. 首个样板模块必须低风险，不选 Flying Runtime、PetHost、海斗多源查询或在线更新事务。
5. 先通过 adapter/facade 建边界，不为了“架构漂亮”重写已经稳定的内部实现。
6. Phase 1 完成后，再一项一项迁移 Shell、Settings、Online、Pets、Mayhem，最后建立真正的 LeagueClient module。

---

# 四、必须冻结的已验收契约

架构阶段默认不得改变以下行为；如确有新用户价值，需要另立 Issue，而不是顺手重构：

## 单实例

- 普通实例所有权仍由原 Mutex 负责；
- 第二次普通启动仍通过当前会话命名 AutoResetEvent 发送无参数 activation；
- 控制中心未开则打开，已开则 BringToFront/Activate，不能 Toggle；
- `--cleanup` 和各 smoke/test 继续使用独立 Mutex；
- 不因为“统一架构”改成 TCP/HTTP/重型 IPC。

## Flying Runtime / 桌宠

- 已验收的五种 Flying Runtime 轨迹、尺寸、素材/Profile、自由出屏行为保持；
- VPet 继续由独立 `.NET 8 x64 / WPF` `FACM.PetHost.exe` 承载；
- PetHost Job Object、parent-pid、bundle SHA 隔离和 ready 前 Shell 不隐藏等契约保持；
- `AnimalPetEnabled` / `PetStyleId` 和旧 `settings.ini` 继续兼容。

## 海斗

- 字段级多源容灾保持；
- 国内优先源/腾讯 Patch 语义/LCU → DataDragon fallback 保持；
- 公网 live probe 与 deterministic core CI 继续分离。

## Online / Release

- 当前正式交付仍是单个 `FACM.exe`，匹配 PetHost bundle 内嵌；
- Release 与在线 manifest 继续走已验证事务；
- 架构测试不自动更新线上版本。

---

# 五、Issue #55 验收重点

Phase 1 必须至少证明：

1. 正常产品启动通过 `FacmHost` 进入模块生命周期。
2. 重复模块、缺失依赖和循环依赖能确定性失败并留下明确诊断。
3. 初始化顺序由依赖决定，关闭时按反向顺序停止/释放。
4. 日志包含初始化顺序、每模块耗时、总耗时、最慢模块和失败模块。
5. 至少一个低风险样板模块接入 Host，用户可见行为保持当前 main。
6. 现有单实例、桌宠、海斗、GameLocator、PetHost 等 smoke 不回归。
7. Windows 测试包实机确认 Shell、控制中心、二次启动、桌宠、海斗、更新入口没有因 Host 引入改变。
8. canonical docs 与实际代码一致。

---

# 六、当前不做

- 不迁移 Electron / Vue；
- 不把 FACM 主程序整体迁到 .NET 8；
- 不重写 Flying Runtime；
- 不重构 VPet/PetHost 进程模型；
- 不改变 Issue #53 单实例激活协议；
- 不改变 `settings.ini` 格式；
- 不重做控制中心 UI；
- 不在 Phase 1 新增账号 / 战绩 / Gameflow / ChampSelect 产品功能；
- 不把历史机器猫/Q 版蜘蛛探索线当成当前主线；
- 不发布正式 3.2.0。

---

# 七、后续迁移顺序

Issue #55 通过 CI + Windows 实机验收后，按小 Issue 继续：

1. Shell / Application lifecycle orchestration；
2. Settings；
3. Online；
4. Pets facade（先包住现有 `AnimalPetManager`，不动 Flying Runtime）；
5. Mayhem；
6. LeagueClient module；
7. 账号 / Gameflow / ChampSelect / 战绩；
8. 在稳定模块架构上重新规划更完整的 FACM 控制中心 UI。

每次只迁移一个所有权边界，避免大爆炸重写。

---

# 八、当前下一步

1. 在 `feat/facm-host-phase1-55` 上完成 3.2 target architecture / decision 文档收口。
2. 为 Issue #55 创建/维护单一 PR，不创建 `v2/final/test` 等平行分支。
3. 文档规格稳定后开始 Phase 1 代码：先实现最小 `FacmHost + Module` 契约和 deterministic tests，再选低风险样板模块接入。
4. Phase 1 CI 全绿后提供 Windows 测试包，先验收“行为无变化 + Host 可观测性”；实机通过前不继续大规模迁移。
5. 正式 Release 继续等待单独授权。

---

# 九、给下一会话的一句话

**当前主线是 Issue #55 / `feat/facm-host-phase1-55`：FACM 3.2 先建立 lightweight `FacmHost + Module` 架构基础，吸收成熟软件的显式依赖/生命周期/feature ownership 思路，但不换 Electron/.NET 8、不动已验收 Flying Runtime/PetHost/单实例/海斗/发布契约；Phase 1 先做宿主、依赖解析、启动耗时观测和一个低风险样板模块，线上仍是 FACM 3.1.3。**