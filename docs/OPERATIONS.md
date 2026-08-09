# FACM 构建与验证运行手册

## 核心 Windows CI

工作流：`.github/workflows/build.yml`（`FACM Windows Build`）。

用途：判断仓库代码本身是否可以安全构建和打包。它必须尽量确定、可重复，不应依赖 Hexdata、腾讯 LOL 官网、OP.GG、ARAMMayhem.com、Data Dragon、CommunityDragon 等实时公网服务是否临时可用。

核心 CI 目前验证：

- tools 输入文件完整性；
- CleanupProfile 配置状态；
- PetHost win-x64 publish、自检和内嵌 bundle；
- FACM .NET Framework 4.8 Release 编译；
- 悬浮球、旧 Sprite、游戏目录预算/取消、海斗 HTTP 正文取消等本地 deterministic smoke；
- 腾讯海克斯大乱斗公告解析的离线 fixture；
- 内嵌 PetHost 释放与启动；
- FACM.exe 资源、版本、签名步骤、下载包与 artifact。

`FACM.csproj` 的 `ValidateRuntimeSourcesAfterCiBuild` 只能放确定性/本地 smoke。不要把实时第三方 source probe 再放回这个 target。

### 并发与过期构建取消

`FACM Windows Build` 的 concurrency key 按 **事件类型 + PR 编号或 ref** 分组，而不是按 commit SHA 分组：

```text
facm-windows-build-<workflow>-<event>-<PR-or-ref>
```

同一 PR/分支的新提交应取消旧运行；push 与 pull_request 保持不同组；不同 PR/分支保留并行能力。

## 机器猫 Gate 1 Prototype CI

工作流：`.github/workflows/machine-cat-prototype.yml`（`FACM Machine Cat Prototype`）。

用途：只验证 `prototypes/FACM.MachineCatPrototype/` 独立原型，不把 Prototype 变成 FACM 正式构建依赖。

当前验证：

1. .NET 8 WPF Release build；
2. `--self-test`：检查 11 个机器猫动作/视角资源能找到并解码、状态姿态数值合法、Run 节奏高于 Walk、Turn 覆盖多个确认视角、frame-gap clamp 生效；
3. `--window-smoke-test`：真实 Show 透明 WPF 窗口，确认 `Loaded` 并收到至少 3 帧 `CompositionTarget.Rendering` 后自动关闭；
4. win-x64 self-contained publish；
5. 上传独立 Gate 1 artifact。

**注意：该工作流成功不等于 Gate 1 视觉成功。** PR #35 第一版已经实际证明：build/self-test/window-smoke/publish 可以全绿，但角色外形和动作仍可能在真实 Windows 肉眼验收中失败。因此每次 Gate 1 候选都必须把 artifact 给用户实机看，用户未明确通过前不得进入 Gate 2、不得接 FACM/PetHost、不得合并 Prototype PR。

## 海斗第三方数据源探测

工作流：`.github/workflows/mayhem-source-probe.yml`（`FACM Mayhem Source Probe`）。

用途：单独判断真实公网数据源和解析器当前是否仍兼容。该结果与“FACM 核心代码能否构建”是两件事。

触发方式：

- 每 6 小时自动运行一次；
- Actions 中手动 `workflow_dispatch`；
- Mayhem 相关代码进入 `main` 时立即运行；
- Mayhem 相关 PR 会运行 advisory probe，用于提前发现来源变化，但该 job 设置 `continue-on-error`，不会把公网临时故障变成核心 PR 构建门禁。

处理失败时先区分外部站点/WAF/429/5xx/超时与 FACM 自身解析回归；核心 CI 绿色而 live probe 失败时，默认先按外部集成健康问题排查。

## 发布候选实机验收

CI 无法完整证明真实 Windows 前台激活、鼠标、磁盘速度、杀软扫描和用户网络环境。正式发布前集中做一轮 5～10 分钟 smoke。

至少检查：

- **控制中心首帧**：底部 5 个按钮第一次出现就正常；
- **桌宠 outside-click**：从 VPet 左键打开控制中心，下一次点击屏幕空白处应收起；
- **桌宠启动流畅性**：首次启用 VPet 时 FACM 不因 PetHost 解包或 pipe connect 假死；
- **桌宠进程树**：退出 FACM 后没有遗留 PetHost；
- **海斗国内容灾/当前平衡/热缓存**；
- **清理流程流畅性和原有安全语义**。

机器猫 Prototype 在正式接入前另有独立视觉 Gate，不得用核心发布 smoke 替代。

## 正式发布

正式版本使用 `.github/workflows/publish-release.yml`。发布/在线清单事务与第三方海斗 source probe 相互独立；不得因为 probe 临时失败而绕过发布包自身的签名、SHA-256、manifest 和 PetHost 内嵌验证。

发布前必须先有明确的用户实机验收与发布授权。满足后可从 Actions 手动 `workflow_dispatch`，或通过短分支 + PR 修改并合并 `release/request.json` 触发同一个发布工作流。

`release/request.json` 只负责提供发布参数，不能替代发布流程本身的任何安全检查。正式发布仍依次完成：输入校验 → PetHost publish/self-test → FACM Release build → 内嵌资源验证 → Authenticode 签名 → disabled manifest → 确认 main 未移动 → 发布元数据 → GitHub Release → 最后启用在线更新清单。

内嵌资源验证必须在 **Windows PowerShell 5.1 / .NET Framework** 上读取 .NET Framework 4.8 的 `FACM.exe` manifest resources。不要在 PowerShell 7 / .NET 中直接调用 `Assembly.ReflectionOnlyLoadFrom()`。

如果 Release 公开前失败，在线清单保持 disabled；如果 Release 已公开但最终清单启用失败，工作流必须明确报错。同一版本重试必须先核对 tag/Release/manifest 实际状态，不得盲目覆盖。

正式发布成功后同步更新 `docs/PROJECT_STATE.md`。
