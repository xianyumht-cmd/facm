# FACM 高真实感桌宠：VPet PetHost

> 状态：FACM 3.1 正式桌宠运行层。  
> 目标：使用独立 .NET 8 / WPF / VPet Core 宿主实现高精度桌宠，同时让 .NET Framework 4.8 的 FACM 主程序保持稳定、流畅、可回退。

## 架构边界

FACM 主程序仍是 .NET Framework 4.8 + WinForms，负责清理工具、ToolBundle、在线管理、海斗排行、控制中心、托盘和桌宠选择器。

桌宠运行层单独使用 `FACM.PetHost.exe`：

- `FACM.exe`：产品主进程；
- `FACM.PetHost.exe`：独立 .NET 8 x64 WPF 子进程，负责 VPet Core、透明窗口、动作状态和桌面移动；
- 双向命名管道：FACM 发送启用、复位、退出，PetHost 回传单击、右键、ready/error；
- Windows Job Object：PetHost 启动后由 FACM 尝试加入带 `KILL_ON_JOB_CLOSE` 的 Job；
- `--parent-pid`：PetHost 自己每 2 秒检查一次父进程，作为 Job Object 无法分配时的兼容兜底；
- PetHost 启动失败、IPC 断开或宿主意外退出时，FACM 自动恢复默认悬浮球。

### 为什么不是单 PID

FACM 主程序和 PetHost 使用不同 CLR/UI 技术栈：net48 WinForms 与 net8 WPF。为了让任务管理器只显示一个 PID，必须把主程序整体迁到现代 .NET 并重新整合 WPF/VPet，这会同时影响清理提权、更新器、ToolBundle、WinForms UI、签名和现有 smoke。

正式发布前不做这种高风险迁移。当前目标是**一个 FACM 主进程管理一棵受控子进程树**：保留 VPet 崩溃隔离，同时保证 FACM 退出时 PetHost 不成为孤儿进程。

未来如果 FACM 主程序整体迁到 .NET 8，可以再评估把 WPF 桌宠组件同进程托管；这属于架构版本升级，不属于 3.1 发布前修补。

## UI 线程与启动性能

PetHost 启动链包含大包检查/释放、进程创建和最长 7 秒的命名管道连接。它们不能同步运行在 WinForms UI 线程。

当前 `VPetHostClient.Activate` 只在 UI 线程登记回调与当前 pet id，然后把启动链提交到后台任务：

1. 定位或释放当前 FACM.exe 内嵌的精确 PetHost bundle；
2. 创建便携数据目录；
3. 启动 `FACM.PetHost.exe --pipe ... --parent-pid ...`；
4. 尝试把新进程加入 FACM Job Object；
5. 在后台连接 named pipe；
6. 连接成功后发送最新 pet id 并启动事件读取；
7. 失败时切回 UI context 恢复默认悬浮球。

`Stop` 也不再让 UI 线程等待 WPF 最长 1.2 秒退出；发送 stop 后由后台完成等待/强制结束。

## 控制中心交互

从 VPet 点击打开控制中心时，Windows 的前台激活规则可能拒绝 FACM 立即抢焦点，因此不能只依赖 `CompactMenuForm.Deactivate` 判断“用户点到了空白处”。

控制中心现在同时保留：

- 正常获得焦点时的 `Deactivate` 自动关闭；
- 一个按物理左键边沿触发的 outside-click watcher；
- watcher 先等待打开面板的那次左键完全释放，再武装下一次点击，避免 PetHost 上报 IPC 点击后面板刚打开就被同一次按键关闭；
- 打开文件夹选择器、消息框等内部对话流程时，沿用 `_dialogOpen` 防止误关父面板。

## PetHost 如何交付

运行时是独立子进程，但正式发布只需要一个 `FACM.exe`。

构建顺序：

1. `dotnet publish` 生成 win-x64 self-contained PetHost；
2. 运行 `FACM.PetHost.exe --self-test`；
3. 将完整 publish 目录压缩为 `PetHostBundle.zip`；
4. 以 `FACM.Resources.PetHost.zip` 嵌入 `FACM.exe`；
5. 构建后的 `FACM.exe --embedded-pethost-test` 再释放并启动内嵌 PetHost 自检。

正式运行查找顺序：

1. 当前 `FACM.exe` 自身内嵌的 `FACM.Resources.PetHost.zip`；
2. 仅当前构建没有内嵌资源时，兼容应用目录历史/开发用 `PetHost\FACM.PetHost.exe`；
3. 最后探测开发构建目录。

内嵌宿主释放到：

`FACM\runtime\pethost-host\<FACM-MVID>\`

MVID 绑定当前 FACM 精确构建内容，避免单 EXE 在线更新后误用旧 sidecar PetHost。

## 当前运行层

PetHost 使用：

- .NET 8 Windows / WPF；
- x64；
- `VPet-Simulator.Core` 1.1.0.66；
- WPF 无边框透明桌面窗口；
- PerMonitorV2 DPI；
- VPet 自己的 `GameCore`、`GraphCore`、`PetLoader`、`Main` 和 `IController`。

正式 VPet 角色重点覆盖 Default / Idle、Move、Raised、Touch Head、Touch Body、StartUP、方向与位移同步、多显示器复位，以及左键/右键到 FACM 的桥接。

## 动画资源与便携目录

FACM 不把 VPet 默认角色整套动画提交进仓库或直接重分发在安装包里。首次使用按需缓存上游最小动作集，固定到上游提交：

`ac77ba144ed39f61624d93542c008b38be4d85aa`

便携目录：

```text
FACM\
└─ runtime\
   ├─ pethost-host\<FACM-MVID>\   # self-contained 程序宿主
   └─ pethost\                     # 动作资源与生成缓存
      ├─ Assets\vpet-ac77ba14\
      └─ Cache\
```

首次加载会处理约 538 个动作资源文件，使用并发下载、固定 partial staging、逐文件大小校验、`.download` 原子替换和完成标记复核。缓存残缺时完成标记会失效并重新准备。

## 许可证与来源

- VPet / VPet-Simulator.Core 代码：Apache-2.0；
- VPet 默认动画：VUP-Simulator 制作组的独立动画授权条款。

FACM 使用这些动画前必须继续遵守上游授权；如果项目商业化，应重新核对并履行相应商业授权要求。

上游：<https://github.com/LorisYounger/VPet>

PetHost publish 中保留 `VPET-ASSET-NOTICE.txt`。

## CI 与实机验收

Windows CI 验证 PetHost publish/self-test、完整 ZIP 内嵌、FACM 释放内嵌包和启动自检、最终资源与打包。CI 能证明构建/交付/启动链，但透明窗口、真实交互、outside-click、Job Object 在用户桌面环境中的行为仍需 Windows 实机验收。

发布前重点测试：

1. 从可写空目录只运行一个 `FACM.exe`；
2. 首次选 VPet 时控制中心不应因解包/pipe connect 假死；
3. 点击 VPet 能打开控制中心，再点击屏幕空白处应收起；
4. 在任务管理器结束 `FACM.PetHost.exe`，默认悬浮球应恢复；
5. 正常退出 FACM 后，不应残留 PetHost；
6. 若能测试强制结束 FACM，PetHost 也应被 Job/父进程守护清理；
7. `runtime\pethost-host\<FACM-MVID>` 与 `runtime\pethost` 按预期生成。
