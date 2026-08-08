# FACM 构建与验证运行手册

## 核心 Windows CI

工作流：`.github/workflows/build.yml`（`FACM Windows Build`）。

用途：判断仓库代码本身是否可以安全构建和打包。它必须尽量确定、可重复，不应依赖 OP.GG、ARAMMayhem.com、Data Dragon、CommunityDragon 等实时第三方服务是否临时可用。

核心 CI 目前验证：

- tools 输入文件完整性；
- CleanupProfile 配置状态；
- PetHost win-x64 publish、自检和内嵌 bundle；
- FACM .NET Framework 4.8 Release 编译；
- 悬浮球、旧 Sprite、游戏目录预算/取消、海斗 HTTP 正文取消等本地 deterministic smoke；
- 内嵌 PetHost 释放与启动；
- FACM.exe 资源、版本、签名步骤、下载包与 artifact。

`FACM.csproj` 的 `ValidateRuntimeSourcesAfterCiBuild` 只能放确定性/本地 smoke。不要把实时第三方 source probe 再放回这个 target。

## 海斗第三方数据源探测

工作流：`.github/workflows/mayhem-source-probe.yml`（`FACM Mayhem Source Probe`）。

用途：单独判断真实第三方数据源和解析器当前是否仍兼容。该结果与“FACM 核心代码能否构建”是两件事。

触发方式：

- 每 6 小时自动运行一次；
- Actions 中手动 `workflow_dispatch`；
- Mayhem 相关代码进入 `main` 时立即运行；
- Mayhem 相关 PR 会运行 advisory probe，用于提前发现来源变化，但该 job 设置 `continue-on-error`，不会把第三方临时故障变成核心 PR 构建门禁。

探测内容由 `FACM.exe --mayhem-source-test` 执行，覆盖 OP.GG / ARAMMayhem 排行、Riot Data Dragon / CommunityDragon 元数据和图片链路。

### 失败诊断

每次 probe 无论成功失败都会尝试上传 `mayhem-source-probe-运行编号` artifact，保留 14 天，其中包括：

- `mayhem-source-probe.stdout.txt`；
- `mayhem-source-probe.stderr.txt`；
- FACM probe 运行时生成的 `logs/**`（如果存在）。

处理失败时先判断：

1. 第三方是否返回 WAF / 429 / 5xx / 结构变化；
2. 是否只有单一来源失败而其他来源正常；
3. deterministic 核心 CI 是否仍为绿色。

如果核心 CI 绿色而 live probe 失败，默认先按“外部集成健康问题”排查，不要直接把无关产品代码回滚。

## 正式发布

正式版本仍使用 `.github/workflows/publish-release.yml`。发布/在线清单事务与第三方海斗 source probe 相互独立；不得因为 probe 临时失败而绕过发布包自身的签名、SHA-256、manifest 和 PetHost 内嵌验证。
