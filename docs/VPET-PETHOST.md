# FACM 高真实感桌宠：VPet PetHost

> 状态：技术预览 / 实机验收阶段。  
> 目标：替换“WinForms 透明窗口 + Sprite Sheet 随机平移”作为正式桌宠运行层，但在用户确认视觉与运动质量前保留旧 Sprite 引擎作为手动回退。

## 为什么拆成独立 PetHost

FACM 主程序目前仍是 .NET Framework 4.8 + WinForms，并承载清理工具、ToolBundle、在线管理、Mayhem 排行图片卡和 10 套控制面板主题。

桌宠的真实性需求与这些功能的技术需求不同。为了不让一次桌宠技术迁移同时影响整个 FACM，当前采用：

- `FACM.exe`：继续负责现有产品逻辑、设置、托盘、桌宠选择器和故障回退。
- `PetHost/FACM.PetHost.exe`：独立的 .NET 8 x64 WPF 进程，负责 VPet Core、透明桌宠窗口、动作状态和桌面移动。
- 双向命名管道：FACM 向 PetHost 发送启用、复位、退出等命令；PetHost 把单击、右键和运行状态回传给 FACM。
- 父进程守护：FACM 退出后 PetHost 自动退出，避免孤儿桌宠进程。

这让桌宠以后可以独立升级到新的渲染、动作或模型方案，而不需要迁移整个 FACM。

## 当前运行层

PetHost 使用：

- .NET 8 Windows / WPF；
- x64；
- `VPet-Simulator.Core` 1.1.0.66；
- WPF 无边框窗口 + DWM glass frame；
- PerMonitorV2 DPI；
- VPet 自己的 `GameCore`、`GraphCore`、`PetLoader`、`Main` 和 `IController` 接口；
- VPet 原生动作状态和移动配置，而不是 FACM 自己随机改变窗口速度。

当前首只技术预览角色重点验证：

1. Default / Idle；
2. Move；
3. Raised（长按后拖动/提起，保持 VPet 原交互）；
4. Touch Head / Touch Body；
5. StartUP；
6. 动画与位移同步；
7. 朝向与实际移动一致；
8. 连续运行无瞬时透明/闪烁；
9. 多显示器与高 DPI 下仍可复位；
10. 单击打开 FACM、右键显示 FACM 功能列表。

FACM 自己的左键/右键桥接只在 VPet 配置中的 TouchHead + TouchBody 区域生效，不再把整个透明 PetHost 窗口当成打开控制面板/功能列表的点击区域。VPet 自己的触摸、长按 Raised 等互动不因此改变。

## 动画资源策略

FACM 不把 VPet 默认角色的整套动画直接提交进本仓库，也不在安装包里重新分发整套资源。

首次选择“高精度桌宠 · VPet Core”时，PetHost 会从 VPet 官方 GitHub 仓库按需缓存当前首只角色的最小动作集：

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

这是 2026-05-19（`VPet-Simulator.Core` 1.1.0.66 发布当天）的默认动画更新提交。FACM 故意使用与稳定 Core 同代的动画快照，而不是把 5 月稳定 Core 与 8 月以后继续变化的动作定义混用。

## 便携数据目录

当前正式运行不会再把新的 VPet 资源/动画缓存写到 `%LOCALAPPDATA%`。FACM 启动 PetHost 时会显式传入便携数据目录：

`FACM\runtime\pethost\`

其中主要包含：

```text
FACM\
└─ runtime\
   └─ pethost\
      ├─ Assets\
      │  └─ vpet-ac77ba14\
      └─ Cache\
```

如果升级前已经存在 `%LOCALAPPDATA%\FACM\PetHost`，PetHost 会在首次启动时把旧数据复制到新的便携目录，逐文件检查长度后再尝试删除旧目录。迁移本身不是桌宠启动的硬依赖：旧缓存无法复制时，会直接在 `FACM\runtime\pethost` 重新准备资源，不会因为迁移失败让桌宠不可用。

海斗图片磁盘缓存也只写入 `FACM\runtime\cache\mayhem-images`；若 FACM 所在目录不可写，则放弃磁盘缓存而只使用内存，不再回退到 Windows TEMP。

## 首次加载与续传

第一次启用可能需要处理约 538 个动作资源文件。当前实现：

- 下载并发 20；
- 固定 partial staging，可从中断进度继续；
- 逐文件按 Git tree 大小校验；
- 单文件先写 `.download`，完成后原子替换；
- 恢复 VPet 官方桌面程序使用的动态 `PNGAnimation.MaxLoadMemory` 初始化；
- VPet `LoadALL()` 放到后台执行；
- 加载界面显示 `正在生成动作缓存 x/y` 的真实进度，而不是长时间只显示“正在建立动作状态机”。

下载前会读取官方 Git tree，校验文件数量和总大小；异常偏少、大小为 0 或超过安全上限时直接拒绝下载。只有所有清单文件成功写完且完成标记写入后，才切换成正式缓存目录。

## 许可证与来源

运行代码与动画资源必须分开看：

- VPet / VPet-Simulator.Core 代码：Apache-2.0；
- VPet 默认动画：VUP-Simulator 制作组的独立动画授权条款。

FACM 当前不是商业项目，本技术预览仅按上游明确允许的非商业条件使用这些动画，并向用户明确显示来源与项目链接。FACM 不出售这些动画，也不把它们重新授权成 FACM 自有素材。

如果 FACM 将来改成商业用途，必须在继续使用或分发这些默认动画前重新核对并履行上游商业授权要求。

上游：<https://github.com/LorisYounger/VPet>

PetHost 安装目录还会包含 `VPET-ASSET-NOTICE.txt`，首次资源准备界面也会显示来源。

## 与旧 Sprite 引擎的关系

这些文件目前继续保留：

- `SpritePetWindow.cs`
- `SpritePetAssetService.cs`
- 原 8 套 CC0 Sprite 素材元数据
- 旧 Sprite smoke test

但它们已经降级为回退/对照测试，不再是正式桌宠架构。VPet 技术预览启动失败时，FACM 不会偷偷用旧低清 Sprite 冒充成功；主程序会恢复默认悬浮球，让故障保持可见。

旧 Desktop Homunculus 兼容代码也不作为当前正式桌宠运行层。它可能探测历史外部安装位置，但当前 VPet PetHost 不依赖它，也不会把新的 VPet 数据写入其目录。

## CI

Windows CI 会：

1. 验证 FACM 旧 Sprite 回退链；
2. 使用 .NET 8 构建 `FACM.PetHost`；
3. 发布 `win-x64` self-contained 包；
4. 使用显式 `--data-root` 运行 `FACM.PetHost.exe --self-test`，避免自检依赖用户 `%LOCALAPPDATA%`；
5. 验证 VPet Core、控制器、x64 环境和 IPC；
6. 把完整 PetHost 目录打进 FACM ZIP；
7. 单独记录 `FACM.PetHost.exe` SHA-256。

CI 不把“能构建”当成视觉验收。真实感、动作自然度、透明窗口稳定性和交互命中范围仍必须由 Windows 实机运行确认。

## 实机验收

下载包含 `PetHost/` 目录的新构建后：

1. 启动 `FACM.exe`；
2. 托盘 → `桌面宠物`；
3. 选择列表第一项 `高精度桌宠 · VPet Core`；
4. 首次启用允许它联网缓存官方最小动作集；
5. 观察首次下载和 `动作缓存 x/y` 是否持续推进并最终自动显示桌宠；
6. 测试移动、停下、转向、长按拖动/提起、摸头/身体；
7. 测试人物附近透明区域左键/右键不再打开 FACM；
8. 右键打开 FACM 功能列表后，点击菜单外空白区域应自动关闭；
9. 检查 `FACM\runtime\pethost` 是否生成 Assets/Cache，新的 VPet 数据不应继续写入 `%LOCALAPPDATA%\FACM\PetHost`。

在第一只 VPet 桌宠实机验收完成前，不合并 PR #13。
