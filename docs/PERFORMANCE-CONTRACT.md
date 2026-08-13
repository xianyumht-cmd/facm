# FACM Performance Contract

## 目标

FACM 后续向 League Dashboard、战绩、Champ Select、当前对局等方向扩展时，性能目标不是单纯追求“加载越快越好”，而是：

> 高配要快，普通机要顺；一旦 League 进入敏感阶段，游戏优先，FACM 第二优先。

FACM 不承诺所有电脑表现完全一致。目标基线是一台本身能够正常运行 League 的电脑：同时运行 FACM 后，不应因为 FACM 的后台工作明显恶化游戏体验。

## 统一预算

新 League 产品功能不得自行定义无上限并发或预取量，应消费共享 `PerformanceBudget`。

当前预算上限：

| 场景 | 网络并发 | 图片解码 | 磁盘 I/O | 后台 CPU | 战绩预取 | 非关键刷新 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Desktop | 4 | 2 | 2 | 2 | 20 | >=15s |
| League Client | 3 | 2 | 2 | 2 | 12 | >=20s |
| Queueing | 2 | 1 | 1 | 1 | 4 | >=30s |
| Champ Select | 2 | 1 | 1 | 1 | 0 | >=45s |
| In Game | 1 | 1 | 1 | 1 | 0 | >=60s |
| FACM hidden/background | 1 | 1 | 1 | 1 | 0 | >=60s |

`In Game` 和 `Champ Select` 优先于 FACM 窗口可见性。最小化 FACM 不能成为放宽游戏中预算的理由。

## 强制规则

后续新增的大量数据、图片和客户端功能遵守以下约束：

- UI thread 不直接执行网络请求、大规模文件扫描、批量图片解码或长时间统计。
- 长任务必须支持取消；离开页面或新请求取代旧请求时，应尽快取消旧工作。
- 网络与外部数据读取必须有 timeout 或总预算，不能无限等待。
- 并发必须有上限；禁止把列表长度直接变成并发请求数。
- 大列表使用分页、渐进加载或虚拟化；1000 场数据不等于创建 1000 个复杂 UI 控件。
- 首屏优先：先显示账号/最近数据，再后台补充历史统计，而不是全部完成后才显示页面。
- 不可见页面停止或显著降低非关键刷新。
- 图片优先经过统一缓存；相同头像、英雄、装备等资源不应被多个页面重复下载和重复高成本解码。
- Queueing、Champ Select、In Game 中禁止非必要历史预取和维护任务。
- In Game 中关闭非必要视觉增强；用户主动请求仍可执行，但必须受最保守预算限制。
- 缓存不能只追求最低内存。稳定的低 CPU/GPU/磁盘负载优先于为了少占几十 MB 而反复重新下载、重新解码和重新计算。

## 已有能力的处理

当前 LeagueClient 已有 CancellationToken 和 2 秒 HTTP timeout；Mayhem 元数据链有总时间预算；Mayhem 图片缓存已有磁盘/解码并发门。这些行为继续保留。

Performance Contract 第一阶段不重写这些稳定功能，而是为后续新功能提供统一上限。只有真实测试证明现有模块需要接入动态预算时，才逐项迁移，避免为了统一而制造回归。

## Gameflow 接入边界

本阶段只建立 `LeagueActivityLevel` 与预算模型，不主动轮询 Gameflow。

下一项 League Dashboard 应成为第一个正式消费者：由 LeagueClient 读取真实 Gameflow，再将客户端/排队/选人/游戏中状态映射到 Performance Budget。状态读取本身也必须轻量、可取消、低频且不阻塞 UI。

## 验证

`FACM.exe --performance-contract-test` 是 deterministic 门禁，并由 CI 自动执行。至少保证：

- 预算从 Desktop 到 In Game 只会更保守，不会反向放宽；
- In Game 网络、图片、磁盘、后台 CPU 并发均为 1；
- In Game 战绩预取为 0；
- In Game 禁止后台预取、维护工作和非必要视觉增强；
- 隐藏窗口不会放宽 In Game 预算。

真实 FPS、CPU、GPU、内存与 1% Low 仍需要后续 Windows 基准机实测，deterministic smoke 不能替代真实游戏性能测试。
