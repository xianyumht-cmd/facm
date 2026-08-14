# FACM 当前项目状态

> 2026-08-15：FACM 3.2.0 仍是当前正式生产基线。原 League 五阶段规划已经 **5/5（100%）DONE**；其后的 Tools / Automation Gate 2（OP.GG 一键应用符文 + 召唤师技能）也已完成腾讯/国服实机验收并合入 `main`。
>
> 当前正在实施 **5/5 之后的新独立扩展：Tools / Automation Gate 3（OP.GG 推荐装备集安全写入）**，Issue #99 / Draft PR #103。Gate 3 不重新设计已经验收通过的 Dashboard / Player / Live / Build Advisor / Gate 2 Apply。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 当前 Gate 3 没有创建新 Release、Tag，没有修改 `online/version.json`，没有改变线上版本。

## 当前进行中扩展：Tools / Automation Gate 3

- Issue #99：OPEN。
- Draft PR #103：OPEN / Draft；腾讯/国服真实商店识别验收前不 Ready、不合并。
- branch：`feat/opgg-itemsets-gate3-99`。
- base：`main` @ `641691108b8eca47c21c2b9b893c651f1ce957b7`。
- exact 行为候选 HEAD：`41110482986c9d562fba166b7472e1032027a95a`。
- UI Text Contract #117：SUCCESS。
- Windows Build #996：SUCCESS。
- Windows #996 日志明确输出 `FACM performance contract smoke passed.`，Gate 3 filesystem / UI text smoke 已随 Performance Contract 实际执行。
- 候选 FACM.exe：3.2.0.0。
- signed FACM.exe SHA-256：`C2E312D40C86B31339D7DB217937ACBD067BE89C3898C096DC7A92E52344F4A4`。
- artifact：`FACM-Windows-x64-996`，artifact id `9229662143`，size 154,746,197 bytes。
- artifact ZIP SHA-256：`18AD47E2B52036566CDBCC1F776B33F26EC39D16A3A50CFE82D401487082CE8C`。
- GitHub artifact：`https://github.com/xianyumht-cmd/facm/actions/runs/31828180073/artifacts/9229662143`。
- **CI / fake filesystem 不能替代腾讯客户端真实商店是否识别 Recommended JSON；当前仍待 Windows 腾讯/国服实机验收。**
- 完整交接：`docs/HANDOFF-20260815-TOOLS-GATE3.md`。

### Gate 3 产品行为

Gate 3 增加独立托盘入口 **`OP.GG 装备集`**：

- 继续复用已验收的 OP.GG Build Advisor champion / mode / position / version 上下文；
- 只有 `ChampSelect` + 用户主动点击后才重新获取 structured item IDs；
- 点击后仍需 Yes / No 二次确认，默认 No；
- Yes 后才读取 `/data-store/v1/install-dir` 并写 League Recommended JSON；
- 写入前重新检查仍为 `ChampSelect`、仍是同一英雄，并在两侧可判断时仍是同一 queue；变化则 fail-closed、零磁盘写；
- item groups 对齐 Akari 当前 OP.GG 路线：starter 最多 3 组、boots、prism、core 最多 4 组、last；
- 输出 League item-set `type=global / map=any / mode=any`；
- recipe restore 覆盖 Akari 当前 Muramana / Seraph / Fimbulwinter / upgraded item 及 22/32 前缀变体；
- In Game 零磁盘写。

### Gate 3 文件安全边界

FACM 不机械复制 Akari 的文件操作：

- FACM own prefix 固定 `facm1-`；文件名只由 FACM 根据当前上下文构造，不接受远端任意文件名/路径；
- install-dir 必须绝对路径且已存在；相对/不存在路径 fail-closed；
- install-dir leaf 为 `LeagueClient` 时，必须确认同级 sibling `Game` 已存在，才写 `../Game/Config/Global/Recommended`；否则不猜路径；
- 标准 Riot layout 才写 `installDir/Config/Global/Recommended`；
- 不递归扫描，不删任何非 `facm1-*.json`；
- 新 JSON 先在内存完整生成；
- 同目录 `.facm1-<guid>.tmp` 写入并验证后，再 atomic move / replace；
- destination 提交后再次读回验证 uid / title / blocks / items；
- 只有新 destination 验证成功后，才 best-effort 清理其它旧 `facm1-*.json`；
- replace private `.bak` 在 rollback 失败时保留恢复证据，不盲删；
- durable commit 完成后 cleanup 失败只报 warning，不把真实成功误报成取消/失败；
- 不 auto accept / pick / ban / swap / reroll / dodge / skin；不聊天广播；不 overlay / 注入；不新增第二套 LCU connector。

### Gate 3 deterministic smoke

已覆盖：

- Prepare 解析 OP.GG starter / boots / prism / core / last，且 0 filesystem mutation；
- `ranked / 157 / mid` 复用已验收 Build Advisor context；
- recipe restore；
- Tencent sibling `Game` path / standard Riot path；
- relative / missing / broken Tencent layout fail-closed；
- temp → atomic commit → destination read-back verify；
- `user.json` / `third-party.json` 不删不改；
- superseded `facm1-*.json` 仅在新文件成功后清理；
- InProgress / champion drift = 0 disk write；
- forced commit failure 保留旧 FACM + user file；
- caller cancellation before commit = 0 filesystem mutation；
- tray bridge；
- 21 个 Gate 3 scoped UI Text Key 均有非空默认文案并支持 `ui-text.ini` override。

### Gate 3 CI 历史

- Windows #995：Release compile 失败仅来自 deterministic fake 的 `Task<byte[]>` 方法误直接返回 `byte[]`（5 个 CS0029）；不是腾讯路径或运行时磁盘逻辑失败。
- 同一分支修正并精简 fake 后，行为候选 `41110482986c9d562fba166b7472e1032027a95a` 的 UI #117 / Windows #996 全绿。
- docs-only 后续提交不改变 Build #996 行为候选；判断程序行为以 `411104... / Build #996` 为准。

### Gate 3 下一步验收

腾讯/国服至少确认：

1. 原 `OP.GG 对局助手` 仍正常；
2. Gate 2 `OP.GG 一键应用` 符文 + 召唤师技能仍正常；
3. 新托盘入口 `OP.GG 装备集` 正常；
4. Champ Select 能显示当前英雄 / mode / position 与装备预览；
5. 未确认前不生成 `facm1-*.json`；选 No 必须零写；
6. 选 Yes 后国服正常 layout 写到类似 `E:\WeGameApps\英雄联盟\Game\Config\Global\Recommended\facm1-....json`；
7. FACM 显示写入并读回验证成功；
8. **进入游戏后客户端商店能看到对应 `[OP.GG] <英雄> ...` 推荐装备**；这是最终关键验收；
9. 再写另一英雄时，旧 FACM 文件可清理，但任何非 `facm1-*.json` 必须保留；
10. 离开 Champ Select / In Game 后不能新写，过程无明显卡顿。

实机明确通过后，再 fresh-check PR #103 exact latest HEAD / CI，Ready / merge，验证 main post-merge，再做 canonical closeout。**不因此自动发布新版本。**

## 最新完成扩展：Tools / Automation Gate 2

- Issue #96：completed。
- PR #97：merged。
- task branch：`feat/opgg-apply-gate2-96`（未删除）。
- 腾讯/国服最终候选 HEAD：`5472114145c7467db536c25ef8d7596ca0222cb5`。
- 候选 Windows Build #986：SUCCESS；UI Text #107：SUCCESS。
- Build #986 日志明确输出 `FACM performance contract smoke passed.`。
- artifact：`FACM-Windows-x64-986`，id `9226997388`，ZIP SHA-256 `49B3D0177471F9C44EA13316214643C903B38A7576EBDD34A74AFCFB6C85399B`。
- packaged FACM.exe SHA-256：`C041774343586D8A390DA86A19B654CB274BE8167179E8100BBA596F6801ED27`。
- 用户腾讯/国服实机反馈：**“经我测试 功能正常使用”**。
- 行为 merge commit：`67abfc0d9f4c3fced927f7888954f1948f77f945`。
- merge 后 main：UI Text #108 / Windows #987 SUCCESS。

### Gate 2 已验收冻结边界

- Gate 1 `OP.GG 对局助手` 仍是只读推荐；Gate 2 使用独立 `OP.GG 一键应用` 入口；
- 唯一 `LeagueClientModule + LeagueClientSessionProvider` 不变；
- 未点击按钮时 0 LCU writes；Yes / No 默认 No；
- 写前重新检查 phase / champion / queue；
- 召唤师技能保持可判断的 Flash D/F 槽位并 PATCH 后读回验证；
- 符文只有 `canAddCustomPage=true` 才创建新 `[FACM]` 页；满页直接跳过，**绝不覆盖用户已有符文页**；
- writer transport method + path allowlist 硬拒绝 ready-check、Champ Select actions 等越界写入；
- In Game 0 writes；不 auto accept / pick / ban / swap / reroll / dodge / skin；不聊天广播；不 overlay / 注入。

## 原 League 五阶段：5/5 DONE

1. **League Dashboard Gate 1 — DONE**
2. **Player Gate 1 — DONE**
3. **Champ Select / Current Game Gate 1 — DONE**
4. **Player Gate 2 — DONE**
5. **Tools / Automation Gate 1（OP.GG 对局助手只读推荐）— DONE**

原规划正式完成进度：**5/5 = 100%**。Gate 2 / Gate 3 是 5/5 之后的独立扩展，不改变原进度定义。

## 关键已完成行为基线

### Tools / Automation Gate 1

- Issue #93 completed；PR #94 merged。
- 最终行为候选 `3b3a3e3ddeeb3fb40fa86a9de4a440c42d34d66f`。
- merge commit `90b3c829aa8682f0d6be139512b348eb4f4aff78`。
- Build #974 腾讯/国服实机确认：`ChampSelect · 疾风剑豪 #157 · ranked / mid`、OP.GG Global 16.16、Tier / Win / Pick / Ban、召唤师技能、符文、出装、技能加点、Counter 正常。
- Gate 1 仍是只读推荐；后续 Gate 的写能力不改变 Gate 1 当时的只读验收事实。

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
- Gate 2 一键应用：无后台轮询、无自动触发；用户点击 + 确认后才串行 LCU write；In Game 0 writes。
- Gate 3 装备集：无后台磁盘工作；Prepare 0 filesystem mutation；只有用户确认后单次串行文件事务；In Game 0 disk writes。不能放宽既有 Performance Contract 数字预算。

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
- OP.GG 装备集：Build #996 CI / offline filesystem smoke 已通过，**腾讯 Recommended 路径和游戏内商店识别仍待实机**。
- 腾讯 match-history 的 `gameCount` 不作为账号全历史总数；分页按请求窗口实际返回数量判断。

## 下一步规则

当前唯一主线是 **Issue #99 / Draft PR #103 / Tools / Automation Gate 3**。

- 不在实机前合并；
- 不把 CI 绿当成腾讯客户端商店识别成功；
- 用户实机通过后按 exact HEAD / checks Ready + merge + main CI + canonical closeout；
- 失败则保持 Draft，只按真实路径/商店证据做 scoped fix；
- 不重开已经完成的五阶段和 Gate 2；
- 不自动发布新正式版，不删任务分支。
