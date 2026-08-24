# 2026-08-25 海斗紧凑海符卡交接

## 目标

Issue #165：FACM「海斗攻略」对齐 lolapisevers 2026-08-24/25 已上线的紧凑海符榜思路。参考的是信息结构与经过验证的数据投影，不复制服务端运行时。

## 本轮实现

- `MayhemCardRenderer` 从固定 `1260×1540` 大卡改为约 `840px` 宽的动态高密度卡。
- 信息顺序对齐最新海符榜：英雄概览 → 基础 ARAM / Mayhem 两层平衡 → 出装与技能 → 按稀有度分组强化 → 选符方向。
- 强化展示收敛为 TOP 10；选符方向由单强化的真实胜率/选择率推导，并强制三个方向不重复英雄强化，不声称是三符组合胜率。
- 去掉结果卡内额外的「胜率前五英雄」面板，把纵向空间优先留给当前英雄的实战决策信息。
- 新增详细出装模型：两套核心构筑、出门装、鞋子、召唤师技能、技能优先级。
- `MayhemBuildDetailsService` 使用既有 `LeaguePublicDataTransport` 读取同一 OP.GG build 页面，遵守 allowlist、15 分钟缓存、stale fallback；失败时回退到 FACM 原有核心装备/技能数据，不阻断海斗查询。
- 图片资产不再加载 splash / TOP5 头像等非必要装饰；首轮英雄/强化/技能图标有约 1.15 秒总预算，详细出装新增图标约 0.55 秒补充预算，超时直接占位。
- 仍复用 `MayhemImageCache` 的内存/便携磁盘缓存，不新增第二套图片缓存。

## 与 lolapisevers 的边界

参考并复用了以下稳定思路：840px 紧凑布局、TOP10、两套核心出装、出门/鞋子/召唤师/技能、按稀有度两列卡片、单符三方向决策、远程图标总预算。

FACM 不复用服务端 FastAPI/Pillow 缓存实现，也不伪造 lolapisevers 的 `180KB` HTTP 图片响应约束。FACM 是本地 WinForms 位图，重点是减少画布与无效资产、缩短首次绘制等待。

## 验证状态

当前代码位于 `feature/mayhem-compact-parity-165`。下一步必须通过 PR 的 Windows Build / UI Text Contract；Mayhem Source Probe 仍按仓库规则作为外网 advisory。正式在线 Release 不能仅凭 CI 自动发布，需形成单一 Windows 候选后让用户做一次最终实机验收，再按 `docs/OPERATIONS.md` 授权发布。
