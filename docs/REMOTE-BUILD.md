# FACM 远端构建

FACM 使用 GitHub Actions 的 Windows Runner 进行正式构建。

## 构建入口

- 工作流：`FACM Windows Build`
- 触发：提交到 `main`，或更新以 `main` 为目标的 Pull Request
- 产物：`FACM-Windows-x64-<run_number>`

## 队列恢复

工作流并发组按工作流名称和提交 SHA 隔离。旧提交的异常排队任务不会阻塞新提交的构建。

本地一键脚本仅作为备用。