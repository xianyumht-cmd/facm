# FACM 当前项目状态

> 2026-08-11：FACM 3.1.2 已正式发布并启用在线更新。

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

## 3.1.2 之后的 main 开发

- PetHost 首次生成 VPet 动作/PNG 缓存时，启动卡改为“正在编译着色器…”产品文案 + 真实进度条 + 百分比/完成数；进度直接使用 VPet `LoadALL` 的 `readyCount / graphCount`，不是定时器模拟进度。
- 该启动卡属于 FACM 自己的 `FACM.PetHost/PetHostWindow.cs`，并非 VPet 配置项；底层实际工作仍是 VPet 动作缓存生成，产品文案不改变加载语义。
- `VPet / VUP-Simulator（非商用授权）` 来源说明继续保留。
- 该变更当前只进入开发/测试链路，未单独触发新的正式在线版本；FACM 3.1.2 仍是线上稳定版。

## 在线更新状态

- 现有受支持客户端应通过 `online/version.json` 检测 3.1.2。
- 本次不是强制更新；低于 `minimum_version` 的既有安全语义保持不变。
- 3.1.1 的在线更新链路此前已在真实 Windows 客户端完成“检测 → 下载 → 校验 → 替换 → 重启”实机验证，3.1.2 继续沿用同一事务式发布与客户端更新机制。

## 后续

FACM 3.1.2 可作为当前稳定基线。新功能或缺陷继续按 `AGENTS.md` 从最新 `main` 建一任务一短分支 + PR。

已记录的下一产品方向仍是 Issue #33 `整理轻量蜘蛛桌宠方案`：先复盘旧蜘蛛方案失败模式并确认运动模型、渲染方式、性能预算和交互边界，再决定是否实现，不因本次 3.1.2 发布而自动进入编码。
