# FACM 3.1 架构

## 进程边界

FACM 3.1 是一棵由 `FACM.exe` 管理的产品进程树，而不是强制单 PID。

```text
FACM.exe  (.NET Framework 4.8 / WinForms)
├─ 控制中心 / 悬浮球 / 托盘
├─ 清理与内置工具
├─ 海斗查询与在线更新
└─ FACM.PetHost.exe  (.NET 8 x64 / WPF / VPet Core)
```

`FACM.exe` 是产品主进程和生命周期拥有者。`FACM.PetHost.exe` 仅负责高精度桌宠运行层，通过命名管道与主进程通信。

PetHost 启动后尝试加入 FACM 创建的 Windows Job Object，并使用 `KILL_ON_JOB_CLOSE`；PetHost 自身仍保留 `--parent-pid` 守护作为兼容兜底。这样既保留 WPF/VPet 崩溃隔离，又避免 FACM 结束后留下孤儿宿主。

PetHost 的内嵌包定位/释放、进程创建、最长 7 秒的 named-pipe connect 和停止等待均不得占用 WinForms UI 线程。

## 控制中心

`MainForm` 是悬浮球和应用级入口拥有者；`CompactMenuForm` 是轻量弹出控制中心。

当前底部桌宠/海斗两个入口仍由 `CompactMenuEnhancer` 兼容注入。为了避免旧版 `Application.Idle` 后置重排造成首帧残影，兼容布局必须在控制中心第一条 `WM_PAINT` 真正分发前完成；Idle 只能作为异常情况下的兜底，不再承担正常布局职责。

控制中心关闭使用两条信号：

- 正常前台激活场景：`Deactivate`；
- PetHost 等跨进程点击打开场景：物理左键 outside-click watcher。

outside-click watcher 必须先等待打开面板的那次按键释放，再监测下一次按下，且内部 modal 对话流程由 `_dialogOpen` 抑制误关。

## 清理执行边界

清理的安全规则仍全部归 `SafeCleanupService` 所有，包括编译期白名单、重解析点阻止、预览统计和执行前再次校验。

从 WinForms 控制中心调用时，两个可能持续较久的阶段不再占用 UI 线程：

```text
CompactMenuForm
   │
   ├─ SafeCleanupService.CreatePlan
   │      └─ BackgroundOperationDialog → worker thread
   │             └─ 递归统计目标 / 大小 / reparse 检查
   │
   └─ SafeCleanupService.Execute
          └─ BackgroundOperationDialog → worker thread
                 └─ 二次安全校验 / 文件删除
```

后台化只改变执行线程，不改变允许删除的路径集合和校验顺序。正式删除阶段不给任意中断按钮，避免把“中止”误解为事务回滚；单目标失败仍按原有语义记录后继续。

非 UI/测试调用没有 WinForms message loop 时直接执行同步 core，因此服务逻辑仍可独立验证。

## 海斗查询数据流

海斗查询采用字段级多源合并，而不是“一个网站成功才算整次查询成功”。

```text
用户英雄名/别名
        │
        ├─ ChampionAliases（本地快速解析）
        │
        ├─ Hexdata（国内优先）──────────────┐
        │     排名 / 胜率 / 前十             │
        ├─ ARAMMayhem.com ─────────────────┤
        │     完整当前平衡 / 选用率 / 备用排行│
        ├─ OP.GG ──────────────────────────┤
        │     技能加点 / 核心装备等可选攻略  │
        ├─ lol.qq.com ─────────────────────┤
        │     国服当前 Patch / 本版本增量改动 │
        └─ LCU → DataDragon/CommunityDragon │
              英雄、技能、装备、强化图标      │
                                            ▼
                                  MayhemChampionResult
                                            │
                                  MayhemCardRenderer
```

来源按字段短预算并行读取。OP.GG 不再是排行整体成功条件。

腾讯公告是“本版本改了什么”的增量日志，不是完整当前状态库。只有完整平衡来源的 Patch 与腾讯当前国服 Patch 一致时，FACM 才把其 Buff/Debuff 当成当前完整状态；版本不一致时拒绝静默展示旧值，只允许显示明确标注为非完整状态的本版本官方增量。

## 海斗图片与渲染

Riot 元数据优先使用本机 League Client LCU，无法使用时回退 Data Dragon / CommunityDragon。

`MayhemImageCache` 有 10 分钟内存缓存和 6 小时便携磁盘缓存。磁盘缓存读取和 Bitmap 解码必须在后台执行，并限制为最多 4 路并发，避免“缓存越热越卡 UI”或几十张图同时争抢 CPU。

最终卡片渲染在图片异步准备完成后执行；网络正文统一受 `CancelableHttpContentReader` 的取消和大小上限保护。

## 构建与外部健康检查

`FACM Windows Build` 只承担 deterministic 核心构建与本地 smoke。真实 Hexdata、腾讯、OP.GG、ARAMMayhem、Riot CDN 的可用性由独立 `FACM Mayhem Source Probe` 检查。

公网 probe 失败不能自动证明核心程序构建失败；但正式发布候选仍需要确认主查询在国内优先源和字段级降级下可以完成用户实际查询。

## 发布边界

正式发布包继续只交付一个 `FACM.exe`。匹配版本的 self-contained PetHost publish 目录在构建时压成 `FACM.Resources.PetHost.zip` 嵌入主 EXE，运行时按 FACM MVID 释放到 `runtime\pethost-host\<FACM-MVID>`。

正式 Release 与在线更新事务由独立发布工作流负责；发布前实机验收与普通 Actions 测试 artifact 分开。