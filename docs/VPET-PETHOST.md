# FACM 高真实感桌宠：VPet PetHost

> 状态：FACM 3.1 正式桌宠运行层。  
> 目标：使用独立 .NET 8 / WPF / VPet Core 宿主实现高精度桌宠，同时让 .NET Framework 4.8 的 FACM 主程序保持稳定、可回退。

## 架构

FACM 主程序仍是 .NET Framework 4.8 + WinForms，负责清理工具、ToolBundle、在线管理、海斗排行、控制面板、托盘和桌宠选择器。

桌宠运行层单独使用 `FACM.PetHost.exe`：

- `FACM.exe`：负责产品逻辑、设置、托盘、桌宠选择器和故障恢复；
- `FACM.PetHost.exe`：独立 .NET 8 x64 WPF 进程，负责 VPet Core、透明桌宠窗口、动作状态和桌面移动；
- 双向命名管道：FACM 发送启用、复位、退出等命令，PetHost 回传单击、右键、ready/error 等状态；
- 父进程守护：FACM 退出后 PetHost 自动退出；
- PetHost 启动失败、IPC 断开或宿主意外退出时，FACM 自动恢复默认悬浮球，不再让桌面入口一起消失。

## PetHost 如何交付

运行时仍是独立进程，但正式发布不再要求用户手工携带 `PetHost/` 目录。

正式构建顺序：

1. `dotnet publish` 生成 win-x64 self-contained PetHost；
2. 运行 `FACM.PetHost.exe --self-test`；
3. 将完整 publish 目录压缩为 `PetHostBundle.zip`；
4. 把 ZIP 以 `FACM.Resources.PetHost.zip` 嵌入 `FACM.exe`；
5. 构建后的 `FACM.exe` 再执行 `--embedded-pethost-test`，由 FACM 自己释放内嵌包并启动释放后的 PetHost 自检。

正式运行时，FACM 查找顺序为：

1. 当前 `FACM.exe` 自身内嵌的 `FACM.Resources.PetHost.zip`；
2. 仅当当前构建没有内嵌资源时，才兼容应用目录下历史/开发用的 `PetHost\FACM.PetHost.exe`；
3. 最后才探测开发构建目录中的 PetHost。

这个顺序是升级兼容的必要条件：旧版完整包可能在应用目录留下旧 sidecar，但新版单 EXE 在线更新后必须优先运行当前 EXE 自带的匹配 PetHost，不能被旧 sidecar 覆盖。

内嵌包释放到：

`FACM\runtime\pethost-host\<FACM-MVID>\`

`<FACM-MVID>` 来自当前 `FACM.exe` 的精确构建内容。换版本或换内嵌 PetHost 后会进入新的宿主目录，避免“主程序已经更新、却继续启动旧 PetHost”的错配。

这也解决了旧版在线更新器只能下载一个 `FACM.exe` 的兼容问题：新 EXE 本身已经携带匹配的 PetHost，因此旧客户端升级后无需再补 sidecar 文件。

## 当前运行层

PetHost 使用：

- .NET 8 Windows / WPF；
- x64；
- `VPet-Simulator.Core` 1.1.0.66；
- WPF 无边框窗口 + DWM glass frame；
- PerMonitorV2 DPI；
- VPet 自己的 `GameCore`、`GraphCore`、`PetLoader`、`Main` 和 `IController` 接口；
- VPet 原生动作状态和移动配置，而不是 FACM 自己随机改变窗口速度。

正式 VPet 角色重点覆盖：Default / Idle、Move、Raised、Touch Head、Touch Body、StartUP、方向与位移同步、多显示器复位，以及左键/右键到 FACM 的桥接。

FACM 的左键/右键桥接只在 VPet 配置中的 TouchHead + TouchBody 区域生效，不把整个透明 PetHost 窗口当成控制面板点击区域。

## 动画资源策略

FACM 不把 VPet 默认角色的整套动画提交进本仓库，也不在安装包里重新分发整套动画资源。

首次选择“高精度桌宠 · VPet Core”时，PetHost 从 VPet 官方 GitHub 仓库按需缓存最小动作集：

- `vup.lps`
- `vup/Default/`
- `vup/IDEL/`
- `vup/MOVE/`
- `vup/Raise/`
- `vup/StartUP/`
- `vup/Touch_Body/`
- `vup/Touch_Head/`

资源固定到上游提交：

`ac77ba144ed39f61624d93542c008b38be4d85aa`

这是 2026-05-19、`VPet-Simulator.Core` 1.1.0.66 发布当天的默认动画更新提交。FACM 使用与稳定 Core 同代的动画快照，不把稳定 Core 与后续变化的动作定义混用。

## 便携数据目录

新的 VPet 数据不写 `%LOCALAPPDATA%`，FACM 启动 PetHost 时显式传入：

`FACM\runtime\pethost\`

其中主要包含：

```text
FACM\
└─ runtime\
   ├─ pethost-host\
   │  └─ <FACM-MVID>\
   │     └─ FACM.PetHost.exe + self-contained runtime
   └─ pethost\
      ├─ Assets\
      │  └─ vpet-ac77ba14\
      └─ Cache\
```

`pethost-host` 是程序运行宿主；`pethost` 是动作资源和生成缓存。两者职责不同。

如果升级前存在 `%LOCALAPPDATA%\FACM\PetHost`，PetHost 会在首次启动时复制旧数据到新的便携缓存目录，逐文件检查长度后再尝试删除旧目录。迁移失败不会阻止桌宠启动，会直接在新目录重新准备资源。

## 首次加载与续传

第一次启用可能需要处理约 538 个动作资源文件。当前实现：

- 下载并发 20；
- 固定 partial staging，可从中断进度继续；
- 逐文件按 Git tree 大小校验；
- 单文件先写 `.download`，完成后原子替换；
- 动作缓存完成标记会再次核对固定 commit、关键目录、文件数量和总字节；
- 完成标记存在但缓存已经残缺时，会使标记失效并重新准备资源；
- VPet `LoadALL()` 后台执行并显示真实进度。

下载前还会校验官方 Git tree 的文件数量和总大小；异常偏少、大小为 0 或超过安全上限时直接拒绝继续。

## 许可证与来源

运行代码与动画资源必须分开看：

- VPet / VPet-Simulator.Core 代码：Apache-2.0；
- VPet 默认动画：VUP-Simulator 制作组的独立动画授权条款。

FACM 使用这些动画前必须继续遵守上游授权；如果项目商业化，应重新核对并履行对应商业授权要求。

上游：<https://github.com/LorisYounger/VPet>

PetHost publish 中保留 `VPET-ASSET-NOTICE.txt`，首次资源准备界面也显示来源。

## 与旧 Sprite 引擎的关系

旧 `SpritePetWindow` / `SpritePetAssetService` 和原 CC0 Sprite 素材仍作为回退/对照代码存在，但不再是正式桌宠运行层。

VPet 启动失败时，FACM 不会偷偷用低清 Sprite 冒充成功；主程序会恢复默认悬浮球并保留可诊断错误。

旧 Desktop Homunculus 兼容代码也不作为当前正式运行层，只保留历史安装位置探测兼容。

## CI 保证

Windows CI 会验证：

1. 工具输入及 CleanupProfile 状态；
2. PetHost win-x64 self-contained publish；
3. 独立 PetHost `--self-test`；
4. 完整 PetHost publish ZIP 的生成；
5. `FACM.Resources.PetHost.zip` 确实嵌入 `FACM.exe`；
6. `FACM.exe --embedded-pethost-test` 能安全释放该 ZIP，并启动释放后的 PetHost 自检；
7. FACM 其它已有 smoke tests、资源校验和下载包生成。

CI 能证明构建/交付/启动链，但桌宠视觉自然度、透明窗口表现和交互命中范围仍需要 Windows 实机验收。

## 实机验收

新的正式构建只需要 `FACM.exe` 即可验证完整桌宠交付链：

1. 把 `FACM.exe` 放到一个新的可写空目录；
2. 启动 FACM；
3. 托盘或控制中心 → `桌面宠物`；
4. 选择 `高精度桌宠 · VPet Core`；
5. 检查 `runtime\pethost-host\<FACM-MVID>` 是否自动出现并包含 `FACM.PetHost.exe`；
6. 首次启用允许它联网缓存官方最小动作集；
7. 测试移动、停下、转向、长按拖动/提起、摸头/身体；
8. 测试 PetHost 被手工结束后 FACM 默认悬浮球是否自动恢复；
9. 检查 `runtime\pethost` 是否生成 Assets/Cache，新的 VPet 数据不应继续写入 `%LOCALAPPDATA%\FACM\PetHost`。
