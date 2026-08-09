# FACM Machine Cat 3D Prototype — Gate 1

这是 Issue #33 / Draft PR #35 在 2D Gate 1 四轮实机失败后的 **3D 刚性分件原型**。

## 为什么转 3D

真实 Windows 录屏已经确认：

1. 程序重画矢量角色：外形失败；
2. 整图 crossfade：出现双角色鬼影；
3. 单图镜像/切姿势：无鬼影但动作仍像切图片；
4. 扁平 PNG 局部位移场：手脚会拉长/变胖，产生明显橡皮感。

因此停止继续调 PNG 技巧。当前目标是：**不拉伸顶点，使用真正独立的 3D 零件围绕关节旋转。**

## 用户提供模型的已验证事实

本轮用户自行下载并提供：

- `664230004_doraemon_model.glb`
- 对应原始下载 ZIP / FBX

实际文件检查结果优先于网页描述：

- GLB 2.0；
- 29 个独立 Mesh；
- 61 个 Node；
- `skins = 0`；
- `animations = 0`；
- 原始 FBX 也未检出 Skin / Cluster / AnimationStack；
- 模型不是现成骨骼动画模型，但头、眼、鼻、嘴、胡须、项圈、铃铛、身体、口袋、左右上臂/小臂/手、左右腿/脚、尾巴已经拆成独立 Mesh；
- 因此 Prototype 在运行时为这些刚性零件建立关节关系，不做蒙皮顶点形变。

## 第三方模型边界

**模型文件不进入本仓库，不进入 GitHub Actions artifact。**

`.gitignore` 明确排除 `*.glb / *.gltf / *.fbx / *.zip`，CI 发布前也会检查 artifact 中不存在这些格式。

测试时：

1. 下载 `FACM-MachineCat-3D-Gate1-*` runtime artifact；
2. 将你自己合法取得的 `664230004_doraemon_model.glb` 放到 EXE 同目录；
3. 或直接把 GLB 文件拖到 `FACM.MachineCat3DPrototype.exe` 上。

## 技术路线

- .NET 8 WPF；
- 内置 `Viewport3D`，Gate 1 暂不引入大型游戏引擎或第三方渲染框架；
- 极窄 GLB 2.0 Loader，只实现当前模型实际需要的 triangle / POSITION / NORMAL / indices / node matrix；
- 加载时把 GLB 节点变换烘到各独立 Mesh 的世界坐标；
- 运行时按零件名建立刚性关节：
  - shoulder → forearm → hand；
  - hip → foot；
  - head / face group；
  - bell secondary motion；
  - tail secondary motion；
- Walk / Run 只旋转刚性 Mesh，不拉伸像素或顶点；
- Turn 是整个 3D 模型围绕 Y 轴连续旋转，不再切前/侧/背图片；
- 模型原文件只有通用材质，Prototype 按零件名在运行时恢复经典蓝/白/红/黄配色，并用两个很小的程序球体补瞳孔；不修改模型文件本身。

## Gate 1 操作

窗口固定，不做桌面自动巡航：

- `1` Idle
- `2` Walk
- `3` Run
- `4` Turn（连续 3D 旋转）
- `D` / `F1` 调试文字
- `Esc` 退出
- 鼠标左键可拖动窗口

本轮只验：

1. 外形/材质是否可接受；
2. Walk 左右手脚是否真的围绕关节连续摆动；
3. Run 是否比 Walk 明显更有速度/幅度，但不穿模得离谱；
4. Turn 是否真正连续 360°，彻底摆脱离散图片切换；
5. CPU / 内存是否适合继续做桌宠。

Gate 1 没被用户明确通过前，不做 Gate 2 自动漫游，不接 FACM/PetHost，不替换 VPet。
