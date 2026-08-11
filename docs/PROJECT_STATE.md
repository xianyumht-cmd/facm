# FACM 当前项目状态

> 正式发布工作流于 2026-08-11 01:46:22Z 更新。

## 当前正式版

- 版本：FACM 3.1.2
- GitHub Release：v3.1.2
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：5a2371d1815a009ae4c5cef85ac446aebdbc99fa
- 发布元数据提交：1f86c3b6a5dd30e1a02f3c7c1019e44d3b0dfe56

## 验证状态

- Build #495 的 Windows 发布候选已完成用户实机验收并确认无问题。
- 正式 Release 继续经过签名、SHA-256、PetHost self-test/内嵌验证与事务式在线清单启用。
- Issue #28 记录本次 3.1.0 正式发布授权与执行。

## 后续

- 新功能或缺陷继续通过 Issue + 短分支 + PR 进入 main。
- 下一正式版本继续使用 `.github/workflows/publish-release.yml`；可从 Actions 手动触发，也可通过合并 `release/request.json` 的审计式发布请求触发。
