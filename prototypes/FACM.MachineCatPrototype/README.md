# FACM Machine Cat Prototype — Gate 1

这是 Issue #33 的**独立桌宠原型**，只验证机器猫角色在原地时的视觉与动作质量。

## 当前边界

- 不接入 `FACM.exe`；
- 不修改 `FACM.PetHost.exe`；
- 不依赖或替换 `VPet-Simulator.Core`；
- 不做自动桌面漫游 / Gate 2；
- 不重新生成角色图片；
- 不复用 PR #13 的随机窗口平移路线。

## 三轮 Gate 1 结论

### 第一版：视觉失败

第一版使用程序绘制的 WPF 矢量机器猫。虽然 Release build、deterministic self-test、真实 WPF window smoke、自包含 publish 都通过，但用户实机录屏确认：角色与已经认可的圆润 2.5D 机器猫明显不一致，动作也有纸片变形感。

结论：**失败，不进入 Gate 2。**

### 第二版：外形正确，但整图 crossfade 失败

第二版直接使用用户已经认可的机器猫 Identity / Action Sheet 透明素材，角色外形方向恢复正确；但用户第二次真实 Windows 录屏清楚显示：

- Walk / Run 在整张动作图与镜像图之间 crossfade 时会出现双重角色/鬼影；
- Turn 多视角在交接 crossfade 时同样出现重影；
- 技术上“短时间淡化”仍然是两张完整角色同时显示，不能达到高质量桌宠要求。

结论：**外形基线保留，整图 alpha-crossfade 方案淘汰。**

### 第三版：当前候选

第三版继续保留已经确认的透明动作/视角素材，不再生成新图；但修改动画规则：

- 任意时刻屏幕上只允许绘制**一只**机器猫；
- Walk / Run 继续使用各自已确认动作姿势，左右换步只在步态中心点切镜像；
- 换步瞬间角色的水平/旋转偏移本身为 0，并做约 4.5%～6% 的极短水平收窄，随后立刻展开，用形变掩盖换面；
- Turn 使用 `正面 → 3/4 → 侧面 → 背面 → 镜像侧面 → 镜像3/4 → 正面`，每次视角切换先轻微水平收窄，再切图、展开；
- 状态之间的切换同样禁止 alpha-crossfade，只在最窄点切换视觉；
- Idle / Observe / Raised / Recover / Sleep 继续使用对应已确认素材并只做轻微 `translate / rotate / scale`；
- 11 个素材按需解码一次并缓存为冻结 `BitmapImage`。

曾尝试从已经扁平化的 Idle PNG 直接裁出头、手、脚做骨骼 Rig，但预演发现脚和身体已经烘焙在同一张位图，硬裁会破坏轮廓、重新产生纸片拼装感。因此该试验**没有作为测试包交给用户**。真正的分层骨骼方案必须有原始分层素材，不能从当前扁平成品 PNG 硬拆。

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

左键点击进入 `Observe`；按住移动超过约 6 DIP 才进入拖动 / `Raised`，松开进入 `Recover`。透明宿主区域继续通过 `WM_NCHITTEST -> HTTRANSPARENT` 做近似点击穿透。

## 第三版 Gate 1 验收重点

优先检查第二版视频暴露的具体问题：

1. Walk 连续观察数秒，不应再看到半透明双机器猫；
2. Run 连续观察数秒，不应再出现整图重影；
3. Turn 正面/3⁄4/侧面/背面交接时，任何帧都只能有一个清晰角色；
4. 换步或换视角时允许存在非常短的水平收窄，但不能像闪烁/消失；
5. Raised → Recover 与 Sleep 继续检查节奏和比例；
6. 角色外形必须继续保持第二版已经确认的 2.5D 质感。

如果第三版仍然有明显切图感或收窄感，继续留在 Gate 1 修；不因为“鬼影已经消失”就自动进入 Gate 2。

## 与 PR #13 旧 Sprite 的差异

PR #13 旧 Sprite 已经有 `Stopwatch + deltaTime`、透明窗口、多方向 Sprite 和速度平滑，所以这些本身不算新方案。

当前 Gate 1 的核心要求是：

- 已人工确认的角色视觉不能被实现阶段重新设计；
- 窗口不漫游时，动作本身先通过肉眼验收；
- 透明双图 crossfade 已被真实视频证伪并禁止；
- 自动化只证明工程没坏，视觉是否合格必须由用户真实 Windows 实机判断。

## Gate 2 边界

只有 Gate 1 被用户明确通过后，才实现：

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

`--self-test` 检查 11 个动作/视角资源全部嵌入并能解码、Run 节奏高于 Walk、Walk/Run/Turn 任何采样点都不存在第二个完整角色、状态切换不重新引入 ghost、Turn 覆盖多视角、姿态数值边界和 frame-gap clamp。

`--window-smoke-test` 会真正 Show 透明 WPF 窗口并确认至少收到 3 帧 `CompositionTarget.Rendering` 后自动关闭。

**CI 全绿仍不等于 Gate 1 视觉通过。**