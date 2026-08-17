# FACM 3.4 发布后 League UX / 快捷键收口交接（2026-08-17）

## 当前事实

- 生产正式版仍是 FACM 3.4.0 / Release `v3.4.0`，在线更新已启用。
- 本轮来自 3.4.0 在线更新后的真实使用反馈。
- Issue：#126 `3.4 实机反馈：全局快捷键启动生效 + League 推荐中心 UX 收口`
- Branch：`fix/league-startup-ux-126`
- Draft PR：#128 `修复 3.4 League 快捷键启动并升级推荐中心`
- 本轮没有修改 release manifest、版本号或在线更新事务；没有用户明确发布授权前不得发布。

## 用户反馈

1. 一键退出游戏 / 一键关闭大厅只有在先打开 FACM 任意界面后才可靠生效；期望 FACM 启动后无需打开任何功能页即可使用。
2. 英雄联盟相关界面对比 OP.GG / Akari 显得过于简陋；允许做更人性化的信息组织和适量 RGB / 霓虹光效。
3. 装备 / 符文推荐的一键应用至少应有三个可选择项；不能只是三个假按钮。
4. 一键应用下方存在一个空白名称按钮。
5. 更新窗口出现当前 `3.4.0.0`、最新 `3.4.0` 的展示不一致。

## 根因与实现

### 1. 全局快捷键启动生命周期缺口

3.4.0 已经使用独立 STA `RegisterHotKey + NativeWindow`，快捷键并不是在 League 页面打开时才注册，因此不能把实机症状简单归因为“页面焦点”。代码复查还确认：旧 `TryApply()` 会等待 ApplyMessage 被 worker `WndProc` 真正处理，所以“仅仅是 CreateHandle 后过早 `_ready.Set()`”不足以单独解释全部实机现象。

本轮因此不冒充已经证明唯一根因，而是对两个启动边界同时收紧：

- worker 侧增加 READY message：隐藏窗口创建后，READY 必须完整经过该 worker 的 WinForms message loop / `WndProc`，才允许服务构造完成。
- 主 UI 侧增加一次性 rearm：FACM 模块图仍在 `Application.Run(mainForm)` 前初始化；第一次进入**主 UI 线程**的 `Application.Idle` 后，把已经保存的两组快捷键事务性重新注册一次。
- 因 hotkey worker 自己也运行 WinForms message loop，`Application.Idle` 是静态事件，因此 rearm 保存主 UI managed thread ID，并明确忽略 worker 线程的 Idle，避免错误线程回调和自等待。
- rearm 成功/失败均写日志，随后立即解绑 Idle；没有轮询、没有常驻 Timer、没有 low-level keyboard hook。
- 最终是否完全消除用户机器上的“必须先点开 FACM”症状，以本轮唯一一次集中 Windows 实机验收为准。

### 2. League Hub 视觉与信息层次

不重做 Issue #120 / PR #121 已验收的单入口 / 三分区 / 单 LCU session 架构，只增强呈现：

- 暗色电竞 surface / card 层次。
- 静态 cyan → violet → pink accent，选中项与 hover 更明确。
- 不使用动画光效，不新增网络任务或后台线程。

### 3. 一键应用的三个真实可选组合

原 Gate 2 只读取 OP.GG payload 的第一组 `summoner_spells` 与第一组 runes。

现在：

- `PrepareOptionsAsync()` 仍只请求同一份 OP.GG payload 一次。
- 分别保留 OP.GG 召唤师技能列表和符文列表的原始热度顺序，最多取前三档；FACM 按相同档位组合成 #1 / #2 / #3 三个可选组合。
- 这里**不宣称 OP.GG 原始接口提供了三个彼此绑定的“整套 Build”**：符文和技能本来就是独立排行，所以 UI 明确写成「FACM 组合」，并分别展示“符文热度 / 技能热度”的 pick rate 与局数证据。
- 手动 UI 使用“主流组合 / 热门备选 / 第三组合”；如果任一侧源数据不足，缺失位置直接禁用并说明，不制造虚假数据。
- 手动点击应用时重新拉取当前数据并保持所选组合序号，再进入已有确认流程。
- 自动应用继续调用兼容入口 `PrepareAsync()`，固定使用组合 #1；默认关闭。

Gate 2 既有安全语义保持不变：用户确认、Champ Select 写前上下文重验、英雄/队列/阶段漂移 fail closed、Flash 槽位保持、符文页容量保护、写后回读验证。

### 4. 装备推荐

一键应用页新增 starter / boots / core 的只读装备预览，帮助用户在一个页面看到完整推荐决策。

写入 `Recommended` 仍由已验收 Gate 3 独立页面「OP.GG 推荐装备集」处理；没有把磁盘写入权限扩进 Gate 2。

### 5. 空白按钮

`LeagueHubNavigation.ItemSet` 使用 `League.ItemSet.Menu`。该 key 在 `LeagueItemSetUiTextKeys` 有 fallback，但没有 canonical `UiTextCatalog` 默认项；Hub 旧逻辑直接 `_ui.Get(textKey)`，所以缺项时渲染为空。

修复：ItemSet 导航走 `LeagueAdvisorText` fallback，并把用户可见名称统一成「OP.GG 推荐装备集」。

### 6. 版本显示

`OnlineCenterForm` 旧逻辑直接 `Version.ToString()`，因此程序集 `3.4.0.0` 与 manifest `3.4.0` 显示位数不同。

现在只改显示层：revision=0 时显示三段式 `3.4.0`；正数 revision 仍保留四段式。版本比较、下载、校验、替换和 online manifest 都没有改变。

## 自动验证覆盖

- `LeagueEfficiencySmokeTest`：增加 hotkey worker 必须完成 message-dispatch readiness 的契约；主 UI 首次 Idle rearm 仍需最终 Windows 实机确认其真实机器效果。
- `LeagueBuildApplySmokeTest`：
  - 三个组合保持 #1/#2/#3；
  - 真实 spell/rune 候选不能坍缩成重复数据；
  - 分别保留 pick rate / play；
  - 三个组合准备只允许一次 OP.GG payload 请求；
  - preview / prepare 阶段必须 0 LCU write；
  - 原 Flash 槽位、符文容量、上下文漂移、partial failure、取消和 forbidden endpoint 契约继续覆盖；
  - League 推荐 fallback 文案不能为空。
- UI Text Contract：新增用户文案继续经过 fallback / text key 路径。

## 最终一次 Windows 实机验收

等 PR #128 当前 HEAD 的 Windows Build 与 UI Text Contract 全绿后，只做一次集中验证：

1. 完全退出 FACM。
2. 启动候选 FACM，**不要先点击悬浮球、托盘、控制中心或任何 FACM 页面**。
3. 在对应 League 环境直接按已经配置的“一键关闭大厅 / 一键退出游戏”快捷键，确认两者都能响应。
4. 再打开「英雄联盟中心」：确认推荐区没有空白按钮，「OP.GG 推荐装备集」名称正常。
5. 进入英雄选择并让 OP.GG 推荐就绪：确认一键应用页显示三个组合位置；源数据足够时 #1/#2/#3 可分别选择，数据不足时缺失组合明确禁用。
6. 切换 #2/#3 时检查符文 / 召唤师技能预览与各自热度证据会变化；点击应用后的确认框必须显示所选 FACM 组合序号。
7. 检查出门装 / 鞋子 / 核心装备预览可读，并确认实际 Recommended 写入仍从独立装备集入口执行。
8. 打开检查更新窗口，确认 `3.4.0.0 / 3.4.0` 的位数不一致已经消失。
9. 主观检查 League Hub 静态光效与信息密度：需要更有电竞工具感，但不应影响文字可读性或出现明显卡顿/闪烁。

## 收口规则

- 用户实机通过之前：PR #128 保持 Draft，不 merge。
- 用户实机通过之后：先记录验收证据、让 PR ready，再按仓库既定流程合并。
- 只有用户进一步明确授权“发布/推送更新”后，才进入版本号 / release request / online manifest 流程。
