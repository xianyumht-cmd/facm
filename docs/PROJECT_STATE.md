# FACM 当前项目状态

> 2026-08-12：FACM 3.1.2 已正式发布并启用在线更新；PR #40 正在实机验收 PetHost 启动卡、启动性能与 shell-first 启动体验。

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

## PR #40：PetHost 启动卡、启动性能与 shell-first 启动

- 启动卡属于 `FACM.PetHost/PetHostWindow.cs`，不是 VPet 配置项；VPet 资源准备的 `x/1995` 阶段显示“正在编译着色器…” + determinate 进度条 + 百分比/完成数。
- “正在核对 VPet 官方动作清单”改显示“加载中请稍等....”。第一次编译结束后的 `LoadALL` 阶段也显示“加载中请稍等....”，但由于该阶段的 `readyCount` 在真实机器上可能长期保持 0，现已改为 **indeterminate 不定进度条**，不再显示误导性的 `0%` / `0/N`。
- 按用户要求，加载卡不再显示“动画来源：VPet / VUP-Simulator（非商用授权）”；授权/来源信息继续保留在随包文档与 NOTICE 中，不占加载卡 UI。
- 第一轮 Build #703 虽然 CI 成功，但用户 Windows 实机发现加载卡比上一版约晚 30 秒出现，因此未合并。根因是内嵌 self-contained PetHost 在进程启动前需要释放/检查数百个文件，并受慢盘/杀软扫描影响。
- PR #40 已把 PetHost 缓存身份由 FACM MVID 改为内嵌 `PetHost.zip` SHA-256；首次释放仍完整校验，后续缓存命中只检查完成标记与启动关键文件；相同 PetHost payload 可跨 FACM-only 构建复用。
- **新的产品启动不变量：FACM shell 必须先可见。** `Program` 在 WinForms message loop 前只创建最小 runtime 目录，不再同步加载 ToolBundle/PetHost；`MainForm.Shown` 后后台预热两类可选 payload。
- 用户启用了桌宠时，默认 FACM 悬浮入口会继续保持可见；只有 PetHost 真正发出 `ready` 后，才把默认入口隐藏并交给桌宠接管。这样解包、杀软扫描、资源准备、动作缓存或 PetHost 失败都不会制造“程序好像没启动”的无可见 UI 间隙。
- 最新代码 HEAD `cabee2feb7a59cf14d233f11591364d5f42c2a22` 的 FACM Windows Build #728 成功，FACM Mayhem Source Probe #67 成功；artifact 等待用户实机确认 shell 首帧、VPet 接管时机和第二阶段不定进度条。
- PR #40 在用户确认前不得合并，也不得触发新的正式在线版本。

## 在线更新状态

- 现有受支持客户端应通过 `online/version.json` 检测 3.1.2。
- 本次不是强制更新；低于 `minimum_version` 的既有安全语义保持不变。
- 3.1.1 的在线更新链路此前已在真实 Windows 客户端完成“检测 → 下载 → 校验 → 替换 → 重启”实机验证，3.1.2 继续沿用同一事务式发布与客户端更新机制。
- PR #40 当前只提供测试 artifact，不修改 3.1.2 在线更新清单。

## 后续

FACM 3.1.2 继续作为当前线上稳定基线。PR #40 完成实机验收后，再决定是否合并和安排后续正式版本。

下一项产品结构调整已经明确：**“桌面宠物”和“面板主题”合并为一个“主题”入口**，统一管理面板主题与桌面形态；同时重新设计默认 FACM 悬浮入口的现代轻量视觉。该信息架构调整应在 PR #40 验收后使用独立短分支实施，避免把当前启动修复继续扩大。