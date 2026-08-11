# FACM 当前项目状态

> 2026-08-12：FACM 3.1.2 已正式发布并启用在线更新；PR #40 正在实机验收 FACM Shell、主题/桌面形态、PetHost 加载与桌宠移动体验。

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
- 用户已完成实际查询验收并明确授权正式发布和在线更新推送。
- 发布请求 PR #38 的 FACM Windows Build #697 成功后合并到 main。
- 正式发布继续经过 PetHost publish/self-test、FACM Release build、内嵌资源验证、Authenticode 签名、SHA-256、disabled manifest、公开 GitHub Release、最终 enabled manifest 的事务式链路；在线清单现已指向 v3.1.2。

## PR #40：PetHost 加载、FACM Shell 与主题整合

### PetHost 加载卡

- 启动卡属于 `FACM.PetHost/PetHostWindow.cs`，不是 VPet 配置项。
- VPet 资源准备的 `x/1995` 阶段显示“正在编译着色器…” + determinate 真实进度条 + 百分比/完成数。
- “正在核对 VPet 官方动作清单”显示“加载中请稍等....”。
- 第一次编译结束后的 `LoadALL` 阶段显示“加载中请稍等....” + indeterminate 不定进度条；不再显示可能长期卡住的 `0% / 0/N`。
- 加载卡不显示“动画来源：VPet / VUP-Simulator（非商用授权）”；授权/来源信息继续保留在随包文档与 NOTICE 中。

### PetHost 启动性能

- 第一轮 Build #703 虽然 CI 成功，但用户 Windows 实机发现加载卡比上一版约晚 30 秒出现，因此未合并。
- 根因是内嵌 PetHost 为 self-contained runtime，旧启动链在进程启动前释放/检查数百个文件，缓存命中还递归扫描完整目录；慢盘/杀软会直接推迟 WPF 窗口出现。
- 缓存身份现由 FACM MVID 改为内嵌 `PetHost.zip` SHA-256；首次释放完整统计，后续命中只检查完成标记与关键启动文件。
- 用户 2026-08-12 实机日志显示：首次 VPet `startup-ms=103446`，后续缓存命中 `startup-ms=641`，证明快速缓存路径已生效。

### FACM Shell 与默认启动

- 新产品决策：FACM 默认不再采用“桌面无入口、仅托盘常驻”；启动后立即显示 FACM 自己的轻量 Shell。
- Shell 窗口从 88×88 收紧到 **56×56**，实际可见主体约 46px；旧蓝色玻璃球的外发光、呼吸和环绕亮点已移除，改为深色圆角方形、细边框、品牌 `F` 和轻量 Hover。
- Shell 空闲时不再 33ms 常驻重绘；首次 layered frame 有有限重试和 Win32 错误日志，避免单次 `UpdateLayeredWindow` 时序失败后只剩托盘图标。
- 当 `AnimalPetEnabled=true` 时，Shell 先保持可用；桌宠真正 ready 后才隐藏 Shell，由桌宠接管。失败时继续恢复/保留 Shell。
- 默认 `AnimalPetEnabled=false` 时不预热 PetHost。只有配置已启用桌宠或用户主动选择桌宠后，才进入 PetHost 准备/启动链。
- “复位桌面位置”在 Shell 模式下现在会清除已保存的 `BallX/BallY`，恢复到主屏右侧默认位置并保存默认哨兵；不再错误地调用“读取旧保存位置”。

### 主题与桌面形态

- 控制中心底部从 `日志 / 面板主题 / 桌面宠物 / 海斗排行榜 / 退出` 收敛为 `日志 / 主题 / 海斗排行榜 / 退出`。
- 托盘同样只保留一个顶层「主题」入口。
- 「主题」菜单内部区分：`面板外观…` 与 `桌面形态 → FACM 悬浮入口 / 选择桌面宠物… / 复位桌面位置`。
- 「主题」是统一产品入口；现阶段保留既有 `ThemeId` 与 `AnimalPetEnabled/PetStyleId` 配置兼容，不擅自合并持久化枚举或固定桌宠名称。
- Build #741 的临时主题菜单曾因 `Closed` 同步 Dispose 引发 `ObjectDisposedException`；现已改为延迟 Dispose、菜单动作延迟到 ToolStrip 点击栈退出后执行，并对 outside-click Timer 做销毁防护。
- Build #759 修复二级「桌面形态」点击被 outside-click watcher 当成菜单外点击的问题；用户实机确认该版本整体“比较满意”。

### Sprite 桌宠方向

- 当前产品优先级按用户实机体验收敛：**苍蝇（greenfly）优先，其次蜘蛛（spider），再其次 VPet**；其它低质量候选暂不优先投入。
- 用户最看重桌宠的移动轨迹和真实性。Sprite 桌宠不再把屏幕 WorkingArea 当硬边界：取消边缘 clamp/bounce，允许随机自然移动到屏幕外；手动拖动后也不再强制拉回可视范围。恢复入口由“复位桌面位置”承担。
- 苍蝇现有像素源清晰度偏低，但其移动轨迹应保留；后续若替换素材，应优先提高源清晰度而不是破坏现有移动感。
- 蜘蛛素材清晰度较好，但用户观察到偶发“倒着走”。当前方向行按数学八方向直接映射，原始素材虽明确为 8 方向 × 13 帧，但没有可靠文档给出 spritesheet 八行的具体方向顺序；在确认实际行序前不凭猜测重排。
- VPet 真实感较好但运行层较重，且当前没有自主漫游；这属于后续桌宠行为优化，不阻塞本 PR 当前 Shell/主题验收。

### 验证状态

- Build #716：`x/1995` 资源准备阶段接入真实进度条。
- Build #719：移除动画来源 UI。
- Build #728：Shell-first、ready 后再接管、后段不定进度条通过。
- Build #736：56px 新 FACM Shell、统一主题入口、默认不预热未启用 PetHost。
- Build #741：首个新 Shell + 主题整合包，但主题菜单 Dispose 生命周期存在回归，不作为候选。
- Build #755：菜单生命周期根修通过。
- Build #757：Shell 首次 layered frame 有限重试与日志通过；用户实机确认无配置时 Shell 正常显示，有 VPet 配置时自动加载 VPet。
- Build #759：主题二级桌面形态菜单修复通过；用户实机确认整体体验较满意。
- Build #762：Shell 真正复位 + Sprite 桌宠取消硬屏幕边界；Windows Build 成功。用户已实机确认“复位桌面位置”正常、Sprite 桌宠跑出屏幕后行为正常，两项均验收通过。
- PR #40 最新 HEAD `e58206e07dc116fa5e4b7e4c73ea6603327c7ad7` 的 Windows Build #763 与 Mayhem Source Probe #103 均成功；用户已明确授权将当前版本作为正式版发布并推送在线更新。
- 当前阶段：**准备合并 PR #40 并进入正式发布事务链**。

## 在线更新状态

- 现有受支持客户端应通过 `online/version.json` 检测 3.1.2。
- 本次不是强制更新；低于 `minimum_version` 的既有安全语义保持不变。
- 3.1.1 的在线更新链路此前已在真实 Windows 客户端完成“检测 → 下载 → 校验 → 替换 → 重启”实机验证，3.1.2 继续沿用同一事务式发布与客户端更新机制。
- PR #40 当前不直接修改在线更新清单；合并后按正式发布工作流生成新版本并在发布资产验证成功后启用新 manifest。

## 后续

PR #40 发布完成后，下一项优先继续桌宠素材质量：保留现有运动引擎和轨迹逻辑，先提高苍蝇源素材清晰度，再校准蜘蛛 8 方向 spritesheet 行序；素材实验不得混入本次正式发布产物。

用户此前提到的“打包内置自定义默认配置”仍未在 PR #40 中实现；应在 Shell/主题结构稳定后单独处理，避免把开发机专属 `BallX/BallY/GamePath` 原样写入所有用户默认配置。