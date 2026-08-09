# FACM 技术决策

## 2026-08-09：新桌宠先走独立机器猫 Gate 1，不提前替换 VPet

### 决策

Issue #33 当前角色方向从蜘蛛改为用户已确认外形的机器猫桌宠。第一阶段只在 `prototypes/FACM.MachineCatPrototype/` 做独立 WPF Gate 1 原型，验证 Idle / Walk / Run / Turn / Observe / Raised / Recover / Sleep 的原地动作；在用户真实 Windows 视觉验收前，不接入 `FACM.exe`、不修改 `FACM.PetHost.exe`、不替换 VPet，也不实现自动桌面漫游。

PR #13 旧蜘蛛/Sprite 的失败复盘继续保留为防回归基线：不能因为换成 WPF、增加 deltaTime、多帧或更高清素材就认定路线已经不同。当前 Gate 1 使用连续 Rig 参数驱动动作，不靠固定 FPS Sprite 切帧或窗口随机平移制造生命感。

### 原因

- 旧 Sprite 已经具备 `Stopwatch + deltaTime`、多方向多帧、速度平滑和透明窗口，失败点不是“技术名词不够新”，而是动作与行为没有形成真正角色感；
- 先固定窗口验证原地动作，可以在运动轨迹和桌面漫游掩盖问题之前发现动作本身是否机械；
- 现有 FACM 3.1.1 / PetHost / VPet 已实机稳定，不应为尚未通过视觉验收的新桌宠扩大正式回归面；
- 用户已确认当前机器猫角色外形，本阶段不再把时间投入到继续生成角色图片。

### 后果

- Gate 1 CI 独立于正式 Release，负责 Release build、deterministic 动画自检、真实 WPF window smoke 和 win-x64 self-contained artifact；
- 自动测试通过只代表工程/动画数学/窗口运行链有效，不能替代用户视觉验收；
- 只有 Gate 1 人工通过后才进入 Gate 2：先用调试图形验证 BehaviorController / MotionController 的轨迹，再把真实角色动作与 `actualSpeed` 绑定；
- 在 Gate 2 前继续禁止“随机角度 + 随机速度 + 窗口平移”和碰边直接速度反射的旧路线。

## 2026-08-09：VPet 保持独立子进程，不为单 PID 强迁主程序

### 决策

FACM 3.1 继续采用：

`FACM.exe (net48 WinForms)` → `FACM.PetHost.exe (net8 x64 WPF/VPet)`

PetHost 启动后尝试加入 FACM 创建的 Windows Job Object，并启用 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`。PetHost 自身的 `--parent-pid` 检测继续保留。

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
- 腾讯版本公告只列本版本增量，无法单独回答“这个英雄现在所有 Buff/Debuff 是什么”；
- 用户需要的是当前生效状态，因此完整状态必须带 Patch 语义，并与当前国服版本核对。

### 后果

- 单一海外来源失败时，已有核心字段继续返回；
- 每个来源使用独立短超时预算；
- 完整平衡状态 Patch 落后于腾讯当前 Patch 时，FACM 不展示旧数值；
- 官方公告可以显示明确的本版本改动，但在没有完整状态时必须标注“非完整当前状态”；
- 核心 CI 只验证离线解析 fixture，真实站点健康继续由独立 live probe 监控。
