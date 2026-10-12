# FACM 在线状态

FACM 3.4.8 在控制中心增加“在线状态”入口，用于修改英雄联盟好友列表中展示的 presence 状态。

## 实现边界

- 读取：`GET /lol-chat/v1/me`
- 写入：`PUT /lol-chat/v1/me`
- 与现有 `LeagueClientModule` 共用同一个 `LeagueClientSessionProvider`，不新增第二套 LCU 发现、认证或连接。
- 写入器硬限制为上面的一个 PUT 地址；Gate 2、匹配、选人、Bench 等写边界不扩张。
- 每次用户点击最多发送一次 PUT；不会后台轮询重写，也不会使用代理、网络拦截或注入去维持伪装状态。
- 写入时先读取完整 presence 对象，只修改 `availability` 和 `lol.gameStatus`，其它字段原样保留。
- 写入后做两次短间隔只读验证。若客户端恢复了实际状态，FACM 显示“已被客户端覆盖”，不继续抢写。

## 用户状态

| FACM | availability | lol.gameStatus |
| --- | --- | --- |
| 在线 | `chat`（已使用 `online` 的客户端继续沿用 `online`） | `outOfGame` |
| 离开 | `away` | `outOfGame` |
| 勿扰 | `dnd` | `outOfGame` |
| 手机在线 | `mobile` | `outOfGame` |
| 隐身 | `offline` | `outOfGame` |
| 显示为游戏中 | `dnd` | `inGame` |

“隐身”和“显示为游戏中”能否长期保持由当前 League Client 决定；客户端阶段变化可能覆盖它们。FACM 的原则是读回确认、诚实显示，不和客户端进入持续写入竞争。

## 控制中心入口

控制中心功能区不再用三条横向卡片 + 箭头 + 单独的长“更多设置”按钮，而是改为五个桌面快捷方式：

- 修复工具
- 英雄联盟
- 在线状态
- 个性化
- 更多设置

图标区没有外层卡片表格；默认只显示图标和名称，悬停才出现轻量底色，同时复用原来的底部说明区。工作目录、清理环境和底部说明区位置保持不变。
