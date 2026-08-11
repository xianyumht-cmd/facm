# FACM 当前项目状态

> 2026-08-12：FACM 3.1.2 已正式发布并启用在线更新；PR #40 正在实机验收 PetHost 加载、FACM Shell 与主题/桌面形态整合。

## 当前正式版

- 版本：FACM 3.1.2。
- GitHub Release：v3.1.2。
- 在线更新：已启用。
- `minimum_version=3.0.0`。
- `force_update=false`。
- 3.1.2 发布基础 main：`5a2371d1815a009ae4c5cef85ac446aebdbc99fa`。
- 3.1.2 发布元数据提交：`1f86c3b6a5dd30e1a02f3c7c1019e44d3b0dfe56`。
- 3.1.2 在线更新启用提交：`86935aad1b20a6c54203caa5a56202e3ccccfd33`。
- 在线清单 SHA-256：`A9843A9FA52A935874B268615C8BA929C01A9D209DBC85879013CB142AA8F8DE`。

## 3.1.2 已验证内容

- PR #36 已将英雄**完整基础 ARAM Buff/Debuff**接入海斗查询，并与 Mayhem 专属状态分层展示；两个层级不做没有来源依据的数值相加。
- PR #37 已同步真实 OP.GG 页面兼容修复：支持 `Patch` / `Ver` / `Version` / 中文版本号，支持 `+ 2.5%` 这类符号与数字分隔形态，并保留未知带符号修正 fail-closed 与版本不一致时隐藏旧完整值的保护。
- PR #37 最终 HEAD 的 FACM Windows Build #695 成功，FACM Mayhem Source Probe #39 成功；亚索、库奇、萨勒芬妮等代表性平衡形态均有确定性回归覆盖。
- 用户已完成实际查询验收，确认结果基本无问题，并明确授权正式发布和在线更新推送。
- 发布请求 PR #38 的 FACM Windows Build #697 成功后合并到 main。
- 正式发布继续经过 PetHost publish/self-test、FACM Release build、内嵌资源验证、Authenticode 签名、SHA-256、disabled manifest、公开 GitHub Release、最终 enabled manifest 的事务式链路；在线清单现已指向 v3.1.2。

## PR #40：PetHost 加载、FACM Shell 与主题整合

### PetHost 加载卡

- 启动卡属于 `FACM.PetHost/PetHostWindow.cs`，不是 VPet 配置项。
- VPet 资源准备的 `x/1995` 阶段显示“正在编译着色器…” + determinate 真实进度条 + 百分比/完成数。
- “正在核对 VPet 官方动作清单”显示“加载中请稍等....”。
- 第一次编译结束后的 `LoadALL` 阶段也显示“加载中请稍等....”，但真实机器上 `readyCount` 可能长期保持 0，因此已改成 **indeterminate 不定进度条**；不再显示误导性的 `0% / 0/N`。
- 加载卡不显示“动画来源：VPet / VUP-Simulator（非商用授权）”；授权/来源信息继续保留在随包文档与 NOTICE 中。

### PetHost 启动性能

- 第一轮 Build #703 虽然 CI 成功，但用户 Windows 实机发现加载卡比上一版约晚 30 秒出现，因此未合并。
- 根因是内嵌 PetHost 为 self-contained runtime，旧启动链在进程启动前释放/检查数百个文件，缓存命中还递归扫描完整目录；慢盘/杀软会直接推迟 WPF 窗口出现。
- 缓存身份现由 FACM MVID 改为内嵌 `PetHost.zip` SHA-256；首次释放完整统计，后续命中只检查完成标记与关键启动文件。
- 用户后续视频已确认加载窗口出现时间明显恢复。

### FACM Shell 与默认启动

- 新产品决策：FACM 默认不再采用“桌面无入口、仅托盘常驻”；启动后立即显示 FACM 自己的轻量 Shell。
- Shell 窗口已从 88×88 收紧到 **56×56**，实际可见主体约 46px；旧蓝色玻璃球的外发光、呼吸和环绕亮点已移除，改为深色圆角方形、细边框、品牌 `F` 和轻量 Hover。
- Shell 空闲时不再 33ms 常驻重绘，只在 Hover 过渡时短暂刷新；透明分层窗口上的文字渲染使用灰度抗锯齿，避免 ClearType 彩边。
- 当 `AnimalPetEnabled=true` 时，Shell 先保持可用；PetHost 真正发送 `ready` 后才隐藏 Shell，由桌宠接管。PetHost 失败时继续恢复/保留 Shell。
- **默认 `AnimalPetEnabled=false` 时不预热 PetHost**。只有配置已启用桌宠或用户主动选择桌宠后，才进入 PetHost 准备/启动链；不能让可选 VPet 成为默认启动负担。

### 主题与桌面形态

- 控制中心底部已从 `日志 / 面板主题 / 桌面宠物 / 海斗排行榜 / 退出` 收敛为 `日志 / 主题 / 海斗排行榜 / 退出`。
- 托盘同样只保留一个顶层「主题」入口，不再并列显示“控制面板主题 / 桌面宠物 / 宠物复位 / 恢复默认悬浮球”。
- 「主题」菜单内部区分：`面板外观…` 与 `桌面形态 → FACM 悬浮入口 / 选择桌面宠物… / 复位桌面位置`。
- 「主题」是统一产品入口；现阶段不强行合并 `ThemeId` 与 `AnimalPetEnabled/PetStyleId` 的持久化枚举，避免 UI 整理破坏既有配置兼容。
- 没有把任何具体桌宠名称擅自提升为产品固定名称；桌面宠物仍按现有目录/配置名称展示。
- Build #741 实机日志暴露「主题」临时下拉菜单的生命周期回归：`Closed` 事件同步 `Dispose()`，而 WinForms 内部 `SetVisibleCore/OnItemClicked/ModalMenuFilter` 仍在当前消息栈中继续使用该菜单，最终连续触发 `ObjectDisposedException` 并可终止主消息循环。
- 修复已改为：主题菜单动作通过 `BeginInvoke` 推迟到 ToolStrip 点击栈退出后执行；菜单本身也在 `Closed` 后通过 owner 消息队列延迟 Dispose；通用 `ContextMenuStrip` 的 outside-click Tick 增加 disposing/disposed 防护，避免已排队的 WM_TIMER 在销毁后重新触碰下拉句柄。

### 验证状态

- Build #716：`x/1995` 资源准备阶段接入真实进度条。
- Build #719：移除动画来源 UI。
- Build #728：Shell-first、ready 后再接管、后段不定进度条通过。
- Build #736：56px 新 FACM Shell、统一主题入口、默认不预热未启用 PetHost，Windows Build 全步骤成功；Mayhem Source Probe #75 成功。
- Build #741：首个可实机验收的新 Shell + 主题整合包；用户日志随后确认主题菜单 Dispose 生命周期回归，因此该包**不可接受为候选**。
- 菜单生命周期根修最新代码提交为 `35e0e0defc09ed250acd547bcb4b73611fc253e3`；等待其最终 HEAD CI 与下一轮实机菜单验收。
- 当前阶段：**等待修复包实机验收**。PR #40 仍未合并、未发布；验收前不继续扩大功能范围。

## 在线更新状态

- 现有受支持客户端应通过 `online/version.json` 检测 3.1.2。
- 本次不是强制更新；低于 `minimum_version` 的既有安全语义保持不变。
- 3.1.1 的在线更新链路此前已在真实 Windows 客户端完成“检测 → 下载 → 校验 → 替换 → 重启”实机验证，3.1.2 继续沿用同一事务式发布与客户端更新机制。
- PR #40 当前只提供测试 artifact，不修改 3.1.2 在线更新清单。

## 后续

FACM 3.1.2 继续作为当前线上稳定基线。PR #40 下一步只做实机视觉/交互验收和必要微调，不自动正式发布。

用户此前提到的“打包内置自定义默认配置”仍未在 PR #40 中实现；应在 Shell/主题结构稳定后单独处理，避免把开发机专属 `BallX/BallY/GamePath` 原样写入所有用户默认配置。

Issue #33 `整理轻量蜘蛛桌宠方案` 仍是已记录的独立后续方向，不因本次 Shell/主题重构自动进入实现。
