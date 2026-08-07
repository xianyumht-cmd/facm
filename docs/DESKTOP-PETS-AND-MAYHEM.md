# FACM 开源 3D 桌面宠物与海斗排行榜

## 开源 3D 桌面宠物

FACM 不再自行绘制二维贴图或程序化低多边形模型。桌宠运行时改为：

- 引擎：Desktop Homunculus
- 渲染：Bevy / Vulkan / 真实 VRM 角色
- 角色格式：VRM
- 交互：透明桌面窗口、拖动、鼠标跟随、表情、动作和点击事件
- FACM 连接方式：本机 `127.0.0.1:3100` REST API 与 SSE 事件流

FACM 负责：

1. 查询并下载 Desktop Homunculus 官方 Windows x64 MSI；
2. 调用 MSI 完成安装；
3. 下载用户选择的 CC0 VRM 模型；
4. 通过 `/assets/import` 导入模型；
5. 创建 Persona，挂载 VRM 并启动角色；
6. 订阅 `pointer-click` 事件，点击桌宠后打开 FACM 控制面板。

首次使用需要下载约 200 MB 的引擎安装包和一个 VRM 模型。引擎安装后的实际占用可能约 600 MB。FACM.exe 保持较小，是因为引擎和模型由首次设置流程单独安装到运行环境，并非再次使用简易贴图替代。

内置 10 个 CC0 VRM 角色：

- 兔兔 Rabbit
- 泰迪 Teddy
- 蘑菇帽 Cappy
- 恐龙少年 DinoKid
- 酷外星人 CoolAlien
- 女巫 Witch
- 幽灵 Ghost
- 机甲伙伴 Polybot
- 宇航员 Astronaut
- 牛奶人 Milk

角色来自 Open Source Avatars 的 100Avatars R1 原创 CC0 集合，模型使用永久托管地址。FACM 会在界面显示原始名称和授权。

### 显卡兼容

Desktop Homunculus 当前仍处于 Alpha。部分 NVIDIA 设备若出现黑色背景，需要在 NVIDIA 控制面板中把 `Vulkan/OpenGL present method` 设置为 `Prefer native`。

### 面板主题

控制面板主题和桌宠角色继续使用独立设置：

```ini
ThemeId=glass-blue
PetStyleId=rabbit
```

`ThemeId` 只影响控制面板。`PetStyleId` 只决定 Desktop Homunculus 启动哪个 VRM Persona。

## 海斗排行榜

入口：控制面板底部“海斗排行”，或托盘右键菜单“海斗排行榜”。

支持英雄中文名、英文名和常用别名。

数据按字段明确分工：

- OP.GG：当前版本、梯队、技能加点、核心出装、强化符文；
- ARAMMayhem.com：胜率、选用率、名次、Mayhem 调整和当前版本总体胜率前十。

多个页面并行读取，总等待上限为 7 秒。窗口显示查询阶段、用时和进度，并支持主动取消。结果缓存 10 分钟。任一来源未返回的字段会明确显示“未返回”，不会生成估算数据。
