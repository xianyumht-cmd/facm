# FACM 当前项目状态

> 2026-08-09：FACM 3.1.0 发布候选已完成 Windows 实机验收，用户确认测试无问题并授权正式发布。

## 当前发布状态

- 当前 `main` 发布基础：`2e47cc400228d1c6a0d9f6e112e11bb371d1c87d`。
- 版本：FACM 3.1.0。
- Build #495：完整 Windows CI 通过，并已完成用户实机验收。
- Issue #28：正式发布 FACM 3.1.0 并开启在线更新。
- `online/version.json` 在正式发布工作流最终成功前必须保持 `enabled: false`。
- 本次发布：`minimum_version=3.0.0`、`force_update=false`、`prerelease=false`。

## 当前动作

合并本次发布请求 PR 后，`.github/workflows/publish-release.yml` 会读取 `release/request.json`，执行签名、PetHost 自检/内嵌、Release 创建与发布、SHA-256 清单以及最终在线更新启用。

任何步骤失败都应遵循事务式发布规则：在 Release 尚未安全公开或最终清单尚未确认前，不提前把在线更新标记为可用。
