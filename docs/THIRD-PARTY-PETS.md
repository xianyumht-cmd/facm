# 第三方桌宠组件与模型

## Desktop Homunculus

- 上游仓库：`not-elm/desktop-homunculus`
- 用途：真实 VRM 桌面角色运行时、透明桌面渲染、角色拖动、指针事件与 Persona API
- 代码授权：MIT OR Apache-2.0
- 文档与上游资源授权：以其仓库声明为准

FACM 不修改或冒充该引擎。首次使用桌宠时，FACM 从上游 GitHub Release 获取官方 Windows x64 MSI，并通过本机 REST API 与 SSE 事件流进行控制。

## Open Source Avatars / 100Avatars R1

- 上游仓库：`ToxSam/open-source-avatars`
- 用途：10 个可再分发的 VRM 桌宠模型及缩略图
- 集合：100Avatars R1
- 授权：CC0 1.0

FACM 保留每个角色的原始名称、模型地址、缩略图地址和授权标识。模型下载后存放于程序目录的 `runtime\pet-models`。

## 数据来源

- OP.GG：Mayhem 英雄版本、梯队、技能、核心出装和强化信息
- ARAMMayhem.com：Mayhem 胜率、选用率、名次、调整信息和总体胜率前十

FACM 在界面中分别标记来源，不把独立排行数据标记成 OP.GG 数据。
