# FACM Machine Cat 3D Prototype — Visual + Desktop Motion Gate

这是 Issue #33 / Draft PR #35 的独立桌宠验证工程，不接 FACM/PetHost，不替换现有 VPet。

## 已确认的失败路线

真实 Windows 录屏已经确认：

1. 程序重画矢量角色：外形失败；
2. 整图 crossfade：出现双角色鬼影；
3. 单图镜像/切姿势：无鬼影但动作仍像切图片；
4. 扁平 PNG 局部位移场：手脚会拉长/变胖，产生明显橡皮感；
5. 第一只 3D GLB 虽然证明了 Viewport3D、刚性零件关节和连续 3D Turn 技术可行，但用户实机明确判定 **外观非常丑**；
6. 之前“窗口固定、只原地踏步”的 Gate 设计也被用户否决：桌宠必须真正改变 Windows 窗口位置，才能评价走路是否可信。

因此当前门槛改为：

> **外观合格 + 真正在桌面移动 + 步态与实际速度同步**，三项必须一起验收。

## 当前 3D 模型的定位

用户自行下载并提供的 `664230004_doraemon_model.glb` / FBX 已验证：

- GLB 2.0；
- 29 个独立 Mesh；
- 61 个 Node；
- `skins = 0`；
- `animations = 0`；
- 原始 FBX 也未检出 Skin / Cluster / AnimationStack；
- 头、眼、鼻、嘴、胡须、项圈、铃铛、身体、口袋、左右上臂/小臂/手、左右腿/脚、尾巴已经拆成独立 Mesh。

它让我们验证了“刚性分件 3D Rig”是可行的，但 **已经不再是视觉候选角色**。不要继续为它调材质、灯光、脸型或细节。

## 第三方模型边界

**模型文件不进入本仓库，不进入 GitHub Actions artifact。**

`.gitignore` 排除 `*.glb / *.gltf / *.fbx / *.zip`，CI 发布前检查 artifact 中不存在这些格式。公开 artifact 只有 runtime；本地测试者自行放入合法取得的模型。

## 当前技术路线

- .NET 8 WPF；
- 内置 `Viewport3D`；
- 极窄 GLB 2.0 Loader；
- 旧模型用刚性零件关节验证 shoulder → forearm → hand、hip → foot、head/face、bell、tail；
- 不 crossfade、不镜像整图、不做位图 warp；
- 3D Turn 使用连续旋转；
- **新增 `DesktopMotionController`，窗口位置本身使用 deltaTime 真正移动。**

### DesktopMotionController

默认启动即进入 AUTO：

- 角色位于 `SystemParameters.WorkArea` 底部的地面线；
- 在屏幕左右选择有最短距离约束的目标点；
- Walk 约 92 px/s，Run 约 168 px/s；
- 使用加速度渐进到目标速度，不瞬移；
- 距目标越近越减速；
- 到达后停留一小段时间，再决定下一目标；
- 需要反向时先减速、连续转身，再移动；
- 目标点永远在工作区内，边缘不直接反弹；
- 产品行为中长距离约 30% 概率进入 Run；
- 动画状态由**实际速度**决定：加速阶段先 Walk，超过阈值才 Run。

自动 self-test 在 1920×1040 工作区模拟 45 秒，必须：

- 累计窗口实际位移 ≥ 900 px；
- 始终处于工作区；
- 始终保持地面线；
- 覆盖 Walk / Turn / Run。

## 操作

- 默认：AUTO 真正桌面巡走
- `A`：开关自动巡走
- `1`：暂停自动并手动 Idle
- `2`：暂停自动并手动 Walk 动作
- `3`：暂停自动并手动 Run 动作
- `4`：暂停自动并手动 360° Turn 演示
- `D` / `F1`：调试信息
- `Esc`：退出
- 鼠标拖动窗口：暂停 AUTO；按 `A` 恢复

## 当前验收顺序

1. **先换掉已经判丑的 3D 模型。**
2. 新模型必须先看外观，不再默认相信网页“rigged / animated”描述；实际下载文件优先。
3. 新模型加载成功后，同时看它在 Windows 桌面真实移动时的 Walk / Run / Turn。
4. 只有外观、实际移动、动作同步三项同时合格，才讨论接 FACM/PetHost。

PR #35 继续保持 Draft。
