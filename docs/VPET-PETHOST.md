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
3. Raised（拖动/提起）；
4. Touch Head / Touch Body；
5. StartUP；
6. 动画与位移同步；
7. 朝向与实际移动一致；
8. 连续运行无瞬时透明/闪烁；
9. 多显示器与高 DPI 下仍可复位；
10. 单击打开 FACM、右键显示 FACM 托盘菜单。

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

`6a6da4089e0706d8f0c61714f3c071fb2a2c268f`

缓存目录：

`%LOCALAPPDATA%\FACM\PetHost\Assets\vpet-6a6da408\`

下载使用随机 staging 目录。只有所有清单文件成功写完且完成标记写入后，才原子切换成正式缓存目录。下载中断、进程被关闭或网络失败不会把半套资源当成可用缓存。

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

但它们已经降级为：

- 回退；
- 对照测试；
- 第二阶段蜘蛛/飞虫方向验证的参考。

它们不再是正式桌宠架构。VPet 技术预览启动失败时，FACM 不会偷偷用旧低清 Sprite 冒充成功；主程序会恢复默认悬浮球，让故障保持可见。

## CI

Windows CI 分成两条桌宠验证：

### FACM / 旧 Sprite 回退

仍运行 `--animal-pet-test`，确保原 8 套 CC0 Sprite 回退链没有被新运行层破坏。

### FACM.PetHost

CI 会：

1. 使用 .NET 8 构建 `FACM.PetHost`；
2. 发布 `win-x64` self-contained 包；
3. 运行 `FACM.PetHost.exe --self-test`；
4. 验证 VPet Core 可以加载、控制器实现正确、x64 环境正确、IPC 协议可用；
5. 把完整 PetHost 目录打进 FACM ZIP；
6. 单独记录 `FACM.PetHost.exe` SHA-256。

CI 不把“能构建”当成视觉验收。真实感、动作自然度、透明窗口稳定性仍必须由 Windows 实机运行确认。

## 实机验收

下载包含 `PetHost/` 目录的新构建后：

1. 启动 `FACM.exe`；
2. 托盘 → `桌面宠物`；
3. 选择列表第一项 `高精度桌宠 · VPet Core`；
4. 首次启用允许它联网缓存官方最小动作集；
5. 连续观察至少数分钟；
6. 测试移动、停下、转向、拖动/提起、摸头/身体、单击、右键和“宠物复位”；
7. 重点观察是否还有瞬间消失、倒着走、动画与位移脱节、明显像图片滑动等旧问题。

只有实机认可第一只 VPet 桌宠后，才继续做第二只蜘蛛/苍蝇等方向明显的宠物，并最终清理旧 `SpritePetWindow` 正式路径。
