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
- 腾讯海克斯大乱斗公告解析的离线 fixture，包括一个英雄多条改动；
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

## 桌宠进程验收

核心 CI 可以验证 PetHost 构建/嵌入/释放/自检，但 Job Object、真实桌面前台激活和 outside-click 仍需要 Windows 实机。

发布候选至少检查：

- 首次启用 VPet 时 FACM 控制中心保持可响应；
- 从 VPet 左键打开控制中心后，下一次点击屏幕空白处能收起；
- 正常退出 FACM 后没有遗留 PetHost；
- 手工结束 PetHost 后 FACM 恢复默认悬浮球；
- 条件允许时强制结束 FACM，确认 PetHost 被 Job Object 或 parent-pid 守护清理。

## 正式发布

正式版本仍使用 `.github/workflows/publish-release.yml`。发布/在线清单事务与第三方海斗 source probe 相互独立；不得因为 probe 临时失败而绕过发布包自身的签名、SHA-256、manifest 和 PetHost 内嵌验证。

用户完成发布候选实机验收前，不触发正式 Release。