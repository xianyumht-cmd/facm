# FACM Machine Cat Prototype — Gate 1

这是 Issue #33 下一阶段的**独立桌宠原型**，用于验证机器猫角色的原地动作是否自然。

当前明确不做：

- 不接入 `FACM.exe`；
- 不修改 `FACM.PetHost.exe`；
- 不依赖 `VPet-Simulator.Core`；
- 不做自动桌面漫游；
- 不重新生成角色图片；
- 不复用 PR #13 的 `SpritePetWindow` 随机窗口平移路线。

原型全部使用 WPF 矢量分层绘制角色，不提交第三方角色图片资源。这样 Gate 1 可以只评估“动作系统本身”，不被图片切帧质量或窗口漫游掩盖。

## 技术路线

```text
CompositionTarget.Rendering
        │
        ├─ Stopwatch / deltaTime（frame gap 最大 50ms）
        │
        ▼
MachineCatAnimator
        │
        ├─ Idle
        ├─ Walk
        ├─ Run
        ├─ Turn
        ├─ Observe
        ├─ Raised
        ├─ Recover
        └─ Sleep
        │
        ▼
RigPose（连续参数）
        │
        ▼
MachineCatRig
   ├─ root/body transform
   ├─ head transform
   ├─ left/right arms
   ├─ left/right legs
   ├─ pupils / blink
   ├─ mouth
   ├─ bell
   └─ shadow
```

这里没有 `frameIndex = time * fixedFps`。动作由连续相位和状态参数驱动，状态切换使用约 180ms 平滑过渡。

## Gate 1 操作

启动后角色默认固定在主屏幕右下角，不会自己巡航。

| 按键 | 状态 |
| --- | --- |
| `1` | Idle 待机 |
| `2` | Walk 行走原地动作 |
| `3` | Run 跑步原地动作 |
| `4` | Turn 转身原地动作 |
| `5` | Observe 观察鼠标 |
| `6` | Raised 举起状态 |
| `7` | Recover 落地恢复，约 1.45 秒后自动回 Idle |
| `8` | Sleep 睡眠 |
| `Space` | 切到下一状态 |
| `D` / `F1` | 显示/隐藏调试状态文字 |
| `Esc` | 退出 |

左键点击角色会进入 `Observe`。按住后移动超过约 6 DIP 才进入拖动/`Raised`，松开进入 `Recover`；普通点击和拖动不会混为一谈。

透明区域通过 `WM_NCHITTEST -> HTTRANSPARENT` 尽量让点击穿透，Gate 1 使用近似角色包围区，后续正式接入前再升级为更精细的 alpha/geometry hit test。

## Gate 1 验收重点

不要评价桌面移动轨迹，因为 Gate 1 故意没有自动移动。

只看：

1. Idle 是否有轻微呼吸、眨眼、头部微动，而不是静态图片；
2. Walk 手脚是否交替，身体是否有克制的重心/上下变化；
3. Run 是否明显比 Walk 更快、更有压缩和腾跃感；
4. Turn 是否通过连续旋转和支撑腿变化完成，而不是瞬间镜像；
5. Observe 是否平滑看向鼠标；
6. Raised 是否有悬空摆动，而不是只移动窗口；
7. Recover 是否有落地压缩和阻尼恢复；
8. Sleep 是否闭眼、慢呼吸，并降低动画更新频率。

如果这些原地动作不自然，**停止在 Gate 1 修动作，不进入 Gate 2。**

## 与 PR #13 旧 Sprite 的本质区别

PR #13 的旧 Sprite 已经有 `Stopwatch + deltaTime`、速度平滑、多方向 Sprite、透明窗口等，因此这些不能当成新方案的创新点。

当前 Gate 1 的差异是：

- 不使用现成多方向 Sprite Sheet；
- 不用固定 FPS 逐帧播放；
- 不通过移动窗口制造“角色在动”的错觉；
- 状态直接输出连续身体/四肢/眼神参数；
- `Walk / Run / Turn / Raised / Recover / Sleep` 是不同运动模型；
- 先验收原地角色动作，再进入运动轨迹设计。

## Gate 2 的 MotionController 设计边界

Gate 1 通过后才实现，不提前混进当前原型。

计划的数据流：

```text
BehaviorController（5~10Hz）
        │ 只决定“想做什么 / 去哪里”
        ▼
MotionController（deltaTime）
        ├─ position
        ├─ velocity
        ├─ desiredVelocity
        ├─ acceleration / deceleration
        ├─ heading / targetHeading
        ├─ angularVelocity
        ├─ arrival
        └─ edge steering
        │
        ▼
AnimationController
        └─ actualSpeed -> gait frequency / Walk / Run
```

硬规则：

- Random 只能选行为/目标，不能每帧直接改位置；
- 不允许 `vx = -vx` / `vy = -vy` 的撞墙反弹作为正式边缘行为；
- 实际速度接近 0 时不能继续播放走路；
- 大角度转向先减速、Turn，再重新加速；
- Gate 2 先用调试图形验轨迹，不让角色美术掩盖屏保感。

## 本地构建

需要 Windows + .NET 8 SDK：

```powershell
dotnet build .\prototypes\FACM.MachineCatPrototype\FACM.MachineCatPrototype.csproj -c Release
```

动画数学自检：

```powershell
.\prototypes\FACM.MachineCatPrototype\bin\Release\net8.0-windows\FACM.MachineCatPrototype.exe --self-test
```

真实窗口 smoke：

```powershell
.\prototypes\FACM.MachineCatPrototype\bin\Release\net8.0-windows\FACM.MachineCatPrototype.exe --window-smoke-test
```

它会实际 `Show()` 一个 Walk 状态透明 WPF 窗口，确认 `Loaded` 后收到至少 3 帧 `CompositionTarget.Rendering`，再自动关闭；超过 3 秒未满足会返回非 0，并写 `machine-cat-window-smoke-error.txt`。

直接指定初始状态：

```powershell
.\prototypes\FACM.MachineCatPrototype\bin\Release\net8.0-windows\FACM.MachineCatPrototype.exe --state Observe
```

`--self-test` 会对 8 个状态按 120Hz 采样 6 秒，检查姿态参数有限/边界、Run 与 Walk 动态差异、Sleep 闭眼、Observe 眼神映射和 deltaTime frame-gap clamp。

## 当前完成定义

Gate 1 的自动化层面需要：

- `dotnet build -c Release` 通过；
- `--self-test` 返回 0；
- `--window-smoke-test` 返回 0；
- win-x64 self-contained publish 成功；
- CI 上传独立原型 artifact。

**视觉 Gate 1 仍必须在真实 Windows 桌面由用户看实际窗口。** CI 可以证明工程、动画数学和 WPF 窗口运行链没有坏，但不能替代“动作看起来是否自然”的人工验收。
