# FACM Tools / Automation Gate 3 交接 — 2026-08-15

> 当前任务：Issue #99 / Draft PR #103 / branch `feat/opgg-itemsets-gate3-99`。原 League 五阶段仍为 5/5 = 100% DONE，Tools / Automation Gate 2 也已完成。Gate 3 是新的独立扩展，未经过腾讯/国服真实商店识别前不得 Ready / merge。

## 1. 当前正式基线

- `main`：`641691108b8eca47c21c2b9b893c651f1ce957b7`
- 正式 Release：FACM 3.2.0 / `v3.2.0`
- `force_update=false`
- 当前任务没有 Release / Tag / online update 授权。
- 旧 Issue #33 / Draft PR #35 机器猫仍暂停，不属于本任务。

## 2. Gate 3 目标

增加独立入口 **`OP.GG 装备集`**：

1. 继续用已验收的 OP.GG Build Advisor 上下文识别 champion / mode / position / version；
2. 只有 Champ Select + 用户点击后才重新获取 structured item IDs；
3. 再经过 Yes / No 二次确认，默认 No；
4. Yes 后才读取 `/data-store/v1/install-dir` 并写 League `Recommended` JSON；
5. 写后读回验证；
6. 客户端商店实际识别，才算腾讯/国服通过。

Gate 3 不改变 Gate 2 的符文 / 召唤师技能行为。

## 3. League Akari fresh source evidence

本轮参考 League Akari `dev`：

`cb236b6caf196e2505c7dfa6b34185020fd1e570`

`src/renderer/src-opgg-window/opgg/utils/loadout.ts`：

- `starter_items`：最多 3 组；
- `boots`：合并一组；
- `prism_items`：合并一组；
- `core_items`：最多 4 组；
- `last_items`：合并一组；
- item-set JSON 使用 `uid/title/sortrank/type/map/mode/blocks/associatedChampions/associatedMaps/preferredItemSlots`；
- `type=global`、`map=any`、`mode=any`；
- item id 在写出前经过 recipe restore。

Akari 当前 recipe restore：

- 3042 → 3004；223042 → 223004；323042 → 323004
- 3040 → 3003；223040 → 223003；323040 → 323003
- 3121 → 3119；223121 → 223119；323121 → 323119
- 2530 → 2526；222530 → 222526；322530 → 322526

Akari 主进程 `writeItemSetsToDisk`：

- GET `/data-store/v1/install-dir`
- 标准 Riot：`installDir/Config/Global/Recommended`
- Tencent：`installDir/../Game/Config/Global/Recommended`
- Akari 只管理自己的 `akari1` 前缀文件。

## 4. FACM 比 Akari 更保守的文件安全语义

FACM Gate 3 固定 own prefix：`facm1-`。

- 不接受 OP.GG 返回的文件名或任意路径；文件名只由 FACM 自己构造。
- install-dir 必须 rooted + existing。
- 当 install-dir leaf 为 `LeagueClient` 时，只有确认存在 sibling `Game` 目录，才使用 Tencent `Game/Config/Global/Recommended`；否则 fail-closed。
- 其它 layout 才走标准 `installDir/Config/Global/Recommended`。
- 不递归扫描，不删除任何非 `facm1-*.json`。
- 新 JSON 先完整生成到内存。
- 同目录写 `.facm1-<guid>.tmp`，先读回验证 temp，再原子 move / replace。
- destination 提交后再读回验证 uid/title/blocks/items。
- 只有新 destination 验证成功，才 best-effort 清理旧 `facm1-*.json`。
- replace 使用 private `.bak`；如果回滚本身失败，宁可保留 `.bak` 作为恢复证据，也不盲删。
- durable commit 完成后，cleanup 失败只报 warning，不能把已经成功的写入误报成“零写取消”。

禁止：

- 不自动写；
- 不 auto accept / pick / ban / swap / reroll / dodge / skin；
- 不聊天广播；
- 不 overlay / 注入；
- In Game 零磁盘写；
- 不新增第二套 League discovery / auth；
- 不删用户或第三方 Recommended JSON。

## 5. 当前实现文件

新增：

- `src/FACM/League/LeagueItemSetModels.cs`
- `src/FACM/League/LeagueItemSetService.cs`
- `src/FACM/League/LeagueItemSetForm.cs`
- `src/FACM/League/LeagueItemSetUiBridge.cs`
- `src/FACM/League/LeagueItemSetUiTextKeys.cs`
- `src/FACM/League/LeagueItemSetUiTextSmokeTest.cs`
- `src/FACM/League/LeagueItemSetSmokeTest.cs`

修改：

- `src/FACM/Application/Modules/LeagueBuildAdvisorModule.cs`
- `src/FACM/League/LeagueAdvisorText.cs`
- `src/FACM/Performance/PerformanceContractSmokeTest.cs`

仍复用现有 `LeagueBuildAdvisorModule` 与唯一 `LeagueClientModule`，没有新增第二套 connector。

## 6. Deterministic smoke

Gate 3 smoke 已接入 Performance Contract，覆盖：

- Prepare 解析 OP.GG starter/boots/prism/core/last，且 0 filesystem mutation；
- ranked / 157 / mid 复用已验收 Build Advisor context；
- recipe restore；
- Tencent sibling `Game` path；
- standard Riot path；
- relative / missing / broken Tencent layout fail-closed；
- happy path temp → atomic commit → destination read-back verify；
- user.json / third-party.json 不删不改；
- superseded `facm1-*.json` 仅在新文件成功后清理；
- InProgress / champion drift = 0 disk write；
- forced commit failure 保留旧 FACM + user file；
- caller cancellation before commit = 0 disk mutation；
- tray reflection contract；
- 21 个 Gate 3 scoped UI Text Key 都有非空默认文案。

## 7. CI 历史

### Windows #995

失败原因只在 deterministic fake：`Task<byte[]>` 方法直接返回 `byte[]`，产生 5 个 CS0029。运行时 Gate 3 代码没有被报告其它编译错误。

随后在同一分支把 fake 修正并精简。

### 当前行为候选

Exact behavior HEAD：

`41110482986c9d562fba166b7472e1032027a95a`

- UI Text Contract #117：SUCCESS
- Windows Build #996：SUCCESS
- Windows log 明确输出：`FACM performance contract smoke passed.`
- FACM.exe：3.2.0.0
- signed FACM.exe SHA-256：`C2E312D40C86B31339D7DB217937ACBD067BE89C3898C096DC7A92E52344F4A4`
- artifact：`FACM-Windows-x64-996`
- artifact id：`9229662143`
- artifact size：154,746,197 bytes
- artifact ZIP SHA-256：`18AD47E2B52036566CDBCC1F776B33F26EC39D16A3A50CFE82D401487082CE8C`
- GitHub 下载：`https://github.com/xianyumht-cmd/facm/actions/runs/31828180073/artifacts/9229662143`

后续 docs-only commit 不改变这个行为候选。判断程序行为仍以 `411104... / Build #996` 为准。

## 8. Windows 腾讯/国服验收清单

1. 原 `OP.GG 对局助手` 仍正常。
2. Gate 2 `OP.GG 一键应用` 符文 + 技能仍正常。
3. 托盘新增 `OP.GG 装备集`。
4. Champ Select 能显示当前英雄 / mode / position 与装备预览。
5. 未点击写入时，不生成 `facm1-*.json`。
6. 点 `预览并写入` 后先弹 Yes / No；选 No 必须零写。
7. 选 Yes 后，腾讯正常 layout 应写到类似：`E:\WeGameApps\英雄联盟\Game\Config\Global\Recommended\facm1-....json`。
8. FACM 状态显示“写入并读回验证”成功。
9. 真正进入游戏后打开商店，能看到对应 `[OP.GG] <英雄> ...` 推荐装备；**这是 CI 无法替代的关键验收**。
10. 如果再次对另一个英雄写入，旧 FACM 文件可被清理，但任何非 `facm1-*.json` 必须保留。
11. 离开 Champ Select / In Game 后不能发起新磁盘写入；客户端过程无明显卡顿。

无需为了测试故意制造破坏性场景；重点确认真实国服路径和商店识别。

## 9. 用户回来后的 continuation

如果用户明确说“正常 / 没问题 / 功能正常”：

1. fresh-check PR #103 exact latest HEAD / CI；
2. 记录 Build #996 腾讯 acceptance；
3. PR Ready for Review；
4. 精确 SHA merge；
5. 验证 main post-merge UI Text / Windows；
6. Issue #99 completed；
7. canonical closeout；
8. 不发布 FACM 3.2.x，不改 online config；
9. 不删除 task branch，除非用户明确要求。

如果失败：

- PR #103 保持 Draft；
- 只根据真实路径 / 商店识别证据做 scoped fix；
- 不顺手重写已验收 Gate 1 / Gate 2。
