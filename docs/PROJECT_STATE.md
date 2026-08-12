# FACM 当前项目状态

> 2026-08-13 交接：FACM 3.1.3 仍是线上正式版。高清 Flying Runtime、五种飞虫精修、产品化桌宠选择器均已进入 `main`。当前唯一进行中的代码任务是 Issue #53 / PR #54“二次启动唤醒现有 FACM 控制中心”；Build #797 已通过 CI，用户已完成 Windows 实机验收并明确反馈“测试通过”。**PR #54 仍 OPEN、未合并、未发布；新对话应从这里继续收口。**

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
2. 本文件：当前任务、CI、实机验收和下一步。
3. PR #54 / Issue #53：当前未完成任务的事实来源。
4. `docs/ARCHITECTURE.md` / `docs/DECISIONS.md`：已经记录本轮单实例激活契约和设计选择。
5. `docs/OPERATIONS.md`：构建、实机验证、发布边界；本交接会补充二次启动验证步骤。
6. `docs/PITFALLS.md`：历史回归与禁止路线，尤其不要破坏已验收的 Shell、PetHost、Flying Runtime 和发布链。

---

# 一、当前仓库 / 分支 / 发布状态

## canonical main

- `main` 当前提交：`285cea0f6eb84a4d0ca116c6af2a857e054ead01`。
- 该 main 已包含：
  - PR #44 高清绿苍蝇基线；
  - PR #46 统一 Flying Runtime；
  - PR #48 蜜蜂/蜻蜓/蝴蝶/飞蛾素材与 Profile 精修；
  - PR #50 发布工作流 `PROJECT_STATE` marker 修复；
  - PR #52 产品化桌宠选择器。
- 上述桌宠相关版本均已由用户 Windows 实机验收。

## 当前任务分支

- 分支：`feat/single-instance-activation-0813`
- Issue：#53 `二次启动直接唤醒现有 FACM 控制中心`
- PR：#54 `feat(shell): 二次启动唤醒现有 FACM 控制中心`
- PR 状态：**OPEN / mergeable / 未合并 / 未发布**。
- 本轮真正改变 EXE 行为的修复代码 HEAD：`5a52ff8936a655c023746d3517848530b9bc26eb`。
- 之后 `2e6377cae2265639aee73ebd22825aaa19403567` 只同步状态文档，不改变 Build #797 的 EXE 行为。
- 本交接文档写入后分支 HEAD 会再次成为 docs-only 新提交；新会话不要死记本文里的分支 HEAD，**先 fresh-read PR #54 当前 head_sha**。

## 在线正式版

- `online/version.json` 当前仍为：
  - `enabled=true`
  - `version=3.1.3`
  - `minimum_version=3.0.0`
  - `force_update=false`
  - SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`
- Issue #53 / PR #54 **没有修改在线 manifest，也没有触发正式 Release**。
- 不要因为本轮实机验收通过就自行发 3.1.4/3.2.0；正式发布仍需要用户另行明确授权。

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

### Build #798 —— docs-only HEAD 也通过

后续状态文档 HEAD：`2e6377cae2265639aee73ebd22825aaa19403567`

- FACM Windows Build #798：SUCCESS
- FACM Mayhem Source Probe #117：SUCCESS
- 该提交不改变 Build #797 EXE 行为，只证明文档同步后的 PR HEAD 仍然是绿色。

## Windows 实机验收 —— **已通过**

2026-08-13 用户测试 Build #797 后明确回复：**“测试通过”**。

已验收的实际行为：

1. FACM 已运行、控制中心关闭时，再次双击同一 `FACM.exe`：直接打开原实例控制中心，不再弹“FACM 已经在运行”。
2. 控制中心本来已经打开时，再次双击：控制中心保持打开并被唤醒，不会被 Toggle 掉。
3. Flying 桌宠运行时重复启动：桌宠继续运行，没有关闭、重启或切换。
4. VPet 路线同样不得被激活流程破坏；本轮设计没有操作其生命周期。
5. 控制中心关闭后可再次重复用 EXE 唤醒。

因此：**功能验收已经完成；当前未完成的是仓库收口，不是功能修复。**

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

PR #54 合并和“发新正式版”是两件事。用户已通过 Build #797 功能验收，但当前并未在本交接请求中要求发布新版本。线上继续保持 3.1.3，直到用户另行明确要求发布/推送更新。

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
- main 尚未包含 Issue #53 功能。
- PR #54 当前无正式 Release 动作。

---

# 七、Canonical docs 状态

本任务分支已修改/计划收口：

- `docs/DECISIONS.md`：记录“普通二次启动采用当前会话命名事件，只做无参数 activation”的持久设计选择。
- `docs/ARCHITECTURE.md`：记录普通 FACM 单实例的 Mutex + activation event 边界，以及和 PetHost IPC 的区别。
- `docs/PROJECT_STATE.md`：本交接文件，记录当前 PR、CI、实机验收、失败与下一步。
- `docs/OPERATIONS.md`：应包含二次启动实机验证步骤与 `--single-instance-activation-test` 的 CI 说明。

Codex 在 PR #54 留过一个 P1 review thread，要求把 activation contract 写入 canonical docs。当时评论基于较早 commit `707ae796...`；之后 ARCHITECTURE / DECISIONS / PROJECT_STATE 已补，交接阶段继续补 OPERATIONS。**新会话合并前要 fresh-check该 review thread 是否仍 unresolved；文档齐全后回复/resolve，不要忽略。**

---

# 八、未完成问题

当前没有已知功能 Bug；剩余都是收口动作：

1. PR #54 仍未合并到 `main`。
2. Issue #53 仍应保持 open，直到 PR merge 后由 `Closes #53` 自动关闭或再 fresh-check。
3. PR review thread 的 canonical docs P1 评论需要确认已满足并 resolve。
4. 本交接文档/OPERATIONS 新提交会触发新的 docs-only CI；合并前应 fresh-check最新 HEAD 的 FACM Windows Build，不要只拿旧 #797 绿色代替最新 PR head 状态。
5. 合并后要 fresh-verify `main` 和 `online/version.json`；不能假设 online manifest 没变化。
6. 正式 Release 未授权，不执行。
7. 临时分支删除属于 destructive ref 操作，需要明确用户意图 + fresh safety check；如果当前 connector 不支持删除，不要声称已删除。

---

# 九、新对话下一步操作（按顺序）

1. 读取 `AGENTS.md`、本文件、PR #54、Issue #53。
2. Fresh-read PR #54：确认仍 OPEN、base=`main`、head=`feat/single-instance-activation-0813`，记录最新 `head_sha`，确认没有他人新改动。
3. Fresh-check最新 HEAD 对应的：
   - `FACM Windows Build`
   - `FACM Mayhem Source Probe`（advisory，但当前最近均成功）
4. Fresh-check PR review threads；确认 canonical docs 评论已被当前文档满足，必要时回复并 resolve。
5. 用户已经对 Build #797 回复“测试通过”；**无需重新要求用户重复同一实机测试，除非最新 HEAD 出现新的代码行为变更。** 若最新 HEAD 只有 docs，沿用 #797 实机验收。
6. 在上述检查通过后，合并 PR #54 到 `main`，使用 expected head SHA 防止 head 移动竞态。
7. 合并后验证：
   - PR #54 = merged；
   - `main` 指向新 merge commit；
   - Issue #53 = closed/completed；
   - `online/version.json` 仍为 3.1.3 / enabled=true / minimum=3.0.0 / force=false；
   - 没有意外 Release/tag。
8. 更新 `docs/PROJECT_STATE.md`：把 Issue #53 / PR #54 从“当前任务”改成“已实机验收并进入 main”，记录 merge SHA；若这一步需要新短文档提交，应遵守仓库当前流程，不制造额外 handoff 分支。
9. 不发布新正式版，除非用户在新对话明确说“发布/推送更新”。
10. #53 收口后，再从最新 main 做下一轮高收益审查；不要为了继续迭代而重新改已经验收稳定的 Flying Runtime。

---

# 十、给下一会话的一句话

**功能已经做完且 Build #797 用户实机测试通过；不要重写单实例方案。当前只需要把最新 docs-only HEAD 的 CI/review fresh-check 完，合并 PR #54，验证 main/Issue/online 状态，然后再选下一个任务。**
