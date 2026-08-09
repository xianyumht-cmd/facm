# FACM 当前项目状态

> 2026-08-10：正式 3.1.1 保持稳定；Issue #33 / Draft PR #35 已停止继续调 2D PNG，当前转入独立 **机器猫 3D Gate 1** 实机验收。

## 当前正式版

- 版本：FACM 3.1.1。
- GitHub Release：v3.1.1。
- 在线更新：已启用。
- `minimum_version=3.0.0`。
- `force_update=false`。
- 3.1.1 发布基础 main：`e53c45773b224e4d8f670f44381e394457fdf660`。
- 3.1.1 发布元数据提交：`04f7cbae702d6dd136ab278f72938cff2a8c26ef`。
- 3.1.1 在线更新启用提交：`de632e2832e6d227aa570082601b33ed8f99a0b9`。

## 已验证完成

- FACM 3.1 的发布前稳定性收口已完成：海斗多源容灾、当前平衡版本校验、控制中心首帧布局、桌宠 outside-click、PetHost 生命周期/流畅性、图片热缓存和清理流程后台化均已进入正式链路。
- 正式 Release 流程已完成签名、SHA-256、PetHost self-test/内嵌验证、disabled manifest → Release → enabled manifest 的事务式发布验证。
- 自签名证书 GitHub Secrets 已更新，普通 Build 与正式 Release 统一使用 `FACM_PFX_BASE64` / `FACM_PFX_PASSWORD`。
- 3.1.0 已正式发布；随后发布 3.1.1 作为纯在线更新验证版。
- 用户已在真实 Windows 环境中确认：现有 3.1.0 客户端能够自动检测、下载、校验、替换并重新启动到 3.1.1，在线更新链路实机验证成功。
- Issue #28（3.1.0 正式发布）与 Issue #31（3.1.1 在线更新验证）均已完成关闭。

## 当前阶段

FACM 当前正式主阶段仍为 **基本完成 / 可暂停**。现有 3.1.1 是后续实验的稳定基线；Issue #33 的桌宠原型不改变正式版本和现有 VPet/PetHost 路线。

新任务继续按 `AGENTS.md`：从最新 `main` 开始，一任务一短分支 + PR；不要从旧本地快照或已合并任务分支继续开发。

## Issue #33 / PR #35：机器猫桌宠 Gate 1

- Issue：#33 `机器猫桌宠 Gate 1 原型（保留蜘蛛失败基线）`。
- Draft PR：#35 `Gate 1：独立机器猫桌宠原地动作原型`。
- 任务分支：`codex/machine-cat-gate1`。
- 旧 2D 原型：`prototypes/FACM.MachineCatPrototype/`。
- 当前 3D 原型：`prototypes/FACM.MachineCat3DPrototype/`。
- 当前仍不修改 `src/FACM`、`src/FACM.PetHost`，不替换 VPet，不进入自动漫游 Gate 2。

### 2D Gate 1 四轮实机结论

1. **第一版：失败。** 程序绘制 WPF 矢量机器猫；CI 全绿但外形与认可的圆润 2.5D 机器猫差距明显，动作有纸片感。
2. **第二版：失败。** 改用认可的透明动作/视角素材；外形恢复，但 Walk / Run / Turn 的完整角色 alpha-crossfade 产生明显“双机器猫/鬼影”。
3. **第三版：失败。** 禁止整图 crossfade 后鬼影消失；但 Walk / Run 仍是一张完整跑姿整体弹动/翻面，Turn 仍是离散前/3⁄4/侧/背图片切换。
4. **第四版：失败。** `ProceduralGaitFrames` 从认可 PNG 生成 32 个局部位移相位；真实 Windows 录屏确认脸/头基本稳定、无鬼影、无明显首次长卡顿，但手脚会拉长、变胖、缩回，Run 更明显，形成典型“橡皮变形感”。

**停止条件已触发：不再做第五轮 PNG crossfade / mirror / squash / warp 参数实验。** 当前扁平 PNG 在“最真实、最流畅”的目标下已达到可接受上限。

### 用户提供 3D 模型的实际检查结果

用户自行下载并提供：

- `664230004_doraemon_model.glb`
- 对应下载 ZIP，内含原始 FBX。

实际文件证据优先于网页描述：

- GLB 2.0；
- 61 个 Node；
- **29 个独立 Mesh**；
- `skins = 0`；
- `animations = 0`；
- GLB 没有贴图，只有一个通用材质；
- 原始 FBX 二进制扫描同样未检出 `Skin / Cluster / AnimationStack / AnimationCurve`；
- 因此它不是现成骨骼动画模型，但头、眼、鼻、嘴、胡须、项圈、铃铛、身体、口袋、左右上臂/小臂/手、左右腿/脚和尾巴已经天然拆成独立刚性 Mesh。

该结构允许 FACM Prototype 自己建立轻量“刚性分件 Rig”，不做蒙皮顶点拉伸。

### 当前 3D Gate 1 路线

- .NET 8 WPF 内置 `Viewport3D`；Gate 1 暂不引入大型游戏引擎或第三方渲染库。
- 极窄 GLB 2.0 Loader，只实现当前模型实际需要的 triangle / POSITION / NORMAL / indices / node matrix。
- 加载时把 GLB 节点变换烘到独立 Mesh；运行时只围绕显式关节旋转刚性零件。
- 关节链：shoulder → forearm → hand、hip → foot、head/face group、bell secondary motion、tail secondary motion。
- Walk / Run 使用左右反相的真实关节角度；不 crossfade、不镜像整图、不 warp 顶点。
- Turn 直接让整个 3D 根节点围绕 Y 轴连续旋转，可自然覆盖任意角度。
- 模型下载文件没有经典材质区分，Prototype 按零件名在运行时恢复蓝/白/红/黄基础配色，并补两个小型程序瞳孔；不修改原模型文件。

### 第三方模型边界

- 模型文件 **不进入公开 FACM 仓库**；
- `.gitignore` 排除 `*.glb / *.gltf / *.fbx / *.zip`；
- 公开 GitHub Actions artifact 发布前硬检查这些格式不存在；
- 公开 runtime 只包含程序，测试者需自行把合法取得的 GLB 放到 EXE 同目录或拖到 EXE 上；
- 当前用户会话中可以把用户自己上传的模型与 runtime 组合成私有测试包，但不把该组合包提交/发布到 GitHub。

### 当前自动验证

3D Prototype 当前验证链：

1. .NET 8 WPF Release build；
2. 内置 synthetic GLB fixture 验证 parser、indices、node matrix；
3. 刚性 Rig 的 Idle / Walk / Run / Turn deterministic self-test；
4. 真正 Show 透明 WPF `Viewport3D` 窗口并收到 Rendering 帧；
5. win-x64 self-contained publish；
6. artifact 不含第三方模型的硬检查；
7. 核心 FACM Windows Build 继续独立通过。

已验证：`FACM Machine Cat 3D Prototype` Run #6 全绿，`FACM Windows Build` #652 全绿。

### 当前唯一剩余 Gate

用户在真实 Windows 上用其本地 GLB 运行 3D Gate 1，重点判断：

- 模型加载/朝向/大小是否正确；
- runtime 配色是否可接受；
- Walk 左右手脚是否真正围绕关节运动，而非拉伸；
- Run 是否自然、有速度差且不过度穿模；
- Turn 是否连续 360°；
- WPF `Viewport3D + AllowsTransparency` 的实际 CPU / GPU / 内存是否适合作为桌宠基础。

只有用户明确确认 3D Gate 1 的原地动作和性能方向合格，才能设计/实现 Gate 2 MotionController。CI 不能替代视觉和资源占用门禁。
