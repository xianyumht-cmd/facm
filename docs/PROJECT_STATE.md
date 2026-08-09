# FACM 当前项目状态

> 2026-08-09：FACM 3.1.0 发布候选已完成 Windows 实机验收，用户确认测试无问题并授权正式发布。

## 当前发布状态

- 当前正式发布代码基础已经通过 PR #29 合并进入 `main`。
- 版本：FACM 3.1.0。
- Build #495：完整 Windows CI 通过，并已完成用户实机验收。
- Build #508：使用新的 `FACM_PFX_BASE64` / `FACM_PFX_PASSWORD` 完成真实自签名预检，Signer 指纹为 `A5E4FC54FBD6B5EC2E1002D3DD2E465D533B3568`。
- Issue #28：第一次自动发布尝试在正式工作流的内嵌资源验证阶段被阻断，已重新打开追踪。
- 第一次尝试没有创建 `v3.1.0` tag/Release，也没有写入发布元数据；`online/version.json` 仍必须保持 `enabled: false`。
- 根因：`publish-release.yml` 在 PowerShell 7 中直接调用 `Assembly.ReflectionOnlyLoadFrom()`；该 API 在 .NET Core / .NET 5+ 不受支持。修复改为复用核心 Build 已验证的 Windows PowerShell 5.1 资源检查方式。
- 第二次审计发布请求：`release/request.json` 的 `request_id=3.1.0-attempt-2`。
- 本次发布参数：`minimum_version=3.0.0`、`force_update=false`、`prerelease=false`。

## 当前动作

修复 PR 通过 CI 后合并，`release/request.json` 的第二次请求会再次触发 `.github/workflows/publish-release.yml`。正式流程仍必须完整执行签名、PetHost 自检/内嵌、SHA-256、disabled manifest、GitHub Release 公开与最终 enabled manifest。

任何步骤失败都继续遵循事务式发布规则：在 Release 尚未安全公开或最终清单尚未确认前，不提前把在线更新标记为可用。
