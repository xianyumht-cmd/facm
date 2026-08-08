# FACM 桌面宠物与海斗排行榜

## 桌面宠物

FACM 3.1 的正式高精度桌宠运行层是 **VPet Core + FACM.PetHost**，不是旧 Desktop Homunculus 方案。

运行职责：

- `FACM.exe`：.NET Framework 4.8 / WinForms 主程序，负责控制中心、托盘、设置、故障恢复和产品逻辑；
- `FACM.PetHost.exe`：.NET 8 x64 / WPF 子进程，负责 VPet Core、透明桌宠窗口和动作状态；
- 命名管道：FACM 发送 activate/reset/stop，PetHost 回传 click/right-click/ready/error；
- Windows Job Object：PetHost 被纳入 FACM 的子进程树，FACM 退出或异常终止后子宿主随 Job 关闭；
- `--parent-pid` 守护仍保留，作为 Job Object 无法分配时的第二道兜底。

PetHost 保持独立进程是刻意的稳定性边界。FACM 主程序当前是 net48 WinForms，而 VPet 宿主是 net8 WPF；为了任务管理器里只剩一个 PID 而在正式发布前强行迁移整个主程序，会扩大 CLR、UI、更新器和资源交付的回归面。产品目标是“一棵受 FACM 管理的进程树”，而不是“无条件单 PID”。

### 启动流畅性

PetHost 的内嵌 ZIP 定位/释放、进程启动和命名管道连接不能阻塞 WinForms UI 线程。

当前启动方式：

1. UI 线程只提交桌宠启动请求；
2. 内嵌 PetHost 检查/释放在后台任务执行；
3. PetHost 启动后立刻尝试加入 FACM Job Object；
4. 最长 7 秒的 pipe connect 也在后台执行；
5. 成功后发送当前 pet id；失败则通过 UI SynchronizationContext 恢复默认悬浮球。

桌宠点击打开控制中心时，控制中心除原有 `Deactivate` 逻辑外还使用独立的屏幕外左键检测。这样即使 PetHost 是当前前台进程、Windows 不允许 FACM 立即抢到前台激活，下一次在面板外的左键点击仍会可靠收起面板。

完整 PetHost 交付、资源与验收说明见 `docs/VPET-PETHOST.md`。

## 海斗排行榜

入口：控制中心底部“海斗排行”，或托盘右键菜单“海斗排行榜”。

支持英雄中文名、英文名和常见简称。查询不再把任何一个海外网站当成整个功能的单点依赖，而是按字段分工和降级。

### 数据源优先级

1. **Hexdata（国内）**：当前英雄胜率、排名和前十榜的国内优先来源；
2. **ARAMMayhem.com**：补充选用率、完整当前英雄平衡修正和海外排行备用；
3. **OP.GG**：只补技能加点、核心装备等攻略字段；OP.GG 无法访问时，排行查询本身仍应成功；
4. **腾讯英雄联盟官网**：国服当前 Patch 与海克斯大乱斗“本版本改动”的官方校验；
5. **League Client / Data Dragon / CommunityDragon**：英雄中文名、英雄/技能/装备/强化图标等 Riot 静态元数据；本机客户端可用时优先 LCU。

### Buff / Debuff 语义

腾讯版本公告是**增量变更记录**，不是所有英雄的完整当前状态表。例如某版本只写“治疗 90% → 100%”，并不能证明该英雄过去留下的其它伤害修正也已经不存在。

因此：

- 完整 `当前平衡调整` 优先使用能给出当前完整状态的来源；
- 腾讯公告用于确认国服当前 Patch，并保留同一英雄一条或多条本版本改动；
- 如果完整状态来源的 Patch 与腾讯当前国服 Patch 不一致，FACM 不展示旧完整数值，而显示“状态同步中”或仅显示明确标注的“本版本官方改动（非完整当前状态）”；
- 不通过历史公告片段猜测未出现字段，也不把 100% / 0 等恢复默认值误当成“该英雄所有专属修正已清空”。

### 超时与降级

每个公网来源有独立短预算，OP.GG 不再占满整次查询的等待时间。一次查询的主数据合并预算约 5.5 秒；窗口仍保留总取消预算。

查询结果缓存 10 分钟。缺失的攻略字段可以为空，但核心排行不能因为 OP.GG 单点故障整体报错。

真实第三方健康检查仍由 `FACM Mayhem Source Probe` 独立执行，不重新绑回核心 Windows Build。腾讯公告解析器另有离线 fixture smoke，确保解析逻辑本身可以确定性回归。
