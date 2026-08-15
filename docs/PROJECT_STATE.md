# FACM 当前项目状态


<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.3.0
- GitHub Release：v3.3.0
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- 发布基础 main：73d49964fdcc8e3a1441dfbcc605d07f7a4ce7c0
- 发布元数据提交：5877ffbe8d69eb1ed826186574daefce6d85b4e0
- Release FACM.exe SHA-256：74649FEC5153A3D47681529D892227F28EB395FC68460D0D12FC8B9D3B9C9C2F
- published_at：2026-08-15T05:13:06.9356240+00:00
- release_notes：FACM 3.3.0：控制中心与托盘菜单进一步收束，新增选人自动应用 FACM 推荐、全局一键结束游戏/关闭大厅、赛后随机点赞与自动返回大厅，以及可选的自动寻找和接受对局。自动化默认关闭，继续保持轻量、低占用、低打扰。
<!-- FACM_RELEASE_STATE_END -->
> 2026-08-15：线上正式生产仍为 **FACM 3.2.0 / v3.2.0**。FACM 3.3.0 已进入用户明确授权的正式发布收口；集成 PR #115 汇合已验收/授权功能。发布工作流成功前，不把 3.3.0 视为生产版本。

## 当前正式生产

- FACM 3.2.0 / GitHub Release `v3.2.0`
- online update：enabled=true
- minimum_version：3.0.0
- force_update=false
- 3.2.0 Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`

## FACM 3.3.0 正式候选

- integration branch：`release/3.3.0-accepted-features`
- PR #115：`FACM 3.3.0：整合已验收 League 效率功能与小白 Shell`
- 版本：3.3.0.0 / informational 3.3.0
- 首个完整集成行为候选 HEAD：`607feeb2d5fd29265945055ae131a0274882cdc3`
- Windows Build #1041：SUCCESS
- UI Text Contract #162：SUCCESS
- Mayhem Source Probe #279：SUCCESS
- Windows #1041 明确输出：`FACM performance contract smoke passed.`
- Build #1041 FACM.exe SHA-256：`609E1C283AB0EE2D055378C2BFE1BFAE9CA9C20B37D5B261C74E3D32939A7418`
- Build #1041 artifact：`FACM-Windows-x64-1041` / id `9241993929`
- artifact ZIP SHA-256：`9CD0A8AC909EA67B69042B165900B84BD091D567FD5A3B7C7724953BD73582B7`
- artifact 只用于 CI 候选证据；正式发布必须重新由 `publish-release.yml` 构建/签名/发布。

### 3.3.0 包含范围

1. **小白版 Shell UX — 用户验收通过**
   - 托盘一级固定 5 项：打开控制中心 / 清理环境 / 英雄联盟 / 更多 / 退出程序；
   - League/OP.GG/游戏效率等业务功能只能进入二级菜单；
   - 控制中心主页采用渐进披露，不再把大量低频按钮平铺；
   - `ShellMenuGroups + ShellUxSmokeTest` 防止后续入口重新膨胀。

2. **Tools / Automation Gate 4：选人自动应用推荐 — 用户验收通过**
   - `LeagueAutoApplyRecommended` 默认 False；
   - 稳定 Champ Select recommendation fingerprint 只自动执行一次；
   - 自动应用符文、召唤师技能和 FACM Recommended item set；
   - 同一路径 OP.GG raw payload 共享 cache；
   - 保留 Gate 2 满符文页绝不覆盖、Gate 3 `facm1-*` 文件 ownership 等安全边界。

3. **游戏效率全局快捷键 — 腾讯实机基本验收通过**
   - 一键结束游戏：全局快捷键精确结束国服 `League of Legends(TM)`；
   - 一键关闭大厅：全局快捷键精确关闭 LeagueClient family；
   - 使用独立 STA `RegisterHotKey` message thread，FACM 后台/最小化不影响触发。

4. **赛后自动化 — 用户验收通过**
   - 自动随机点赞一名 eligible teammate；
   - 自动返回大厅；
   - 同一连续赛后 episode 最多一次；
   - 点赞失败不阻止 `play-again`。

5. **自动下一局 — 用户明确授权随 3.3.0 直接发布**
   - 自动寻找对局；
   - 自动接受 ReadyCheck；
   - 两项默认关闭；
   - 用户明确接受“先发布，实机发现问题再修”的验收策略，因此当前不声称已完成腾讯真实匹配流程实机验证。

### 明确不进入 3.3.0

**账号密码快捷输入已取消。**

用户在真实 QQ/腾讯登录页验证后明确要求“不搞这个了”。3.3.0 正式候选已物理剔除：

- 无 credential hotkey 设置；
- 无账号密码输入 UI；
- 无剪贴板凭据解析入口；
- 无 SendInput credential path；
- 无 UIAutomation credential focus 依赖；
- settings / smoke 明确检查正式产品不包含该能力。

该失败实验不得在后续“顺手恢复”。如果未来重新提出，必须作为新独立需求重新设计。

## League 已完成主线

原五阶段 League roadmap 保持：**5/5 = 100% DONE**。

1. League Dashboard Gate 1 — DONE / 腾讯验收
2. Player Gate 1 — DONE / 腾讯验收
3. Champ Select / Current Game Gate 1 — DONE / 腾讯验收
4. Player Gate 2 — DONE / 腾讯验收
5. Tools / Automation Gate 1（只读 OP.GG 对局助手）— DONE / 腾讯验收

后续已完成/收口：

- Gate 2：手动一键应用符文 + 召唤师技能 — DONE / 腾讯验收 / merged
- Gate 3：Recommended item set — 腾讯游戏商店已确认识别；标题统一 `[FACM]`
- Gate 4：自动应用推荐 — 用户验收通过 / 已进入 3.3 集成
- League Efficiency Gate 5：正式只保留结束游戏 + 关闭大厅两个快捷键
- Gate 6：赛后随机点赞 + 自动返回 — 用户验收通过
- Gate 7：自动找局 + 自动接受 — deterministic smoke 完成，用户授权随 3.3 发布后再以真实使用反馈修正

## 3.3 性能/权限冻结边界

- 唯一 `LeagueClientModule + LeagueClientSessionProvider` 不变；
- 不新增第二套 League discovery/auth connector；
- 自动化默认关闭；
- 不做游戏内 Overlay/注入；
- 不做自动 pick / ban / swap / reroll / dodge / skin；
- Gate 2、赛后、匹配分别使用最小 writer allowlist；
- Gate 2 writer 继续硬拒绝 ready-check accept；匹配能力只能通过 Gate 7 专用 writer；
- In Game Performance budget 仍为 network/image/disk/background CPU concurrency 1，prefetch 0；
- 全局快捷键使用 RegisterHotKey，无键盘轮询/low-level hook。

## 当前发布计划

用户已明确授权：**合并上述 3.3 范围、发布正式版本、推送在线更新和更新公告。**

流程：

1. PR #115 完整 CI 全绿；
2. canonical docs 与实际代码一致；
3. Ready + 精确 HEAD merge #115；
4. 验证 main post-merge Windows/UI CI；
5. 通过 `release/request.json` 请求 FACM 3.3.0：minimum 3.0.0、force_update=false、prerelease=false；
6. `publish-release.yml` 事务式创建 `v3.3.0`，最后启用 online manifest；
7. Release 成功后更新 `online/announcement.json` 与最终 production PROJECT_STATE；
8. 旧并行 PR #105/#110/#112/#114 标记为已被 #115 吸收/替代；不删除分支。

## 冻结的稳定系统

没有真实缺陷或新独立需求时，不重新设计：

- Modular Host
- Performance Contract
- UI Text Contract
- Single-instance Ensure Open / Activate
- Flying Runtime / VPet / PetHost
- Cleanup 安全语义
- Mayhem 多源容灾
- Online Release 事务
- 已验收 League Dashboard / Player / Live / OP.GG Read Advisor / Gate2 Apply / item-set ownership

旧 Issue #33 / Draft PR #35 机器猫继续暂停，不随 3.3 发布自动恢复。
