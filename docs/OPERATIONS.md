# FACM 构建与验证运行手册

## 核心 Windows CI

工作流：`.github/workflows/build.yml`（`FACM Windows Build`）。

用途：判断仓库代码本身是否可以安全构建和打包。它必须尽量确定、可重复，不应依赖 Hexdata、腾讯 LOL 官网、OP.GG、ARAMMayhem.com、Data Dragon、CommunityDragon 等实时公网服务是否临时可用。

核心 CI 目前验证：

- tools 输入文件完整性；
- CleanupProfile 配置状态；
- PetHost win-x64 publish、自检和内嵌 bundle；
- FACM .NET Framework 4.8 Release 编译；
- FACM modular host：`--facm-host-test` 验证依赖拓扑、缺失/重复/循环依赖、失败回滚、反向释放和初始化报告；
- 悬浮球、旧 Sprite、游戏目录预算/取消、海斗 HTTP 正文取消等本地 deterministic smoke；
- 普通 FACM 单实例激活通道：`--single-instance-activation-test` 验证 listener 不存在时有限失败、listener 存在时首次/重复激活各触发一次回调；
- 腾讯海克斯大乱斗公告解析的离线 fixture，包括一个英雄多条改动和“正文提前提及海斗但不应提前进入英雄段”的边界；
- 内嵌 PetHost 释放与启动；
- FACM.exe 资源、版本、签名步骤、下载包与 artifact。

`FACM.csproj` 的 `ValidateRuntimeSourcesAfterCiBuild` 只能放确定性/本地 smoke。不要把实时第三方 source probe 再放回这个 target。

### 并发与过期构建取消

`FACM Windows Build` 的 concurrency key 按 **事件类型 + PR 编号或 ref** 分组，而不是按 commit SHA 分组：

```text
facm-windows-build-<workflow>-<event>-<PR-or-ref>
```

这样：

- 同一 PR 推送新提交时，旧 `pull_request` build 自动取消，只保留最新 HEAD；
- 同一分支连续 push 时，旧 `push` build 自动取消；
- 不同 PR / 不同分支仍可并行；
- `push` 与 `pull_request` 故意保留为不同组，避免 branch push 把 PR required check 对应的运行取消掉。

不要把 `github.sha` 放回 concurrency group。SHA 每次提交都会变化，会让 `cancel-in-progress: true` 失去实际作用。

## FACM modular host 验证

FACM 3.2 架构阶段新增：

```text
FACM.exe --facm-host-test
```

该模式使用独立 `-FacmHostTest` Mutex，不参与普通实例唤醒，也不触碰真实用户配置/公网。

至少证明：

1. 模块按显式依赖的确定顺序初始化；
2. Host 关闭时按反向依赖顺序 Dispose；
3. 缺失 dependency 确定性失败；
4. duplicate module ID 确定性失败；
5. circular dependency 被检测并留下可读链路；
6. 某模块初始化失败时，已经成功初始化的模块会反向 rollback；
7. report 至少保留初始化顺序、每模块 timing、总耗时和 slowest module。

架构变更后如果出现大量 `Application.Run/OpenForms/MessageLoop/...` “不存在”的 CS0234，先检查是否又引入了 `FACM.Application` 之类会遮蔽 `System.Windows.Forms.Application` 的 namespace。当前稳定宿主 namespace 是 `FACM.AppHost` / `FACM.AppHost.Modules`。

## 普通 FACM 单实例 / 二次启动验证

普通模式使用两层本机会话机制：

- Mutex 继续负责“只能有一个普通 FACM 主实例”；
- 当前 Windows session 内的命名 `AutoResetEvent` 只负责把“请打开现有控制中心”的无参数 activation 信号发送给第一实例。

第二实例发现普通 Mutex 已被占用后，应短时间有限重试找到 activation event；成功 `Set()` 后静默退出。若 listener 在预算内始终不存在，才回退“FACM 已经在运行”提示。不要把这条无参数 activation 扩成 TCP/UDP/HTTP、本地端口或重型 IPC；也不要让普通 activation 复用 `--cleanup` / smoke test 的独立 Mutex。

### deterministic smoke

CI 中执行：

```text
FACM.exe --single-instance-activation-test
```

至少证明：

1. listener 不存在时能在有限预算内失败，不无限等待；
2. listener 存在时第一次 signal 恰好触发一次 callback；
3. 第二次 signal 仍能独立触发一次 callback；
4. smoke 使用测试专用事件名，不唤醒真实 FACM 用户实例。

### Windows 实机验收

CI 无法完全证明 Windows 前台窗口激活语义，因此涉及 `SingleInstanceActivation`、`Program` 普通 Mutex 分支或 `MainForm` 外部激活逻辑的发布候选，至少执行：

1. 启动 FACM，关闭控制中心，仅保留 Shell 或桌宠；再次双击同一个 `FACM.exe`，原实例控制中心应直接打开，**不再弹“FACM 已经在运行”**。
2. 控制中心已经打开时再次双击 EXE：只能置前/激活，不能调用 Toggle 语义把控制中心关掉。
3. Flying 桌宠运行时重复启动：桌宠不能停止、重启、切换，只打开控制中心。
4. VPet 运行时同样不得被 activation 流程改变生命周期。
5. 关闭控制中心后可再次双击 EXE重复唤醒。

外部 activation 的产品契约是 **Ensure Open**，不是 Toggle。不要把 `MainForm.ToggleMenu()` 直接绑定为二次启动回调。

PR #54 的 Build #797 已通过上述 deterministic smoke，用户于 2026-08-13 完成 Windows 实机验证并反馈“测试通过”。如果后续提交只改 canonical docs、没有改变 activation 代码行为，可沿用该实机验收；若 `Program.cs`、`MainForm.cs` 或 `SingleInstanceActivation.cs` 再发生行为改动，则必须在**整轮架构重构最终候选**集中重新做这组实机 smoke；长重构内部 Phase 不需要逐轮要求用户测试，除非真实 Windows 行为成为无法自动解除的 blocker。

## 海斗第三方数据源探测

工作流：`.github/workflows/mayhem-source-probe.yml`（`FACM Mayhem Source Probe`）。

用途：单独判断真实公网数据源和解析器当前是否仍兼容。该结果与“FACM 核心代码能否构建”是两件事。

触发方式：

- 每 6 小时自动运行一次；
- Actions 中手动 `workflow_dispatch`；
- Mayhem 相关代码进入 `main` 时立即运行；
- Mayhem 相关 PR 会运行 advisory probe，用于提前发现来源变化，但该 job 设置 `continue-on-error`，不会把公网临时故障变成核心 PR 构建门禁。

探测内容由 `FACM.exe --mayhem-source-test` 执行。当前主查询本身会触达：

- Hexdata 国内英雄排行；
- ARAMMayhem.com 当前英雄页/排行备用；
- OP.GG 攻略补充；
- 腾讯 LOL 官网当前版本公告校验；
- Riot Data Dragon / CommunityDragon 元数据和图片链路。

注意：产品运行时允许字段级降级，但 live probe 会继续对技能加点、核心装备、排行、图标等完整链路提出更严格要求。因此“某个可选攻略源故障导致 probe 红”并不等于“用户查询会整体失败”。

### 失败诊断

每次 probe 无论成功失败都会尝试上传 `mayhem-source-probe-运行编号` artifact，保留 14 天，其中包括：

- `mayhem-source-probe.stdout.txt`；
- `mayhem-source-probe.stderr.txt`；
- FACM probe 运行时生成的 `logs/**`（如果存在）。

处理失败时先判断：

1. 是 Hexdata / 腾讯 / OP.GG / ARAMMayhem / Riot CDN 中哪一个来源返回 WAF、429、5xx、超时或结构变化；
2. 主查询是否已经通过其它来源正确降级；
3. 腾讯官方 Patch 与完整平衡状态 Patch 是否一致；
4. deterministic 核心 CI 是否仍为绿色。

如果核心 CI 绿色而 live probe 失败，默认先按“外部集成健康问题”排查，不要直接把无关产品代码回滚。

## 发布候选实机验收

CI 无法完整证明真实 Windows 前台激活、鼠标、磁盘速度、杀软扫描和用户网络环境。正式发布前集中做一轮 5～10 分钟 smoke，不需要每个提交都重复下载。

对于 FACM 3.2 这种连续后端/架构重构，内部 Phase 通过编译、deterministic smoke、日志和 Actions 后继续推进，不在每个 Phase 都要求用户实机测试。等既定重构范围整体收口、生成**单一最终 Windows 候选包**后，再执行下面这轮集中验收。

至少检查：

- **控制中心首帧**：连续打开几次控制中心，底部按钮第一次出现就应位置/文字正常，不需要鼠标逐个悬停“修复”画面；
- **二次启动唤醒**：FACM 已运行时再次双击同一 EXE，应打开/置前现有控制中心而不是只提示已运行；控制中心已打开时不能被第二次启动 Toggle 关闭；
- **桌宠 outside-click**：从 VPet 左键打开控制中心，下一次点击屏幕空白处应收起；
- **桌宠启动流畅性**：首次启用 VPet 时 FACM 控制中心仍能重绘/移动，不因 PetHost 解包或 pipe connect 假死；
- **桌宠进程树**：正常退出 FACM 后没有遗留 PetHost；手工结束 PetHost 后 FACM 恢复默认悬浮入口；条件允许时强制结束 FACM，确认 PetHost 被 Job Object 或 parent-pid 守护清理；
- **海斗国内容灾**：查询“阿狸 / 阿克尚 / 亚索”等英雄，核心排行不应依赖 OP.GG；如果能临时阻断 OP.GG，再查一次验证排行仍返回；
- **海斗当前平衡**：结果显示当前完整 Buff/Debuff，或在完整状态源 Patch 落后时明确显示“同步中/本版本官方改动（非完整当前状态）”，不得把旧 Patch 数值伪装成最新；同一英雄多条 Buff/Debuff 要全部保留；
- **海斗热缓存流畅性**：同一英雄连续查询两三次，第二/第三次不应因本地图片缓存读盘/Bitmap 解码出现明显 UI 短卡；
- **清理流程流畅性**：点击清理后，生成预览和正式删除期间应出现响应式进度窗，窗口能正常重绘，不再像程序无响应；原预览内容、保留 DATA、路径白名单和重解析点安全语义不能变化。

## 正式发布

正式版本使用 `.github/workflows/publish-release.yml`。发布/在线清单事务与第三方海斗 source probe 相互独立；不得因为 probe 临时失败而绕过发布包自身的签名、SHA-256、manifest 和 PetHost 内嵌验证。

发布前必须先有明确的用户实机验收与发布授权。满足后有两种等价入口：

1. GitHub Actions 中手动 `workflow_dispatch`，填写版本、最低版本、是否强制更新、是否 prerelease 和更新说明；
2. 通过短分支 + PR 修改并合并 `release/request.json`。该文件进入 `main` 时触发同一个发布工作流，适合 API/AI 客户端没有 workflow-dispatch 写权限的场景。

`release/request.json` 只负责提供发布参数，不能替代发布流程本身的任何安全检查。正式发布仍必须依次完成：输入校验 → PetHost publish/self-test → FACM Release build → 内嵌资源验证 → Authenticode 签名 → 生成 disabled manifest → 确认 main 未移动 → 提交发布元数据 → 创建并公开 GitHub Release → 最后启用在线更新清单。

内嵌资源验证必须在 **Windows PowerShell 5.1 / .NET Framework** 上读取 .NET Framework 4.8 的 `FACM.exe` manifest resources。不要在 GitHub hosted runner 的 `shell: pwsh`（PowerShell 7 / .NET）里直接调用 `Assembly.ReflectionOnlyLoadFrom()`；该 API 在 .NET Core / .NET 5+ 不支持。核心 Build 和正式 Release 应保持同一套资源验证方式。

如果在 Release 公开前失败，在线清单应保持 disabled；如果 Release 已公开但最终清单启用失败，工作流必须明确报错，不能伪称在线更新已经开启。

同一版本在 **Release/tag 尚未创建** 的前提下需要重试时，应先修复并验证失败根因，再修改 `release/request.json` 的审计字段 `request_id`（例如 `3.1.0-attempt-2`）并通过新的短分支 + PR 合并触发。`request_id` 不改变版本或客户端语义，只用于让失败后的重试在 Git 历史中可追踪。若目标 tag/Release 已存在，不得用这种方式盲目重试；必须先核对线上 Release 与 manifest 状态。

发布工作流最终成功后会更新 `docs/PROJECT_STATE.md` 中由 `<!-- FACM_RELEASE_STATE_BEGIN -->` / `<!-- FACM_RELEASE_STATE_END -->` 包围的**发布状态区块**，只写入本次工作流可以从 release 输入、manifest 和运行时直接证明的事实：正式版本、Release tag、在线更新状态、`minimum_version`、`force_update`、发布基础提交、发布元数据提交、FACM.exe SHA-256、`published_at` 和更新说明。该步骤必须保留区块之外的当前开发/验收状态，不得整份重建 `PROJECT_STATE.md`，也不得硬编码历史 Build、Issue/PR 编号或用户验收结论。