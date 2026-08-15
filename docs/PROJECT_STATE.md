# FACM 当前项目状态

> 2026-08-15：FACM 3.2.0 仍是当前正式生产基线。原 League 五阶段规划已经 **5/5（100%）DONE**；其后的 Tools / Automation Gate 2（OP.GG 一键应用符文 + 召唤师技能）也已完成腾讯/国服实机验收并合入 `main`。
>
> 当前有三个彼此隔离的后续工作面：**Gate 3（OP.GG 推荐装备集）**正在等待腾讯游戏内商店实机验收；**Gate 4（选人阶段自动应用 OP.GG 推荐）**作为 stacked Draft PR 基于 Gate 3，CI 候选已经完成，等待与 Gate 3 一起做腾讯实机；**Shell UX #104/#105**独立等待控制中心/二级菜单视觉与交互验收。不要把三者混成一个 PR，也不要重新设计已经验收通过的 Dashboard / Player / Live / Build Advisor / Gate 2 Apply。

## 当前正式版

- FACM 3.2.0 / GitHub Release `v3.2.0`
- 在线更新：enabled=true
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`D09BFBCD8F59FE026140B4CFD7BDCFC0002AD0AAF3E0C09E356B4AED61BFD6A9`
- 当前 Gate 3 / Gate 4 / Shell UX 都没有创建新 Release、Tag，没有修改 `online/version.json`，没有改变线上版本。

## 当前自动化候选：Tools / Automation Gate 4

- Issue #106：OPEN。
- Draft PR #107：OPEN / Draft；**当前 base 必须保持 `feat/opgg-itemsets-gate3-99`**，因为 Gate 4 组合 Gate 3 item-set service。
- branch：`feat/opgg-auto-apply-gate4-106`。
- Gate 3 dependency base：`c40d0799b0e36566af6e71d3d64abdad51f1a1b3`。
- final CI candidate HEAD：`ad2bb624d3371df1e50ee47d941ed7e361f61916`。
- UI Text Contract #134：SUCCESS。
- Windows Build #1013：SUCCESS。
- Mayhem Source Probe #253：SUCCESS。
- Windows #1013 日志明确输出 `FACM performance contract smoke passed.`。
- FACM.exe：3.2.0.0，build output size 78,152,704 bytes。
- signed FACM.exe SHA-256：`298B4C6E52782513E433F12CE7B6834C3619775C99D2655C48306C82A6FDEBC2`。
- artifact：`FACM-Windows-x64-1013`，artifact id `9233465608`，size 154,762,212 bytes。
- artifact ZIP SHA-256：`FF4C6A1D3A9C55CFF2F53D40A80A56449F047FC810107BC0C02724B4E2CC26B0`。
- GitHub artifact：`https://github.com/xianyumht-cmd/facm/actions/runs/31838665748/artifacts/9233465608`。
- artifact expires 2026-11-12；当前未过期。
- 完整交接：`docs/HANDOFF-20260815-TOOLS-GATE4.md`。
- **腾讯/国服实机通过前不 Ready、不合并。Gate 3 必须先验收/合并，再把 Gate 4 retarget 到 main 重跑 exact-head CI。**

### Gate 4 产品行为

Gate 4 不新增一级菜单，而是在现有 **`OP.GG 一键应用`** 窗口增加一个持久化总开关：

`选人时自动应用 OP.GG 推荐`

- 默认 OFF；OFF 时 Gate 2 已验收的手动一键流程完全不变；
- 开关状态存现有根目录 `settings.ini`：`LeagueAutoApplyRecommended=True/False`；
- `ui-text.ini` 仍只负责用户可见文字，不能保存行为状态；
- 旧 settings 没有新 key 时安全默认 false；
- 开关变化立即保存，重启 FACM 后继续保持；
- 开启后关闭一键应用窗口也继续工作，因为 controller 属于模块生命周期，不属于 Form 生命周期。

自动模式只在稳定的 Champ Select 上下文执行：

1. 复用现有全局 `LeagueGameflowMonitor -> PerformanceBudgetProvider` 阶段信号；
2. 只有全局预算已经进入 `champ-select`，Gate 4 才启动自身约 2s 的 Advisor 观察；
3. Desktop / Queueing / In Game 不发 Gate 4 自身的 Advisor / OP.GG 观察请求；
4. 当前 champion / queue / mode / position / version / recommendation 内容稳定至少 1.5s 后生成一次性 fingerprint；
5. 同一 fingerprint 最多自动尝试一次，重复轮询不会反复创建符文页或反复重写装备集；
6. 英雄或推荐上下文变化后重新稳定，才允许新 fingerprint 再执行一次；
7. partial / failed 对同一 fingerprint 不进入自动重试风暴，用户仍可手动一键应用；
8. 关闭开关立即清 pending，并取消正在进行的自动事务。

### Gate 4 自动事务与安全边界

一次稳定 fingerprint 自动事务：

1. 使用一份 structured OP.GG build payload；
2. 由已验收 Gate 2 parser 生成 rune + summoner spell plan；
3. 由 Gate 3 parser 生成 Recommended item-set plan；
4. Gate 2 先执行并按现有 phase / champion / queue 重检；
5. 如果 Gate 2 报 `blocked`，立即停止，不继续写磁盘；
6. 上下文仍有效时再执行 Gate 3 item-set transaction；
7. 最终只报真实的 success / partial / failed。

没有新增 LCU writer，也没有扩大 Gate 2 writer allowlist：

- 不 auto accept；
- 不自动 pick / ban / swap / reroll / dodge；
- 不改皮肤；
- 不发 Champ Select 聊天广播；
- 不 overlay / 注入；
- rune page full 仍 fail-closed，绝不覆盖用户已有符文页；
- Gate 3 仍只管理 `facm1-*` Recommended JSON，不删用户/第三方 JSON；
- In Game 自动观察/LCU write/disk write = 0。

### Gate 4 OP.GG 网络去重

Advisor 展示和自动执行需要同一 `/api/global/champions/...` structured payload。Gate 4 新增模块级 `CachingOpggBuildApi`：

- 复用现有 10 分钟 Build cache duration；
- 同一路径成功 payload 在 cache 内直接复用；
- cache miss 用单个 `SemaphoreSlim` 串行；
- 只有非空成功结果进入 cache；
- `LeagueBuildAdvisorDataService` 与 `LeagueAutoApplyExecutor` 共用同一 cache；
- module 是唯一 owner/disposer；
- deterministic smoke 明确证明同一路径连续两次只调用底层 OP.GG 一次，不同 path 才产生第二次调用。

因此首次进入选人时，Advisor 已拉到构筑数据后，随后自动应用通常直接消费同一份原始 payload，不再额外打同一个 OP.GG 构筑 endpoint。

### Gate 4 deterministic smoke

已接入 `PerformanceContractSmokeTest`，覆盖：

- legacy settings 默认 OFF；
- True / False settings.ini parse + serialization；
- 1.5s stability window；
- 同 fingerprint exactly once；
- 不产生 retry storm；
- champion change -> restabilize -> 一次新 apply；
- recommendation change -> restabilize -> 一次新 apply；
- disable 清 pending；re-enable 必须重新稳定；
- In Game / OP.GG unavailable 不 actionable；
- Desktop / Queueing / In Game global budget 不启 Gate 4 observer；只有 Champ Select 启用；
- PollInterval >= 2s；
- shared raw OP.GG same-path cache 去重；
- success / partial / failed 聚合真实性；
- Gate 4 scoped UI Text 默认文案非空；
- 既有 Gate 2 Performance smoke 继续硬拒绝 ready-check、Champ Select actions、query-string path bypass。

### Gate 4 CI 历史

- 第一轮 HEAD `8f4b08462bf63f957ccfccb7b3b17cbbafb4b9a9`：UI #133 SUCCESS；Windows #1012 在 Release compile 抓到 2 个 integration 问题：`FacmHostSmokeTest` 仍用旧 AdvisorModule constructor，以及 WinForms catch 派生/父类顺序错误。
- 同一分支已修正；host smoke 现在明确要求 `Settings + LeagueClient + Performance` 三项依赖。
- 随后再增加全局 Champ Select phase gate 与 shared raw OP.GG cache，并把两项都纳入 smoke。
- final HEAD `ad2bb624...`：UI #134 / Windows #1013 / Mayhem #253 全绿。
- 因 repository workflow 只对 `pull_request -> main` 触发，stacked PR #107 验证时曾**临时** retarget 到 main 以触发现有 workflow；run 启动后立即恢复 Gate 3 base。PR 全程 Draft，未合并。

### Gate 4 腾讯/国服验收

建议与 Gate 3 一起测：

1. 开关保持 OFF，进入 Champ Select，确认不会自动改任何东西；
2. 打开 `OP.GG 一键应用`，开启 `选人时自动应用 OP.GG 推荐`，然后可以关闭窗口；
3. 选/预选英雄并保持稳定，符文 + 召唤师技能 + 推荐装备应自动应用，不再弹 Yes / No；
4. 同一英雄保持不变时不能不停新建符文页/重复写装备集；
5. 换英雄后，稳定后只允许再自动应用一次；
6. Flash D/F 仍按 Gate 2 已验收规则保持；
7. rune page 满时只跳过符文，不覆盖已有页面；
8. 进入游戏后，商店应看到 Gate 3 `[OP.GG] ...` Recommended 装备集；
9. In Game 不再自动观察/写；
10. 关闭自动开关后，后续选人变化不再自动应用；
11. 手动 `预览并应用` 仍可独立工作；
12. 全程不应出现 auto ready / pick / ban / chat。

如果这次组合实机同时证明装备集能在腾讯游戏内商店显示，可以把同一证据作为 Gate 3 最后缺口。**先 close Gate 3 #99/#103，再把 Gate 4 #107 retarget 到 main、重新跑 exact-head CI，最后才 close Gate 4。**

## 当前进行中扩展：Tools / Automation Gate 3

- Issue #99：OPEN。
- Draft PR #103：OPEN / Draft；腾讯/国服真实商店识别验收前不 Ready、不合并。
- branch：`feat/opgg-itemsets-gate3-99`。
- base：`main` @ `641691108b8eca47c21c2b9b893c651f1ce957b7`。
- exact 行为候选 HEAD：`41110482986c9d562fba166b7472e1032027a95a`。
- latest Gate 3 docs HEAD / Gate 4 dependency base：`c40d0799b0e36566af6e71d3d64abdad51f1a1b3`。
- UI Text Contract #117：SUCCESS；Windows Build #996：SUCCESS。
- docs latest UI #119 / Windows #998：SUCCESS。
- Windows #996 日志明确输出 `FACM performance contract smoke passed.`。
- signed FACM.exe SHA-256：`C2E312D40C86B31339D7DB217937ACBD067BE89C3898C096DC7A92E52344F4A4`。
- artifact：`FACM-Windows-x64-996`，artifact id `9229662143`。
- artifact ZIP SHA-256：`18AD47E2B52036566CDBCC1F776B33F26EC39D16A3A50CFE82D401487082CE8C`。
- GitHub artifact：`https://github.com/xianyumht-cmd/facm/actions/runs/31828180073/artifacts/9229662143`。
- **CI / fake filesystem 不能替代腾讯客户端真实商店是否识别 Recommended JSON；当前仍待 Windows 腾讯/国服实机验收。**
- 完整交接：`docs/HANDOFF-20260815-TOOLS-GATE3.md`。

### Gate 3 已实现安全边界

- 复用 Build Advisor champion / mode / position / version context；
- 只有 ChampSelect + 用户确认才做手动磁盘事务；
- 写前重检 phase / champion / queue；
- GET `/data-store/v1/install-dir`；Tencent 必须确认 sibling `Game` 后才写 `../Game/Config/Global/Recommended`；
- standard Riot layout 写 `installDir/Config/Global/Recommended`；
- FACM own prefix 固定 `facm1-`；
- 新 JSON 内存生成 -> same-dir temp -> validate -> atomic move/replace -> destination readback；
- 新 destination 验证成功后才 best-effort 清其它旧 `facm1-*.json`；
- 不递归扫描，不删除任何非 `facm1-*.json`；
- rollback 失败时宁可保留 FACM private `.bak`；
- In Game 0 disk writes。

Gate 3 deterministic smoke 已覆盖路径、ownership、recipe restore、commit/readback、失败保旧、取消、phase/champion drift、user/third-party file 保留等。

## 独立 Shell UX 候选

- Issue #104 / Draft PR #105：Shell UX 收束；**不属于 Gate 3 / Gate 4 branch**。
- behavior candidate `6f3d8330127546327830048d06db89df0ae44a02`。
- UI Text #128 / Windows #1007 SUCCESS；Performance Contract PASS。
- latest docs head `e2ffa6f1702a37598b7629276c183b7499ac846c`；UI #130 / Windows #1009 SUCCESS。
- candidate artifact：`FACM-Windows-x64-1007` / id `9232122863`。
- tray root 固定为 5 个 novice-facing entries：打开控制中心 / 清理环境 / 英雄联盟 > / 更多 > / 退出程序；业务模块只能进入既有二级 group，不能再膨胀一级菜单。
- 控制中心用 progressive disclosure：目录状态 + 管理 / 清理环境主动作 / 修复工具 / 英雄联盟 / 个性化 / 更多设置。
- 尚待用户 Windows 视觉/交互验收；通过前保持 Draft。

## 最新完成扩展：Tools / Automation Gate 2

- Issue #96：completed；PR #97：merged。
- 腾讯/国服最终候选 HEAD：`5472114145c7467db536c25ef8d7596ca0222cb5`。
- Windows Build #986 / UI Text #107 SUCCESS，Performance Contract PASS。
- 用户腾讯/国服实机反馈：**“经我测试 功能正常使用”**。
- 行为 merge：`67abfc0d9f4c3fced927f7888954f1948f77f945`。
- merge 后 main：UI #108 / Windows #987 SUCCESS。
- final canonical main：`641691108b8eca47c21c2b9b893c651f1ce957b7`。

### Gate 2 已验收冻结边界

- Gate 1 `OP.GG 对局助手` 仍是只读推荐；Gate 2 为独立 `OP.GG 一键应用`；
- 唯一 `LeagueClientModule + LeagueClientSessionProvider` 不变；
- 手动模式未点击按钮 = 0 LCU writes；Yes / No 默认 No；
- 写前重检 phase / champion / queue；
- 召唤师技能保持 Flash D/F 并 PATCH 后读回；
- rune page 只有 `canAddCustomPage=true` 才创建新 `[FACM]` 页；满页直接跳过，绝不覆盖用户页面；
- LCU writer method+path allowlist 硬拒绝 ready-check、Champ Select actions 等越界写；
- In Game 0 writes；不 auto accept / pick / ban / swap / reroll / dodge / skin；不聊天；不 overlay / 注入。

## 原 League 五阶段：5/5 DONE

1. League Dashboard Gate 1 — DONE
2. Player Gate 1 — DONE
3. Champ Select / Current Game Gate 1 — DONE
4. Player Gate 2 — DONE
5. Tools / Automation Gate 1（OP.GG 对局助手只读推荐）— DONE

原规划正式完成进度：**5/5 = 100%**。Gate 2 / Gate 3 / Gate 4 是 5/5 之后的独立扩展，不改变原进度定义。

## 关键已完成行为基线

- Tools Gate 1：Issue #93 / PR #94 DONE；Build #974 腾讯实机通过 OP.GG Global 16.16 / ranked-mid / Tier/Win/Pick/Ban / spells/runes/items/skills/counter。
- Player Gate 2：Issue #90 / PR #91 DONE；Build #965 腾讯实机通过中文英雄名与当前已加载场次统计。
- Champ Select / Current Game：Issue #85 / PR #86 DONE；Build #955 腾讯实机通过；保持只读。
- Player Gate 1：Issue #81 / PR #82 DONE；Build #951 腾讯实机通过。
- Dashboard：腾讯实机 DONE。
- 单实例：Issue #53 / PR #54 DONE。

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

旧 Issue #33 / Draft PR #35 机器猫仍是暂停实验，不自动恢复。

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
- OP.GG 对局助手：窗口打开时工作；请求串行、同上下文缓存；In Game 不新增 OP.GG 请求。
- Gate 2 手动一键：无后台轮询；用户点击 + 确认后才串行 LCU write；In Game 0 writes。
- Gate 3 手动装备集：Prepare 0 filesystem mutation；用户确认后单次串行文件事务；In Game 0 disk writes。
- Gate 4 自动应用：默认 OFF；ON 时复用全局 Gameflow phase，仅 `champ-select` 进行约 2s 串行观察；稳定 fingerprint 最多一次 transaction；共享 10 分钟 OP.GG raw payload cache；Desktop/Queueing/InGame Gate4 observer 为 0；In Game 0 automatic writes。

## League / 腾讯国服已验证基线

- 所有 League 功能继续复用唯一 `LeagueClientModule`，禁止新增平行 LCU connector。
- discovery：进程路径 -> 同目录 Riot lockfile；活动 lockfile `FileShare.ReadWrite` + 短重试；WMI fallback 规则保持已验收版本。
- LCU 凭据只在内存使用，禁止日志/UI 输出。
- Akari 官网“不支持腾讯服务器”只视为免责声明；腾讯兼容按源码机制 + fixture + 实机功能判断。
- Dashboard / Player Gate1 / Live / Player Gate2 / OP.GG Advisor / Gate2 一键应用均已腾讯实机通过。
- Gate 3 Build #996：CI + offline filesystem smoke 通过，腾讯 Recommended 商店识别待实机。
- Gate 4 Build #1013：CI + deterministic auto smoke 通过，腾讯自动应用待实机。
- 腾讯 match-history `gameCount` 不作为全历史总数；分页按请求窗口实际返回判断。

## 下一步规则

当前执行顺序固定：

1. **腾讯实机优先测试 Gate 4 Build #1013**，因为该候选包含 Gate 3 item-set，可一次同时覆盖自动应用和 Gate 3 商店识别；
2. 若商店 `[OP.GG] ...` 推荐装备正常且自动应用行为正常，先 fresh-check / close Gate 3 #99/#103；
3. Gate 3 合并 main 后，把 #107 retarget 到 main，确认 diff 只剩 Gate 4，重跑 exact-head UI Text / Windows / Performance；
4. 用户对 Gate 4 明确通过后，Ready / merge #107，再验证 main CI 和 canonical closeout；
5. Shell UX #104/#105 继续独立验收，不能混入 Gate 3/4 closeout；
6. 任一实机失败则保持对应 PR Draft，只按真实证据 scoped fix；
7. 不自动发布新正式版，不删任务分支，不重开已经完成的五阶段/Gate2。
