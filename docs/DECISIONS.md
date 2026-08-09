# FACM 技术决策

## 2026-08-10：桌宠 Prototype 必须保留已人工确认的角色视觉，程序不再重画角色

### 决策

Issue #33 / PR #35 的机器猫 Gate 1 从第二版开始采用：

- 已经由用户人工确认的角色外观作为视觉基线；
- 从本轮已确认的 Identity / Action Sheet 提取透明动作/视角素材；
- 程序只负责状态、时间、轻微 transform、短时 crossfade、镜像换步、鼠标交互和窗口生命周期；
- 不再为了“少用图片/更程序化”而用 WPF 图元重新设计或重画角色；
- 自动 build/self-test/window-smoke 只能证明工程链路，不替代真实 Windows 视觉验收；
- Gate 1 未被用户明确通过前，不进入 Gate 2 MotionController，不接 FACM/PetHost，不合并 Draft PR。

### 原因

PR #35 第一版程序绘制的 WPF 矢量机器猫曾经同时通过 Release build、deterministic self-test、真实 WPF window smoke 和自包含 publish，但用户实机录屏仍显示：角色与已经认可的圆润 2.5D 机器猫明显不一致，Walk/Run/Turn/Raised/Sleep 也有纸片变形感。

因此“技术上可运行”和“桌宠视觉合格”是两条独立门禁；已确认的视觉身份不能在实现阶段被程序方便性重新定义。

### 后果

- PR #35 第一版保留为失败经验，不作为正式桌宠基础；
- 第二版使用 11 个已确认动作/视角透明素材，运行时一次解码并缓存；
- 原型期可使用 `Assets/*.b64` 作为 GitHub/程序集资源载体，正式接入前再决定常规二进制资源打包方式；
- 当前正式 VPet/PetHost 架构不受此实验影响。

## 2026-08-09：VPet 保持独立子进程，不为单 PID 强迁主程序

### 决策

FACM 3.1 继续采用：

`FACM.exe (net48 WinForms)` → `FACM.PetHost.exe (net8 x64 WPF/VPet)`

PetHost 启动后尝试加入 FACM 创建的 Windows Job Object，并启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`。PetHost 自身的 `--parent-pid` 检测继续保留。

Issue #33 的机器猫 Prototype 当前是隔离实验，不改变这条正式架构决策。只有独立 Prototype 完成视觉、运动和稳定性验收后，才重新讨论正式接入方式。

### 原因

- 两个组件当前使用不同 CLR 与 UI 技术栈；
- 把 WPF/VPet 直接塞回 net48 主进程不可行，把 FACM 整体迁到 .NET 8 又会同时扩大清理提权、ToolBundle、更新器、签名、WinForms UI 的回归面；
- 独立 PetHost 提供崩溃隔离，VPet/WPF 出错时 FACM 仍能恢复默认悬浮球；
- Job Object + parent-pid 已解决“FACM 退出后遗留孤儿 PetHost”的产品问题。

### 后果

任务管理器仍会看到 FACM 主 PID 和 PetHost 子 PID，但它们是一棵受 FACM 管理的产品进程树。若未来整体迁移 FACM 到 .NET 8，可在独立架构版本重新评估单进程托管。

## 2026-08-09：海斗按字段多源容灾，官方公告不充当完整状态库

### 决策

海斗查询的数据职责固定为：

- Hexdata：国内优先的英雄胜率、排名和前十；
- ARAMMayhem.com：完整当前英雄平衡状态、选用率、海外排行备用；
- OP.GG：技能加点/核心装备等攻略字段，可失败；
- 腾讯 LOL 官网：国服 Patch 与本版本海克斯大乱斗官方改动；
- LCU / Data Dragon / CommunityDragon：Riot 静态英雄、技能、装备和图标元数据。

### 原因

- OP.GG 在中国大陆存在访问不稳定，不能作为排行榜整体成功条件；
- 单个第三方页面结构/WAF/限流随时可能变化；
- 腾讯版本公告只列本版本增量，无法单独回答“一个英雄现在所有 Buff/Debuff 是什么”；
- 用户需要的是当前生效状态，因此完整状态必须带 Patch 语义，并与当前国服版本核对。

### 后果

- 单一海外来源失败时，已有核心字段继续返回；
- 每个来源使用独立短超时预算；
- 完整平衡状态 Patch 落后于腾讯当前 Patch 时，FACM 不展示旧数值；
- 官方公告可以显示明确的本版本改动，但在没有完整状态时必须标注“非完整当前状态”；
- 核心 CI 只验证离线解析 fixture，真实站点健康继续由独立 live probe 监控。
