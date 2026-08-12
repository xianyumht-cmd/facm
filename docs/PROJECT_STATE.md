# FACM 当前项目状态

> 2026-08-13：FACM 3.1.3 仍是线上正式版。高清 Flying Runtime、五种飞虫精修和产品化桌宠选择器均已完成 Windows 实机验收并进入 `main`。当前任务为 Issue #53 / PR #54：普通模式二次启动直接唤醒现有 FACM 控制中心；尚未合并、尚未发布，等待 Windows 实机验收。

<!-- FACM_RELEASE_STATE_BEGIN -->
## 当前正式版（发布工作流维护）

- 版本：FACM 3.1.3
- GitHub Release：v3.1.3
- 在线更新：已启用
- minimum_version：3.0.0
- force_update：false
- Release FACM.exe SHA-256：`5A6D9C02F2E93A98909D861E6E51301055EF5679CDF85458586759873C6565A2`
<!-- FACM_RELEASE_STATE_END -->

## 已进入 main、尚未发布的新能力

### Flying Runtime 与桌宠产品化

- PR #44：高清绿苍蝇基线已实机验收并合并；保持 `greenfly` ID、既有速度/轨迹和自由出屏行为。
- PR #46：统一 Flying Runtime 已实机验收并合并；主路线为 **绿苍蝇 / 蜜蜂 / 蜻蜓 / 蝴蝶 / 飞蛾**，VPet Core 保留为独立高精度路线。
- PR #48：蜜蜂、蜻蜓、蝴蝶、飞蛾的素材与既有 Profile 二次精修已实机验收并合并；绿苍蝇继续作为轨迹回归基线。
- PR #52：产品化桌宠选择器 Build #790 已实机验收并合并；当前项、飞行性格、轻量/高精度定位和应用交互已收口。
- Legacy 猫、狗、蜘蛛、蚂蚁等 ID 继续兼容旧 `settings.ini`，但不再作为新选择器推荐项。
- Flying Runtime 不使用屏幕硬边界；桌宠允许自然飞出所有屏幕，恢复入口仍是“复位桌面位置”。

### 发布状态维护

- Issue #49 / PR #50 已完成：正式发布工作流只维护 `FACM_RELEASE_STATE` marker 区块，不再整份覆盖 `PROJECT_STATE.md`，也不再写入旧 Build/Issue 模板。
- 3.1.3 发布后曾出现的旧状态覆盖问题已经修复；当前在线 manifest 未受影响。

## 当前任务：Issue #53 / PR #54 二次启动唤醒

### 目标

普通模式已有 FACM 正在运行时，用户再次双击 `FACM.exe` 不再只看到“FACM 已经在运行”，而是直接唤醒原进程的控制中心。

### 当前实现

- 分支：`feat/single-instance-activation-0813`。
- PR：#54，当前保持 OPEN、未合并。
- 普通 Mutex 继续负责单实例所有权；新增当前 Windows 会话内的命名 AutoResetEvent，仅传递无参数“激活”信号。
- 第二实例检测到普通 Mutex 已占用后，最多 1.6 秒有限重试打开激活事件；成功后 `Set()` 并正常退出，不再弹“已经在运行”。
- 第一实例收到信号后：控制中心未打开则创建并显示；已经打开则只 `BringToFront + Activate`，不会因为复用 Toggle 路径把控制中心关掉。
- Flying 桌宠 / VPet 不停止、不切换；控制中心继续按当前桌面形态的既有定位逻辑显示。
- 第一实例刚取得 Mutex 但 WinForms message loop 尚未完全就绪时，以 pending activation 保留请求，在 `Shown` 后消费，避免窄竞态丢失二次启动。
- `--cleanup` 继续使用独立 elevated cleanup Mutex；所有现有 smoke/test 模式继续使用独立 Mutex，不参与普通实例激活。
- 新增 `--single-instance-activation-test`：验证 listener 不存在时有限失败、listener 存在时第一次与重复激活都各触发一次回调。

### 验证状态

- 初版 Windows Build #794 在编译期失败：`ObjectDisposedException` 继承 `InvalidOperationException`，catch 顺序写反导致 CS0160；已通过把子类 catch 放在父类前修复，未改变业务设计。
- 修复后的 PR HEAD：`5a52ff8936a655c023746d3517848530b9bc26eb`。
- FACM Windows Build #797：完整成功，包括 PetHost publish/self-test、Release build、FACM.exe 验证、签名步骤、打包与上传；新增单实例激活 smoke 随 CI build target 一并通过。
- FACM Mayhem Source Probe #116：成功。
- 当前缺口：**Windows 实机二次启动体验尚未由用户验收**。

### 实机验收重点

1. 正常启动 FACM 后再次双击同一 `FACM.exe`：不应出现“FACM 已经在运行”，原进程控制中心应直接出现。
2. 控制中心已经打开时再次双击：控制中心应保持打开并置前，不能被关闭。
3. 绿苍蝇/其它 Flying 桌宠运行时再次双击：桌宠应继续运行，只出现控制中心。
4. VPet 运行时同样不应被停止或切换。
5. 关闭控制中心后再次双击，应可重复唤醒。

## 发布边界

- Issue #53 / PR #54 当前只作为测试候选，不触发正式 Release，也不修改 `online/version.json`。
- FACM 3.1.3 继续作为线上正式版；下一次正式发布必须另行获得用户明确授权。
