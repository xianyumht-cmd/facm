# FACM Machine Cat Prototype — Gate 1

这是 Issue #33 的**独立桌宠原型**，只验证机器猫角色在原地时的视觉与动作质量。

## 当前边界

- 不接入 `FACM.exe`；
- 不修改 `FACM.PetHost.exe`；
- 不依赖或替换 `VPet-Simulator.Core`；
- 不做自动桌面漫游 / Gate 2；
- 不重新生成角色图片；
- 不复用 PR #13 的随机窗口平移路线。

## Gate 1 第一次失败与本轮修正

PR #35 第一版使用程序绘制的 WPF 矢量机器猫。它通过了 Release build、动画自检和真实 WPF window smoke，但用户实机视频确认：角色外观与已经认可的圆润 2.5D 机器猫差距过大，动作也有明显“纸片变形”感。因此第一版被判定为 **Gate 1 视觉失败**，没有进入 Gate 2。

本轮第二版的原则是：**程序不得重新设计或重画已经确认的角色视觉。**

当前角色素材直接来自本次设计过程中用户已经认可的机器猫 Identity / Action Sheet，并提取为透明动作素材；没有再次调用图片生成。程序只负责：

- 动作状态和节奏；
- `deltaTime`；
- 轻微位移 / 缩放 / 旋转；
- 短时交叉淡化；
- 鼠标观察、点击、拖动；
- 状态切换和窗口生命周期。

Prototype 暂时把透明 PNG 以 `Assets/*.b64` 嵌入程序集，启动时只解码一次并冻结/缓存为 `BitmapImage`。这是原型期资源存储方式，不代表正式集成必须继续使用 Base64。

## 第二版技术路线

```text
CompositionTarget.Rendering
        │
        ├─ Stopwatch / deltaTime（frame gap 最大 50ms）
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
        ▼
RigPose
        │  asset / mirror / short crossfade
        │  translate / rotate / scale / shadow
        ▼
MachineCatRig
        ├─ approved transparent Image A
        ├─ approved transparent Image B
        └─ shadow
```

没有传统 `frameIndex = time * fixedFps` 的 Sprite Sheet 播放器。

### Walk / Run

Walk 和 Run 使用各自已经确认的动作姿势。为了形成左右步态，动作在原图和镜像之间切换，但大部分周期只显示一个清晰轮廓；仅在换步瞬间短暂 crossfade，避免第一版实验中容易出现的双重残影。Run 的节奏明显快于 Walk。

### Turn

Turn 不再把一张正面图直接旋转或瞬间左右镜像，而使用：

`正面 → 3/4 → 侧面 → 背面 → 镜像侧面 → 镜像3/4 → 正面`

各视角大部分时间保持清晰，只在视角交接末段短暂 crossfade。

## 操作

启动后角色固定在主屏幕右下角，不会自己巡航。

| 按键 | 状态 |
| --- | --- |
| `1` | Idle |
| `2` | Walk |
| `3` | Run |
| `4` | Turn |
| `5` | Observe |
| `6` | Raised |
| `7` | Recover，约 1.45 秒后回 Idle |
| `8` | Sleep |
| `Space` | 下一个状态 |
| `D` / `F1` | 调试文字 |
| `Esc` | 退出 |

左键点击进入 `Observe`；按住移动超过约 6 DIP 才进入拖动 / `Raised`，松开进入 `Recover`。透明宿主区域仍通过 `WM_NCHITTEST -> HTTRANSPARENT` 做近似点击穿透。

## Gate 1 第二版验收重点

这轮首先看两件事：

1. **角色外形是否终于与用户已经认可的机器猫质感一致，而不是程序重画的廉价替身。**
2. 在保持角色视觉的前提下，Idle / Walk / Run / Turn / Observe / Raised / Recover / Sleep 的节奏、切换和轻微二级运动是否自然。

不要评价桌面漫游轨迹，因为 Gate 1 故意没有 MotionController。

如果第二版仍然看起来像切图片、鬼影明显、比例跳动或动作不自然，继续留在 Gate 1 修，不进入 Gate 2。

## 与 PR #13 旧 Sprite 的差异

PR #13 旧 Sprite 已经有 `Stopwatch + deltaTime`、透明窗口、多方向 Sprite 和速度平滑，所以这些本身不算新方案。

当前 Gate 1 的区别是：

- 先锁定并保留已经人工认可的角色视觉；
- 不用窗口漫游掩盖原地动作问题；
- Walk / Run / Turn / Raised / Recover / Sleep 是独立行为状态；
- Turn 有独立多视角素材，而不是撞边后换方向或瞬间翻面；
- 自动化只证明程序没坏，**视觉是否合格必须由用户实机判断**。

## Gate 2 边界

只有 Gate 1 被用户明确通过后，才会实现：

```text
BehaviorController（5~10Hz）
        ↓
MotionController（deltaTime）
        ├─ position / velocity
        ├─ desiredVelocity
        ├─ acceleration / deceleration
        ├─ heading / targetHeading
        ├─ angularVelocity
        ├─ arrival
        └─ edge steering
        ↓
AnimationController
        └─ actualSpeed → gait / Walk / Run
```

硬规则仍是：Random 只选行为/目标；不撞墙反弹；速度接近 0 不走路；大角度转向先减速/Turn；Gate 2 先用调试图形验轨迹。

## 自动验证

```powershell
dotnet build .\prototypes\FACM.MachineCatPrototype\FACM.MachineCatPrototype.csproj -c Release
.\prototypes\FACM.MachineCatPrototype\bin\Release\net8.0-windows\FACM.MachineCatPrototype.exe --self-test
.\prototypes\FACM.MachineCatPrototype\bin\Release\net8.0-windows\FACM.MachineCatPrototype.exe --window-smoke-test
```

`--self-test` 还会检查 11 个认可动作/视角资源全部嵌入并能解码、Run 节奏高于 Walk、Turn 覆盖多视角、姿态数值边界和 frame-gap clamp。

`--window-smoke-test` 会真正 Show 透明 WPF 窗口并确认至少收到 3 帧 `CompositionTarget.Rendering` 后自动关闭。

**CI 全绿仍不等于 Gate 1 视觉通过。**
