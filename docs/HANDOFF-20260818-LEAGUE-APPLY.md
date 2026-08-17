# FACM League 一键应用实机回归 — 2026-08-18

## 实机结论

FACM 3.4.1 已确认修复腾讯 League 游戏前台全局快捷键：日志出现 `source=async-key-state`，一键退出游戏可用。

同一轮实机测试中，推荐中心的一键应用仍异常。日志能确认装备集成功写入 `Game/Config/Global/Recommended`，但 3.4.1 Gate 2 对符文 / 召唤师技能缺少成功、跳过、blocked 等终态日志，因此不能从旧日志精确区分具体早退分支。

## 高概率结构性问题

Gate 2 旧设计在每次符文应用时都新建 `[FACM]` 自定义符文页；当 `canAddCustomPage=false` 时直接跳过符文。重复实机测试会由 FACM 自己逐渐吃满自定义页容量，之后符文应用永久进入 `no-capacity`，除非用户手工清页。

## 本轮修复原则

- 优先复用与当前英雄/位置同名的 `[FACM]` 页，避免每次应用都泄漏一个新页。
- 容量已满时，只允许复用名称以 `[FACM]` 开头的 FACM 自有页。
- 绝不覆盖普通用户符文页；没有 FACM 自有页时仍 fail-closed。
- 保留创建新页路径，前提是 LCU 明确允许新增自定义页。
- Gate 2 增加 prepare / blocked / rune / spell 最终结果日志，后续一次实机日志即可定位真实分支。
- 保留 3.4.1 的 settled read-back：LCU 2xx 本身仍不算成功。
