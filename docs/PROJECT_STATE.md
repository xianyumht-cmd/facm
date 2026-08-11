# FACM 当前项目状态

> 2026-08-12：FACM 3.1.2 已正式发布并启用在线更新；PR #40 正在实机验收 PetHost 启动卡与启动性能修复。

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

## PR #40：PetHost 启动卡与启动性能

- 目标：把 VPet 资源准备阶段与 `LoadALL` 动作/PNG 缓存阶段的 FACM 启动卡统一改为“正在编译着色器…” + determinate 真实进度条 + 百分比/完成数；该界面属于 `FACM.PetHost/PetHostWindow.cs`，不是 VPet 配置项。
- “正在编译着色器”只是产品层展示文案，底层实际仍是 VPet 资源准备与动作/PNG 缓存生成；进度使用真实完成数，不使用定时器模拟。
- 按用户最新要求，加载卡不再显示“动画来源：VPet / VUP-Simulator（非商用授权）”，对应 TextBlock 已从 UI 中移除且卡片高度收紧；授权/来源信息继续保留在随包文档与 NOTICE 中。
- 第一轮 Build #703 编译、自检和打包成功，但用户 Windows 实机发现加载卡比上一版约晚 30 秒出现，同时截图仍呈现旧式“正在缓存高精度动作 x/1995”且没有新进度条，因此**未通过实机验收、未合并、未发布**。
- 排查确认内嵌 PetHost 是 self-contained runtime，当前包约数百个文件；旧启动链在 `Process.Start(FACM.PetHost.exe)` 前先释放/检查内嵌宿主，缓存命中还会递归统计完整宿主目录，因此慢磁盘/杀软扫描可直接推迟 WPF 加载卡出现时间。
- PR #40 已在同一任务分支根修宿主缓存：缓存身份由 FACM MVID 改为内嵌 `PetHost.zip` SHA-256；首次释放仍做完整统计，后续缓存命中只检查完成标记与启动关键文件；FACM 启动准备完成后后台预热 PetHost，并与用户实际启用共享同一准备任务，避免重复解包。
- 用户后续视频表明加载窗口出现时间已明显恢复，但 `x/1995` 属于更早的 VPet 资源准备阶段；Build #716 已把该阶段也接入同一真实进度条。
- 当前最新代码继续验证“移除动画来源 UI”这一小改动；通过后重新提供最新 artifact 实机确认。
- PR #40 在用户确认启动时间和视觉效果前不得合并，也不得触发新的正式在线版本。

## 在线更新状态

- 现有受支持客户端应通过 `online/version.json` 检测 3.1.2。
- 本次不是强制更新；低于 `minimum_version` 的既有安全语义保持不变。
- 3.1.1 的在线更新链路此前已在真实 Windows 客户端完成“检测 → 下载 → 校验 → 替换 → 重启”实机验证，3.1.2 继续沿用同一事务式发布与客户端更新机制。
- PR #40 当前只提供测试 artifact，不修改 3.1.2 在线更新清单。

## 后续

FACM 3.1.2 继续作为当前线上稳定基线。PR #40 完成连续启动实机验收后，再决定是否合并和安排后续正式版本。

已记录的下一产品方向仍是 Issue #33 `整理轻量蜘蛛桌宠方案`：先复盘旧蜘蛛方案失败模式并确认运动模型、渲染方式、性能预算和交互边界，再决定是否实现，不因本次桌宠加载卡调整而自动进入编码。
