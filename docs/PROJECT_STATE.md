# FACM 当前项目状态

> 2026-08-15：FACM 3.2.0 仍是当前正式生产基线。原 League 五阶段规划已经 **5/5（100%）DONE**；其后的第一个独立扩展 **Tools / Automation Gate 2（OP.GG 一键应用符文 + 召唤师技能）也已完成腾讯/国服实机验收并合入 `main`**。
>
> 当前没有新的 League 主线 Gate 在实施。后续如果继续装备集磁盘写入、更多 OP.GG 自动化或其它能力，必须另建独立 Issue / Gate；不要重新设计已经验收通过的 Dashboard / Player / Live / Build Advisor / Apply Gate。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- Gate 2 本轮没有创建新 Release、Tag，没有修改 `online/version.json`，没有改变线上版本。

## 最新完成扩展：Tools / Automation Gate 2

- Issue #96：completed。
- PR #97：merged。
- task branch：`feat/opgg-apply-gate2-96`（未删除）。
- 腾讯/国服最终候选 HEAD：`5472114145c7467db536c25ef8d7596ca0222cb5`。
- 候选 Windows Build #986：SUCCESS。
- 候选 UI Text Contract #107：SUCCESS。
- Build #986 日志明确输出 `FACM performance contract smoke passed.`。
- 候选 artifact：`FACM-Windows-x64-986`，artifact id `9226997388`。
- artifact ZIP SHA-256：`49B3D0177471F9C44EA13316214643C903B38A7576EBDD34A74AFCFB6C85399B`。
- packaged FACM.exe SHA-256：`C041774343586D8A390DA86A19B654CB274BE8167179E8100BBA596F6801ED27`。
- 用户腾讯/国服实机验收反馈：**“经我测试 功能正常使用”**。
- 行为 merge commit：`67abfc0d9f4c3fced927f7888954f1948f77f945`。
- merge 后 main：UI Text #108 SUCCESS / Windows #987 SUCCESS。
- GitHub 自有 PR 无法由作者自己 APPROVE；验收文字已记录在 PR #97 conversation，不影响真实验收结论。

### Gate 2 已验收行为

Gate 2 只增加 **用户显式触发的一键应用符文 + 召唤师技能**：

- 保留 Gate 1 `OP.GG 对局助手` 为只读推荐；Gate 2 使用独立入口 `OP.GG 一键应用`。
- 继续复用唯一 `LeagueClientModule` 和唯一 `LeagueClientSessionProvider`，不创建第二套 process / lockfile / WMI discovery 或 token 生命周期。
- 只读 `LeagueClientApiClient` 与最小写 `LeagueClientWriteApiClient` 共享同一 session provider。
- 未点击按钮时零 LCU 写请求。
- 用户点击后才准备当前 OP.GG structured spell/rune IDs；之后还必须经过 Yes / No 二次确认，默认按钮为 No。
- 写入前重新读取 Gameflow / Champ Select；必须仍在 `ChampSelect`、仍是同一英雄，并在两侧可判断时仍是同一 queue，否则 fail-closed、零写。
- 召唤师技能：GET 当前 `/lol-champ-select/v1/session/my-selection` → 保持可判断的 Flash D/F 槽位 → PATCH → 再 GET 精确读回验证。
- 符文：GET `/lol-perks/v1/inventory`；只有 `canAddCustomPage=true` 才创建新的 `[FACM]` 页面。
- 符文写链：POST create → PUT page → PUT currentpage → GET pages，按新 page id / style / selected perks 读回验证。
- `canAddCustomPage=false` 时直接跳过符文；**绝不采用 Akari“覆盖第一页” fallback，不覆盖用户已有符文页**。
- 符文和召唤师技能独立记结果；一项成功一项失败只报 partial；零项成功不能误报 partial/success。
- 所有写入串行；页面关闭取消；UI async 异常 containment 在 Gate 2 窗口内。
- In Game 零写请求。

### Gate 2 transport 硬边界

LCU writer 不只依赖上层“自觉”，transport 自身有 method + path allowlist，只允许本 Gate 必需写入：

- `PATCH /lol-champ-select/v1/session/my-selection`
- `POST /lol-perks/v1/pages/`
- `PUT /lol-perks/v1/pages/{newPageId}`
- `PUT /lol-perks/v1/currentpage`

ready-check accept、Champ Select actions、pick / ban / swap / reroll / dodge / skin、带 query 绕行等其它写路径在 HTTP 发出前拒绝。

### Gate 2 deterministic smoke

已接入 `PerformanceContractSmokeTest`，覆盖：

- Prepare / 预览 = 0 writes；
- 当前 OP.GG `runes` style / rune / stat-mod ID 解析；
- happy path exact endpoint / body contract；
- Flash-on-D / Flash-on-F 保持；
- rune inventory full → 不 GET/覆盖已有 page；
- rune skip + spell success → partial；
- 非 ChampSelect / champion drift → 0 writes；
- partial / zero-success 结果真实性；
- caller cancellation → 0 writes；
- forbidden endpoint exclusion；
- transport hard allowlist 拒绝 ready-check、Champ Select actions、query-string escape。

## 装备集仍是独立后续 Gate

League Akari 当前源码中装备集不是同一类 LCU 写入：

1. GET `/data-store/v1/install-dir`
2. 非腾讯通常写 `installDir/Config/Global/Recommended`
3. Tencent 路径特殊落到 `installDir/../Game/Config/Global/Recommended`
4. 管理自身前缀 `.json` 文件、旧文件清理和 recipe item restore

因此装备集必须单独定义：腾讯路径、文件原子写、失败恢复/回滚、旧文件删除范围、客户端读取时序。**不要顺手塞回 Gate 2。**

## 原 League 五阶段：5/5 DONE

1. **League Dashboard Gate 1 — DONE**
2. **Player Gate 1 — DONE**
3. **Champ Select / Current Game Gate 1 — DONE**
4. **Player Gate 2 — DONE**
5. **Tools / Automation Gate 1（OP.GG 对局助手只读推荐）— DONE**

原规划正式完成进度：**5/5 = 100%**。Gate 2 是 5/5 之后完成的独立扩展，不改变原进度定义。

## 关键已完成行为基线

### Tools / Automation Gate 1

- Issue #93 completed；PR #94 merged。
- 最终行为候选 `3b3a3e3ddeeb3fb40fa86a9de4a440c42d34d66f`。
- merge commit `90b3c829aa8682f0d6be139512b348eb4f4aff78`。
- Build #974 腾讯/国服实机确认：`ChampSelect · 疾风剑豪 #157 · ranked / mid`、OP.GG Global 16.16、Tier / Win / Pick / Ban、召唤师技能、符文、出装、技能加点、Counter 正常。
- Gate 1 仍是只读推荐；Gate 2 的后续写能力不改变 Gate 1 当时的只读验收事实。

### Player Gate 2

- Issue #90 completed；PR #91 merged。
- 最终候选 `24b9db09bc50d0d8490fa46bdd303d59a0f1583a`。
- merge commit `1ae84844feddddda91226867172ff93c9cb8d5aa`。
- Build #965 腾讯/国服确认 `英雄` 列、`英雄表现（当前已加载 2 场）`、中文英雄名称与当前已加载战绩统计正常。
- 英雄名称走本地 `/lol-game-data/assets/v1/champion-summary.json`；统计只基于当前 10/20 场，不追加 match-history。

### Champ Select / Current Game Gate 1

- Issue #85 completed；PR #86 merged。
- 候选 `bf9ae52dbb814a5ac862c6671085e6ed0300d456`。
- merge commit `4d0cbe43c9ae5e1bae62ad62d398f8fba1ab138a`。
- Build #955 腾讯/国服实机通过。
- Live Gate 保持只读，不做 teammate history fan-out、图片后台预取、自动 accept / pick / ban / swap / reroll / dodge / 改技能或皮肤。

### Player Gate 1 / Dashboard / 单实例

- Player Gate 1：Issue #81 completed；PR #82 merged；Build #951 腾讯/国服实机通过。
- League Dashboard Gate 1：DONE，腾讯/国服已验收。
- 单实例 Ensure Open / Activate：Issue #53 / PR #54 DONE；没有真实缺陷时不重做。

## 已完成并冻结

没有真实缺陷或新独立需求时，不要顺手重做：

- Modular Host Phase 1～5
- Real Pet Gate 1
- UI Text Contract
- Performance Contract
- League Dashboard Gate 1
- Player Gate 1 / 2
- Champ Select / Current Game Gate 1
- Tools / Automation Gate 1
- Tools / Automation Gate 2
- 单实例 Ensure Open / Activate
- Flying Runtime
- VPet / PetHost
- Cleanup 安全语义
- Mayhem 多源容灾
- Online Release 事务与现有配置兼容

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不是当前主线，不自动恢复。

## Performance Contract

原则：**高配要快，普通机要顺；游戏优先，FACM 第二优先。**

预算上限：

- Desktop：network 4 / image 2 / disk 2 / CPU 2 / prefetch 20
- League Client：3 / 2 / 2 / 2 / 12
- Queueing：2 / 1 / 1 / 1 / 4
- Champ Select：2 / 1 / 1 / 1 / 0
- In Game：1 / 1 / 1 / 1 / 0；后台预取、维护和非必要视觉增强关闭
- Hidden/background：1 / 1 / 1 / 1 / 0

从 Desktop → Client → Queueing → Champ Select → In Game，数字预算和功能开关只能更保守。

- Dashboard Gameflow monitor：client 约 5s / queue 3s / champ-select 2s / in-game 10s。
- Player：无后台定时刷新；默认 10 场，手动最多 20 场；敏感阶段禁止详情预取。
- League Live：Champ Select 可见轮询不快于约 2s；In Game 不快于约 10s；串行、关闭取消。
- OP.GG 对局助手：只在窗口打开时工作；请求串行、同上下文缓存；In Game 不新增 OP.GG 请求。
- Gate 2 一键应用：无后台轮询、无自动触发；只有用户点击 + 确认后才串行写；In Game 零写。不能放宽既有预算。

## League / 腾讯国服已验证基线

- 所有 League 功能继续复用唯一 `LeagueClientModule`，禁止新增平行 LCU connector。
- discovery：进程路径 → 同目录 Riot `lockfile`；活动 lockfile 使用 `FileShare.ReadWrite` + 短重试。
- `MainModule.FileName` 失败时可用 WMI `ExecutablePath`；仍失败时仅 `LeagueClientUx` 使用 WMI `CommandLine` fallback。
- 凭据只在内存使用，禁止日志/UI 输出。
- Akari 官网“不支持腾讯服务器”只视为官方免责声明；腾讯兼容按源码机制 + fixture + 实机功能测试判断。
- Dashboard：腾讯环境已验证当前召唤师、平台/区服 `CQ100`、Gameflow、Performance。
- Player Gate 1：腾讯实机通过。
- Champ Select / Current Game：Build #955 腾讯实机通过。
- Player Gate 2：Build #965 腾讯实机通过。
- OP.GG 对局助手：Build #974 腾讯实机通过。
- OP.GG 一键应用：Build #986 腾讯实机通过，用户反馈“经我测试 功能正常使用”。
- 腾讯 match-history 的 `gameCount` 不作为账号全历史总数；分页按请求窗口实际返回数量判断。

## 下一步规则

当前没有自动启动的新 Gate。

如果继续升级，优先候选包括：

- **装备集磁盘写入 Gate**（必须独立做腾讯路径 + 原子写 + 回滚/清理语义）；
- 更多 OP.GG 数据/工具，但必须继续服从 Performance Contract；
- 其它新工具能力。

任何新 Gate 都先 fresh-check `main`、开放 Issue/PR 与现有边界，再建立唯一任务分支；不重开已经完成的五阶段和 Gate 2。
