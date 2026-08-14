# FACM Tools / Automation Gate 2 交接 — 2026-08-15

> 本文记录 5/5 主路线完成后的第一个独立扩展 Gate。当前状态以 GitHub 实时 Issue #96 / Draft PR #97 / `docs/PROJECT_STATE.md` 为准。

## 1. 已完成基线，不要重做

原 League 五阶段仍然是 **5/5 = 100% DONE**：

1. League Dashboard Gate 1
2. Player Gate 1
3. Champ Select / Current Game Gate 1
4. Player Gate 2
5. Tools / Automation Gate 1（OP.GG Build Advisor，只读）

当前 `main` 基线：`3517c80aaf514a5b8a8f3ad84658bd958c7e5b43`。

生产仍是 FACM 3.2.0 / `force_update=false`。本 Gate 不创建 Release、Tag，不修改 online update。

旧 Issue #33 / Draft PR #35 机器猫仍暂停，不是本 Gate 的工作。

## 2. 当前任务

- Issue #96：`Tools / Automation Gate 2：OP.GG 一键应用符文 + 召唤师技能（显式确认）`
- Draft PR #97：同一 Gate
- branch：`feat/opgg-apply-gate2-96`
- base：`main@3517c80aaf514a5b8a8f3ad84658bd958c7e5b43`
- 腾讯/国服实机通过前必须保持 Draft。

Gate 2 不是把 Gate 1 从“只读”改写成“原来其实会写”。产品语义是：

- Gate 1 仍是已验收并冻结的只读推荐；
- Gate 2 新增独立入口 `OP.GG 一键应用`；
- 用户必须主动进入新入口、点击按钮、再次确认，之后才允许写 LCU。

## 3. League Akari fresh source evidence

本轮参考 League Akari `dev`：

`cb236b6caf196e2505c7dfa6b34185020fd1e570`

### 3.1 召唤师技能

Akari `src/renderer/src-opgg-window/opgg/utils/loadout.ts` + `src/shared/http-api-axios-helper/league-client/champ-select.ts`：

- GET `/lol-champ-select/v1/session/my-selection`
- PATCH `/lol-champ-select/v1/session/my-selection`
- body 使用 `spell1Id` / `spell2Id`
- Akari 也会考虑 Flash D/F 偏好。

FACM Gate 2 采用相同 endpoint contract，但只有用户确认后才 PATCH，并且 PATCH 后再次 GET 精确验证。

### 3.2 符文

Akari `perks.ts` / `loadout.ts`：

- GET `/lol-perks/v1/inventory`
- POST `/lol-perks/v1/pages/`
- GET `/lol-perks/v1/pages`
- PUT `/lol-perks/v1/pages/{id}`
- PUT `/lol-perks/v1/currentpage`

当前 OP.GG rune build 字段（`src/shared/types/opgg/index.ts`）：

- `primary_page_id`
- `secondary_page_id`
- `primary_rune_ids`
- `secondary_rune_ids`
- `stat_mod_ids`

旧 `rune_pages` shape 仍可通过内部 `builds` 兼容。

### 3.3 FACM 与 Akari 的关键安全差异

Akari 在 `canAddCustomPage=false` 时会读取已有符文页并更新第一页。

**FACM Gate 2 明确不这么做。**

FACM 规则：

- `canAddCustomPage=true`：只创建新的 `[FACM]` page；
- `canAddCustomPage=false`：直接跳过符文；
- 不 GET 用户现有 pages 来寻找覆盖目标；
- 不 PUT 任意已有用户 page；
- 召唤师技能可以作为独立子操作继续执行；最终结果只能报 partial。

这是 deliberate fail-closed product decision，不是缺功能。

### 3.4 装备集为什么不在本 Gate

Akari `writeItemSetsToDisk` 不是单纯 LCU PUT：

1. GET `/data-store/v1/install-dir`
2. 非腾讯写 `installDir/Config/Global/Recommended`
3. Tencent 写 `installDir/../Game/Config/Global/Recommended`
4. 管理自身前缀 `.json` 文件并删除旧项
5. 还存在 recipe item restore 映射。

因此装备集涉及：腾讯路径解析、文件原子写、失败恢复、旧文件删除范围、客户端读文件时序等独立风险。

**结论：装备集必须另建 Gate。当前 #96/#97 不允许顺手加入磁盘写入。**

## 4. FACM 当前实现

### 4.1 唯一 League session / connector 不变

新增：

- `src/FACM/League/LeagueClientWriteRuntime.cs`

`LeagueClientModule` 仍只创建一个 `LeagueClientSessionProvider`：

- `LeagueClientApiClient` 用它读；
- `LeagueClientWriteApiClient` 用同一个 provider 写；
- 没有第二套 process/lockfile/WMI discovery；
- 没有第二套 token 生命周期。

写 transport 只允许：POST / PUT / PATCH。

LCU password/token 只进入 Basic Auth header，不允许写日志或 UI。

### 4.2 Apply model/service

新增：

- `LeagueBuildApplyModels.cs`
- `LeagueBuildApplyService.cs`

Prepare 与 Apply 分开：

**PrepareAsync**

- 只读；
- 必须基于当前 accepted Build Advisor snapshot；
- 只有用户点击 apply 入口后才重新读取 exact OP.GG structured spell/rune IDs；
- 4 秒 linked timeout；
- 不产生任何 LCU write。

**ApplyAsync**

- caller 必须已经拿到用户 Yes 确认；
- 首先重新读取 Gameflow / Champ Select；
- 必须仍是 ChampSelect；
- champion 必须与预览 plan 相同；
- queue 在两侧均可判断时必须相同；
- 不满足即 blocked，零写。

### 4.3 符文写链

1. GET inventory
2. `canAddCustomPage=false` → skip，绝不覆盖旧页
3. POST 新 `[FACM] <champion> - <position>` page
4. PUT 新 page 的 style / selected perk IDs
5. PUT currentpage
6. GET pages
7. 按新 page id + primary/sub style + selected perks 做读回验证

创建成功但后续 update/select 失败时：

- 不伪报成功；
- 可能留下 FACM 自己刚创建但未完成的 page；
- 当前不猜测 DELETE endpoint 做自动回滚，因为未对腾讯实机验证删除语义；
- 这比覆盖/删除用户已有 page 更安全。

### 4.4 召唤师技能写链

1. GET current `my-selection`
2. 从当前 D/F 位置判断 Flash 偏好
3. 需要时交换推荐 pair，让 Flash 保持原槽位
4. PATCH spell1Id / spell2Id
5. GET `my-selection`
6. 精确读回验证

如果本来已经是目标值，不发 PATCH，但仍读回验证并记为 `already-set`。

### 4.5 UI

新增：

- `LeagueBuildApplyForm.cs`
- `LeagueBuildApplyUiBridge.cs`
- `LeagueBuildApplyUiTextKeys.cs`

托盘顺序：放在现有 `OP.GG 对局助手` 之后。

窗口：

- 显示当前 context；
- 显示现有只读推荐中的召唤师技能/符文；
- `预览并应用` 按钮只在 ChampSelect + recommendation ready 时可用；
- 点击后重新准备 structured IDs；
- MessageBox Yes/No，默认 No；
- 选 No 零写；
- 所有异常 containment 在新窗口内，不让 async WinForms event 未处理异常冒泡；
- FormClosed 取消 lifetime token。

新增静态可见文字全部进入 `UiTextCatalog`；UI Text Contract 已连续通过 #100 / #101，后续以最新 HEAD CI 为准。

## 5. Deterministic smoke

`LeagueBuildApplySmokeTest` 已接入 `PerformanceContractSmokeTest`。

覆盖：

- Prepare = 0 writes
- 当前 OP.GG rune style / rune / stat mod parse
- success write contract：POST page / PUT page / PUT currentpage / PATCH spells
- Flash-on-D 保持
- Flash-on-F 保持
- rune inventory full → no page GET/write/overwrite
- rune skip + spell success → partial
- InProgress / 非 ChampSelect → zero writes
- champion drift → zero writes
- spell write failure + rune success → partial
- cancellation → zero writes
- 不触碰 actions / reroll / skin 等禁止 endpoint

Windows #980 日志已经实际输出：

`FACM performance contract smoke passed.`

所以不是“测试文件写了但没跑”。

## 6. CI 历史

### 第一轮

HEAD：`bce6b2ce409c0c04992058b4379ce88c762b933c`

- UI Text #100 SUCCESS
- Windows #979 FAILURE

唯一 Release compile error：

`LeagueBuildApplyUiBridge.cs` 中 `ContextMenuStrip` 被 FACM 自有同名类型遮蔽，编译器把参数解释成 `FACM.ContextMenuStrip`，而 tray 实际返回 `System.Windows.Forms.ContextMenuStrip`。

scoped fix：显式使用 `System.Windows.Forms.ContextMenuStrip`。

不要把 #979 误判为腾讯 LCU write 或 OP.GG 失败。

### 第二轮

HEAD：`689c3e8afc2dab3c14c843f36a6dc687e8393b3c`

- UI Text #101 SUCCESS
- Windows #980 SUCCESS
- Performance Contract smoke PASS
- FACM.exe 3.2.0.0
- artifact `FACM-Windows-x64-980`
- artifact id `9226698823`
- artifact ZIP digest `39BACEAC1FB90B83E5D5583C88F111CF8BC8FB46012098A3D113CEB398144F1A`

之后同一分支继续做了 UI fail containment 和 canonical docs；**不要默认 #980 是最终实机候选，必须看 PR #97 最新 exact HEAD / CI。**

## 7. Windows 腾讯/国服实机验收清单

等最新 HEAD CI 全绿后才发 artifact。

### A. Gate 1 回归

- 原 `OP.GG 对局助手` 仍能在 Champ Select 显示正确 champion / mode / position / OP.GG Global version / recommendations；
- 不因 writer 加入而影响原只读页性能、缓存或请求。

### B. 未确认绝对零写

- Lobby 打开 `OP.GG 一键应用` 不写；
- Champ Select 推荐 ready，但什么都不点，不写；
- 点 `预览并应用` 后出现确认框，选“否”，客户端符文/技能完全不变化。

### C. 确认写入

建议测试前在客户端确认当前 Flash 在 D 还是 F，并记住位置。

- 选“是”；
- 有空自定义符文页时出现新的 `[FACM]` page；
- page 内容与推荐一致；
- current page 切到新 FACM page；
- summoner spells 与推荐一致；
- Flash 保持原 D/F 偏好，不无故互换；
- 窗口结果与客户端真实结果一致。

### D. 容量满 fail-closed

这项只有方便测试时做，不要求为了测试故意破坏现有配置。

- 自定义符文页容量满时，FACM 应显示跳过符文；
- 不覆盖任意旧 page；
- spell 仍可单独应用；
- 结果为 partial，而不是 success。

### E. stale context

- 打开确认框后，如果英雄/阶段已经变化，再确认时应该 blocked；
- 不把旧英雄 loadout 写给新英雄；
- In Game 不允许写。

### F. 性能

- 选英雄/切英雄/输入/锁定无明显新增卡顿；
- Gate 2 没有后台轮询；
- 只有显式点击才进行额外 OP.GG + LCU write/read-back chain；
- 关闭窗口立即取消。

## 8. 通过后的正确收口顺序

用户实机明确反馈正常后：

1. fresh-check `main`；
2. fresh-check Issue #96 / Draft PR #97；
3. 确认 exact PR HEAD 没移动；
4. 确认该 exact HEAD Windows + UI Text 全绿；
5. 把 PR 从 Draft 改 Ready；
6. 使用 expected HEAD SHA 合并；
7. Issue #96 completed；
8. 等 main post-merge CI；
9. 更新 `PROJECT_STATE.md` / canonical closeout；
10. 原 5/5 仍保持 100%，只把 Gate 2 标 DONE；
11. **不自动 Release / Tag / online update**；
12. **不删除分支，除非用户明确授权**。

## 9. 有 bug 时的处理原则

保持 Draft，不推翻 Gate 2 设计。按类型 scoped fix：

- 腾讯 endpoint/body shape
- OP.GG rune shape
- Flash slot semantics
- perk inventory/page capacity
- read-back verification
- stale Champ Select context
- UI confirmation/result text
- cancellation/performance

不要因为一个腾讯字段差异就：

- 新建第二套 LCU connector；
- 自动写；
- 覆盖用户现有符文页；
- 加装备集磁盘写；
- 加 auto accept/pick/ban；
- 重做 Gate 1 Build Advisor；
- 恢复机器猫；
- 发布新版本。
