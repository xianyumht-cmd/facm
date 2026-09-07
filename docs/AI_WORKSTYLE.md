# FACM AI 协作方式

## 长重构任务的验收节奏

对于 FACM 这类后端/架构重构任务：

- AI 应按既定技术计划连续推进内部阶段，使用代码审查、日志、deterministic smoke、编译与 CI 自行收敛问题；
- 不要在每个内部 Phase / 模块迁移完成后要求用户坐在 Windows 电脑前逐项实机验收；
- 中间阶段可以提交进度和诊断结果，但不把“用户实机测试”作为每一步继续工作的前置条件；
- 等本轮完整重构目标收口、CI 通过并形成单一 Windows 候选包后，再请用户集中做一次最终实机测试；
- 如果中途遇到只有真实 Windows 环境才能判断、且会阻塞继续实现的不可替代问题，再明确说明具体阻塞证据，而不是默认把所有阶段都交给用户测试。

这只改变验收/协作节奏，不改变既定技术范围，也不意味着可以跳过自动测试、日志诊断或 CI。

## 产品界面优先按场景组织

用户明确偏好 FACM 不要继续采用“新增一个功能 = 新增一个独立页面/入口”的方式堆叠功能。

设计新功能或重构旧功能时，AI 默认按下面的优先级判断：

1. 能在用户当前任务上下文中直接完成的操作，优先内联到当前工作台；
2. 与当前功能强相关但不适合同时展开的能力，优先做成“接着做 / 相关功能”式上下文入口，在同一工作台内切换；
3. 设置、次要配置优先使用展开区、抽屉或局部面板，而不是新的顶层窗口；
4. 只有独立确认、危险操作、复杂编辑或生命周期确实需要隔离时才保留独立窗口。

目标是让 FACM 更像一个完整产品，而不是多个小工具被装进同一个 EXE。具体信息架构由 AI 根据代码边界、风险和用户任务流主动规划，不需要用户逐个指定哪些页面合并。

## 云端 ChatGPT / Codex 发布与源码管理

后续由云端 ChatGPT、Codex 或其他 coding agent 接手时，默认目标是维护当前 **FACM 3.5.x lightweight** 产品线：`main` + WinForms + .NET Framework 4.8 + 单 `FACM.exe`。4.x WinUI/bootstrapper/manifest 体系已经退出当前产品线，除非用户明确要求历史研究，否则不要把旧 4.x 分支、PR、tag 或文档片段当成当前发布方案。

每次接手先读取 `AGENTS.md`、`docs/PROJECT_STATE.md`、`docs/ARCHITECTURE.md`、`docs/DECISIONS.md`、`docs/PITFALLS.md`、`docs/OPERATIONS.md`，再核对远端 `main`、当前任务 branch/PR、Release 和 `online/version.json`。仓库与 GitHub 的当前证据优先于旧聊天、旧分支和过期说明。

云端任务可以自行完成源码修改、deterministic smoke、CI、提交、PR 和非破坏性推送。正式发布只使用 `.github/workflows/publish-3.5-lightweight.yml`（**FACM 3.5 Lightweight Release**）及 `release/3.5-request.json` / workflow dispatch，不恢复旧 heavyweight publisher，也不重新引入 4.x migration 字段。

生产发布的可信链固定为：冻结 `main` -> 构建 3.5 lightweight -> 执行 smoke/gates -> Authenticode 签名 -> 发布新的 `v3.5.x` GitHub Release -> 重新下载公开 `FACM.exe` 验证 size/SHA-256/signer -> 最后启用 `online/version.json`。不得复用旧版本号，也不得把 CI artifact、branch 文件或未签名候选冒充正式 Release。

签名边界当前只涉及 3.5 `FACM.exe` 的 Authenticode PFX。仓库或会话拿不到 PFX/密码时，不得要求用户在聊天中粘贴秘密，也不得绕过生产签名门禁；应使用已授权的 GitHub Actions secret / 安全连接器，或者明确停在签名前并交接可审计的候选提交与哈希。

截图或用户反馈与代码事实冲突时，先确认用户正在运行的 FACM 版本和 `online/version.json` 当前指向，再定位对应源码。当前唯一在线更新通道是 3.5 的 `online/version.json`；`online/facm4-version.json`、`.facm/state/active.json`、4.x detached manifest/bootstrapper 等只属于已退出的历史实现，不是当前运行依据。
