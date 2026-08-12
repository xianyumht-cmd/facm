# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。高清 Flying Runtime、五种飞虫精修、产品化桌宠选择器，以及 Issue #53 / PR #54“二次启动唤醒现有 FACM 控制中心”均已完成 Windows 实机验收并进入 `main`。PR #54 合并提交为 `6147851ee9b28bdb432c17809ac657f46d9ed23f`；Issue #53 已自动关闭为 completed。当前没有新的正式发布动作。

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

1. `AGENTS.md`：仓库强制规则；canonical branch 为 `main`，一任务一短分支，合并后要验证 main 和知识文档；生产发布必须单独授权。
2. 本文件：当前已验证状态、最近完成任务、CI/实机验收和下一步。
3. PR #54 / Issue #53：最近完成的普通单实例激活任务事实来源。
4. `docs/ARCHITECTURE.md` / `docs/DECISIONS.md`：已经记录单实例激活契约和设计选择。
5. `docs/OPERATIONS.md`：已经记录 `--single-instance-activation-test` 和 Windows 二次启动实机验证步骤。
6. `docs/PITFALLS.md`：已经记录 Ensure Open ≠ Toggle、Build #794 catch 顺序，以及历史 Shell/PetHost/Flying Runtime/发布链防回归规则。

---

# 一、当前仓库 / 分支 / 发布状态

## canonical main

- PR #54 合并后的 `main` 提交：`6147851ee9b28bdb432c17809ac657f46d9ed23f`。
- 该 main 已包含：
  - PR #44 高清绿苍蝇基线；
  - PR #46 统一 Flying Runtime；
  - PR #48 蜜蜂/蜻蜓/蝴蝶/飞蛾素材与 Profile 精修；
  - PR #50 发布工作流 `PROJECT_STATE` marker 修复；
  - PR #52 产品化桌宠选择器；
  - PR #54 普通模式二次启动唤醒现有 FACM 控制中心。
- 上述桌宠相关任务和 PR #54 均已由用户 Windows 实机验收。

## 最近完成任务：Issue #53 / PR #54

- 任务分支：`feat/single-instance-activation-0813`
- Issue：#53 `二次启动直接唤醒现有 FACM 控制中心`，已 closed / completed。
- PR：#54 `feat(shell): 二次启动唤醒现有 FACM 控制中心`，已 merged。
- PR 最终 head：`07a39164580c84e1b4c0653e0ce345ab0b9a1706`。
- merge commit：`6147851ee9b28bdb432c17809ac657f46d9ed23f`。
- 本轮真正改变 EXE 行为的修复代码 HEAD：`5a52ff8936a655c023746d3517848530b9bc26eb`。
- 后续提交均为 canonical docs 收口，不改变 Build #797 的 EXE 行为。
- 最终 docs-only HEAD 的 FACM Windows Build #802 和 Mayhem Source Probe #121 均已 fresh-check 为 SUCCESS 后才执行合并。
- 临时任务分支是否删除不在本轮自动收口范围；分支删除属于 destructive ref 操作，需要明确用户意图与 fresh safety check。

## 在线正式版

- `online/version.json` 合并后 fresh-check 仍为：
  - `enabled=true`
  - `version=3.1.3`
  - `minimum_version=3.0.0`
  - `force_update=false`
  - SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`
- GitHub 最新正式 Release 合并后仍为 `v3.1.3`；Issue #53 / PR #54 没有触发新 Release。
- 不要因为本轮实机验收和合并完成就自行发 3.1.4/3.2.0；正式发布仍需要用户另行明确授权。

---

# 二、本轮 Issue #53 / PR #54 的产品目标

旧行为：FACM 已经运行时，用户再次双击 `FACM.exe`，第二进程只弹“FACM 已经在运行”并退出。对现在的常驻 Shell / 桌宠产品，这条恢复路径很差：悬浮入口可能被遮挡、桌宠可能飞出屏幕，用户再次双击程序的自然预期是“把 FACM 叫出来”。

新行为：

- 普通 FACM 已运行时再次双击同一个 EXE：通知现有实例直接打开控制中心；
- 控制中心未开：创建并显示；
- 控制中心已开：只置前/激活，不 Toggle 关闭；
- Flying 桌宠和 VPet 保持原状态，不重启、不停止、不切换；
- 第二实例正常退出，主实例仍唯一；
- `--cleanup` 和所有 smoke/test 模式继续保持原有独立 Mutex 语义。

---

# 三、已完成代码实现

## 1. 单实例激活通道

新增：`src/FACM/Services/SingleInstanceActivation.cs`

设计：

- 普通单实例所有权仍由原有 `Local\\FACM-2C429A53-6710-48BC-A57C-32BEA688B25D` Mutex 负责；**没有把 Mutex 替换成 IPC**。
- 额外建立当前 Windows session 内的命名 `AutoResetEvent`，只承担“请激活现有 FACM”这一种无参数信号。
- 第一实例拥有 listener；后台等待事件，收到后回调 `MainForm.RequestExternalActivation()`。
- 第二实例检测到普通 Mutex 已被占用时，不再第一时间弹“已经运行”；它尝试打开命名事件并 `Set()`。
- 为覆盖“第一实例刚拿到 Mutex、但 activation event 尚未建立”的启动竞态，第二实例做短时间有限重试，总预算约 1.6 秒；仍找不到 listener 才回退旧提示。
- 没有开 TCP/UDP 端口，没有 HTTP、本地 WebSocket、外部服务或新依赖。

## 2. MainForm 激活语义

修改：`src/FACM/MainForm.cs`

关键点：

- 新增 `RequestExternalActivation()`，可从 activation listener 请求 UI 唤醒。
- 如果 WinForms handle/message loop 尚未完全就绪，用 pending flag 记录请求；`Shown` 后消费，避免早期请求丢失。
- 外部激活使用专用“确保控制中心打开”的逻辑：
  - `_menu == null` → 新建并显示控制中心；
  - `_menu != null` → `BringToFront()` / `Activate()`；
  - **绝对不要调用现有 `ToggleMenu()` 作为外部激活入口**，否则菜单已经打开时第二次启动会把它关掉。
- 外部激活不调用 `AnimalPetManager.Stop()`，不修改 `AnimalPetEnabled` / `PetStyleId`，不触碰 Flying Runtime / VPet 生命周期。
- 桌宠激活状态下控制中心继续走现有 cursor/pet-active 定位逻辑。

## 3. Program / Mutex 行为

修改：`src/FACM/Program.cs`

- 普通模式 Mutex 已存在时：优先尝试 `SingleInstanceActivation.TrySignalExistingInstance(...)`；成功即静默退出第二实例。
- 只有激活通知失败时才保留旧“FACM 已经在运行”提示，作为故障兜底。
- `--cleanup` 仍走原来的 `-ElevatedCleanup` Mutex；不参与普通激活。
- 其它 smoke/test 仍各有独立 Mutex，不与普通激活通道混用。
- 新增测试入口：`--single-instance-activation-test`。

## 4. CI smoke

修改：`src/FACM/FACM.csproj`

`ValidateRuntimeSourcesAfterCiBuild` 新增 deterministic 本地 smoke：

- activation listener 不存在时，有限重试必须按预期失败，不能无限挂住；
- listener 存在时，第一次信号触发一次 callback；
- 重复信号再次独立触发一次 callback；
- smoke 不依赖公网，不参与真实用户实例。

---

# 四、已验证结果

## CI

### Build #794 —— 已失败、已修复

- 失败阶段：`Restore and build Release`。
- PetHost publish/self-test 在此之前是成功的。
- 编译错误：`MainForm.cs(124,20) CS0160`。
- 根因：代码先写了 `catch (InvalidOperationException)`，后写 `catch (ObjectDisposedException)`；但 `ObjectDisposedException` 继承 `InvalidOperationException`，后一个 catch 永远不可达。
- 修复：把更具体的 `ObjectDisposedException` 放前面，父类 `InvalidOperationException` 放后面。
- 这是**纯 C# catch 顺序编译错误，不是单实例方案失败，也不需要换 IPC 设计**。

### Build #797 —— 代码候选通过

行为代码 HEAD：`5a52ff8936a655c023746d3517848530b9bc26eb`

结果：FACM Windows Build #797 全流程成功，包括：

- checkout / tools 输入验证；
- CleanupProfile 检查；
- PetHost win-x64 publish + self-test + bundle；
- FACM .NET Framework 4.8 Release build；
- deterministic smoke（包含新的 `--single-instance-activation-test`）；
- `FACM.exe` 验证；
- 签名步骤；
- 下载包生成与 artifact 上传。

Build #797 artifact：

- name：`FACM-Windows-x64-797`
- artifact ID：`9156067026`
- digest：`sha256:801f6dbb04225b281a82fe50d1cf262f825b128d94aaf9208663eca14170fef0`
- 用户实际测试包文件名曾提供为：`FACM-二次启动唤醒-Build797.zip`

同代码阶段 Mayhem Source Probe #116：SUCCESS。

### 后续 docs-only CI

- `2e6377cae2265639aee73ebd22825aaa19403567`：FACM Windows Build #798 SUCCESS；Mayhem Source Probe #117 SUCCESS。
- `395d96297bc9977c92428c68a14edd04e4cc7c23`：FACM Windows Build #801 SUCCESS；Mayhem Source Probe #120 SUCCESS。
- `07a39164580c84e1b4c0653e0ce345ab0b9a1706`：FACM Windows Build #802 SUCCESS；Mayhem Source Probe #121 SUCCESS。
- 这些提交只补 canonical docs，没有改变 Build #797 EXE 行为。

## Windows 实机验收 —— **已通过**

2026-08-13 用户测试 Build #797 后明确回复：**“测试通过”**。

已验收的实际行为：

1. FACM 已运行、控制中心关闭时，再次双击同一 `FACM.exe`：直接打开原实例控制中心，不再弹“FACM 已经在运行”。
2. 控制中心本来已经打开时，再次双击：控制中心保持打开并被唤醒，不会被 Toggle 掉。
3. Flying 桌宠运行时重复启动：桌宠继续运行，没有关闭、重启或切换。
4. VPet 路线同样不得被激活流程破坏；本轮设计没有操作其生命周期。
5. 控制中心关闭后可再次重复用 EXE 唤醒。

因此：**Issue #53 功能、CI、实机验收和仓库合并均已完成。**

---

# 五、失败方案、原因和不要重复的路线

## 已发生失败：Build #794 catch 顺序

不要再次写：

```text
catch (InvalidOperationException)
catch (ObjectDisposedException)
```

`ObjectDisposedException : InvalidOperationException`。具体异常必须排在父类前；或仅捕获父类并明确注释。这个错误会在编译期直接 CS0160。

## 不要把外部激活接到 `ToggleMenu()`

`ToggleMenu()` 的定义就是“开则关、关则开”。外部二次启动的产品语义是 **Ensure Open**，不是 Toggle。若复用 Toggle，用户控制中心已开时再次双击会把它关掉，直接违背验收。

## 不要为这一个无参数信号引入重型 IPC

当前命名 AutoResetEvent 足够：

- 需求只有“激活”这一种无参数信号；
- 无需 NamedPipe payload、socket、HTTP、本地端口或服务发现；
- 不要为了技术统一把 PetHost 的 named-pipe IPC 强行复用到普通进程激活。

只有未来确实需要向现有实例传递文件路径、命令参数等 payload 时，再重新评估 named pipe。

## 不要让第二实例杀掉/重启第一实例

桌宠、控制中心状态、更新状态都属于现有进程。第二次双击只应发激活信号然后退出；不要 kill/relaunch 第一实例来“保证置前”。

## 不要混用 `--cleanup` / smoke test Mutex

普通实例、elevated cleanup、各种 smoke 当前故意使用不同 Mutex。不要把“单实例激活”扩大成所有模式统一一个事件，否则 CI/self-test 或提权清理可能错误唤醒真实 FACM。

## 不要触碰已经验收稳定的桌宠运行层

Issue #53 不需要修改：

- Flying Runtime 轨迹；
- 五种飞虫素材/Profile；
- PetHost/VPet 启动；
- `AnimalPetEnabled` / `PetStyleId`；
- 桌宠自由出屏策略。

外部激活只负责控制中心。

## 不要把本 PR 的验收等同于发布授权

PR #54 合并和“发新正式版”是两件事。用户已通过 Build #797 功能验收，PR #54 也已进入 main，但当前并未要求发布新版本。线上继续保持 3.1.3，直到用户另行明确要求发布/推送更新。

---

# 六、环境状态

## 产品运行边界

- `FACM.exe`：.NET Framework 4.8 / WinForms，产品主进程。
- `FACM.PetHost.exe`：.NET 8 x64 / WPF / VPet Core，仅 VPet 桌面形态需要。
- Flying Runtime：在 FACM 主进程内的轻量 Sprite 桌宠路线。
- 正式交付仍是单个 `FACM.exe`，PetHost bundle 嵌入 EXE 后按 payload SHA-256 解包/复用。

## CI 环境（Build #794 日志已确认）

- GitHub hosted runner：Windows Server 2025 / image `windows-2025-vs2026`
- MSBuild：Visual Studio 2026，setup-msbuild 使用 x86 MSBuild
- FACM：net48 Release
- PetHost：net8.0-windows / `win-x64` / self-contained
- 核心 CI 与 Mayhem live probe 分离；公网 probe 红不能自动等同核心构建失败。

## 当前外部状态

- Build #797 artifact 未过期时可继续复现用户验收候选。
- main 已包含 Issue #53 功能，merge commit 为 `6147851ee9b28bdb432c17809ac657f46d9ed23f`。
- PR #54 已合并，没有触发正式 Release；线上仍为 FACM 3.1.3。

---

# 七、Canonical docs / review 状态

本任务已完成：

- `docs/DECISIONS.md`：记录“普通二次启动采用当前会话命名事件，只做无参数 activation”的持久设计选择。
- `docs/ARCHITECTURE.md`：记录普通 FACM 单实例的 Mutex + activation event 边界，以及和 PetHost IPC 的区别。
- `docs/PROJECT_STATE.md`：记录 Issue #53 / PR #54 的实现、CI、实机验收、合并和当前线上状态。
- `docs/OPERATIONS.md`：已加入二次启动实机验证步骤与 `--single-instance-activation-test` 的 CI 说明。
- `docs/PITFALLS.md`：已加入 Ensure Open ≠ Toggle、不要过度 IPC 化、不要混用 Mutex，以及 Build #794 catch 顺序教训。

Codex 曾在 PR #54 留 P1 review thread，要求补齐 activation canonical docs。该评论基于较早 commit `707ae796...`；相关文档已经补齐并在交接阶段 resolved。合并前 fresh-check 没有发现新的 review 评论。

---

# 八、当前未完成问题

- Issue #53 / PR #54 已完成，不再是进行中任务。
- 当前没有已知需要继续修复的单实例激活 Bug。
- 当前没有必须继续修改的 Flying Runtime / 桌宠选择器阻塞项；这些方案已经实机验收，不要为了“继续迭代”而重新设计。
- 正式 Release 仍未授权，不执行。
- `feat/single-instance-activation-0813` 临时分支未在本轮自动删除；删除分支属于 destructive ref 操作，需明确用户意图和 fresh safety check。

---

# 九、下一步

1. 新任务从最新 `main` 开始，先按 `AGENTS.md` 检查是否已有对应 active Issue/PR/branch。
2. 优先选择新的高收益产品问题或真实缺陷，不要重复修改已经通过实机验收的 Issue #53 单实例方案、Flying Runtime 或桌宠选择器。
3. 若未来需要把文件路径、命令参数等 payload 传给现有 FACM 实例，再单独立项评估 named pipe；不要在无 payload 需求时重构当前 AutoResetEvent 激活契约。
4. 若用户明确要求正式发布，再按 `docs/OPERATIONS.md` 执行发布前 fresh safety check、版本决策、Release 与在线更新验证。

---

# 十、给下一会话的一句话

**Issue #53 / PR #54 已完成：Build #797 用户实机测试通过，最终 docs-only HEAD Build #802 / Probe #121 通过，PR #54 已合并到 main（`6147851...`），Issue #53 已关闭，线上仍是 FACM 3.1.3；不要重写已验收单实例方案，下一轮从最新 main 选择新的高收益任务。**
