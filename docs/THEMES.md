# FACM 内置主题

FACM 内置 10 套主题。主题选择保存到程序目录的 `settings.ini`：

```ini
ThemeId=glass-blue
```

## 当前主题边界

主题不再只描述控制中心。`ThemeCatalog` 是唯一主题目录，`FacmThemeRuntime` 保存进程内当前主题，`FacmDesignSystem` 与 `FacmWindowChrome` 从同一组语义颜色读取外观。

FACM 自己拥有的 WinForms 界面应继承当前主题，包括控制中心、LOL 工作台、清理与修复、设置/工具页和 FACM 自绘临时窗口。切换主题时已打开的 FACM 窗口会刷新，新打开的窗口直接继承当前主题；Windows 文件选择器、UAC 等系统拥有的窗口继续使用系统外观。

语义颜色优先使用背景、Surface、Border、Accent、Text、Success、Warning、Error、Disabled 等角色，不应在新页面中复制另一套硬编码主题引擎。

## 主题列表

| ThemeId | 名称 | 界面尺寸 | 风格 |
|---|---|---:|---|
| `glass-blue` | 深海玻璃 | 430×700 | 蓝紫玻璃、柔光圆角 |
| `obsidian-gold` | 曜石鎏金 | 448×720 | 黑金金属、精致双线 |
| `neon-cyber` | 霓虹赛博 | 466×742 | 洋红青蓝、锐角 HUD |
| `cloud-light` | 云端浅色 | 438×706 | 清爽白蓝、柔和卡片 |
| `brutalist-grid` | 先锋构成 | 500×660 | 黑白蓝红、粗框大字 |
| `holo-spectrum` | 全息光谱 | 454×734 | 全息渐变、晶体面板 |
| `mono-emerald` | 墨绿极简 | 422×688 | 克制黑灰、细线绿光 |
| `rgb-tactical` | RGB 战术 | 474×748 | 电竞灯效、战术切角 |
| `aurora-night` | 极光夜幕 | 442×714 | 青紫极光、深夜氛围 |
| `sunset-synthwave` | 落日合成波 | 460×726 | 橙粉紫夜、复古未来 |

## 切换方式

打开控制中心 → **个性化** → **全局主题**，选择后点击 **应用主题**。当前进程中的 FACM 自有窗口会刷新，选择结果同时保存到 `settings.ini`，下次启动继续沿用。

主题和 `ui-text.ini` 的界面文字配置互不冲突。
