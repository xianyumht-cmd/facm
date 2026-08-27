# FACM 构建、验证与发布运行手册

## 1. 当前生产线

FACM 3.5.15 是正式生产版本。在 FACM 4.0 Gate 13 之前：

- `online/version.json` / `release/request.json` 不因迁移 Gate 改动；
- `FACM.sln` / `src/FACM` / Updater / ToolBundle / PetHost 必须继续可构建；
- 出现 4.0 foundation 问题时回滚 4.0 task branch/PR，不拿 3.5.15 生产线做试验场。

核心 legacy workflow：`.github/workflows/build.yml`，名称 `FACM Windows Build`。它负责确定性构建与本地 smoke，不依赖 OP.GG、腾讯站点、Data Dragon 等实时公网服务可用性。

主要验证包括：PetHost x64 publish/self-test/bundle、FACM .NET Framework 4.8 Release、modular host、single-instance activation、Cleanup/League/Mayhem 等 deterministic smoke、内嵌资源、签名步骤和 artifact。

`FACM UI Text Contract` 继续独立守用户可见文本 contract。

## 2. FACM 4.0 Foundation CI

工作流：`.github/workflows/facm4-foundation.yml`，名称 `FACM 4.0 Foundation`。

Gate 1 起每个 FACM 4.0 PR 至少执行：

```text
1. checkout fetch-depth=0
2. setup .NET 10
3. scripts/check-facm4-architecture.ps1
4. dotnet restore FACM4.sln -p:Platform=x64
5. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
6. dotnet run FACM.FoundationSmoke
7. dotnet publish FACM.App win-x64 self-contained single-file
8. verify FACM.App.exe exists and no DLL leaks
9. upload CI artifact
```

Gate 1 accepted evidence：Foundation #6 SUCCESS，artifact `facm4-gate1-x64`，id `9636175208`，digest `sha256:e574ec965f7b3dffa3f473f01e0312ca2a5432a366e40d62aa1fd07737f5e81a`。

### Architecture gate

`scripts/check-facm4-architecture.ps1` 必须阻止：

- Core 引用 ProjectReference / PackageReference；
- Core 出现 WinUI/WinForms/WPF/System.Drawing UI framework dependency；
- Infrastructure/Platform.Windows 反向引用 App；
- App 缺少规定 composition dependencies；
- migration PR 修改 `online/version.json` / `release/request.json`。

ProjectReference 检查应解析 csproj XML 后比较项目名，不用正则直接匹配 Windows `\` 路径；Gate 1 已验证后者容易产生 false positive。

## 3. FACM 4.0 本地构建命令

在有 .NET 10 SDK 和 Windows 构建工具的机器上：

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet publish src/FACM.App/FACM.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=true -o artifacts/facm4
```

迁移工程默认 `TreatWarningsAsErrors=true`；遇到 nullable/API warning 优先修类型或边界，不为过 CI 全局关闭 warning gate。

## 4. Single-file 分发事实

Gate 0 已验证 WinUI 3 unpackaged/self-contained/single-file 的真实行为：

- 分发形态可以是一个 EXE；
- 首次运行会 self-extract；
- `Environment.ProcessPath` 指向分发 EXE；
- `AppContext.BaseDirectory` 指向 `%TEMP%/.net/...` extraction directory。

因此：

- Runtime/config/cache/PetHost/update package 不得依赖 extraction path 稳定存在；
- Updater replacement target 必须来自 distribution executable path；
- assembly 内嵌资源使用 resource API，不通过 `.net/...` 相对路径猜测。

如果 Gate 10/12 真机证明 self-extract 的 Defender/SmartScreen/体积/更新体验不可接受，批准的 fallback 是“一个签名安装器 EXE -> self-contained app directory payload”，不退回 WinForms。

## 5. Legacy modular host / single instance

Legacy `FACM.exe --facm-host-test` 继续验证拓扑初始化、缺失/重复/循环依赖、失败 rollback、反向 Dispose、timing report。

普通实例保持：

- Mutex = 单实例 owner；
- 当前 Windows session 命名 AutoResetEvent = Ensure Open / Activate 信号；
- 二次启动是打开现有 UI，不是 toggle；
- smoke/test 使用独立 Mutex；
- 不扩展为本地 HTTP/TCP server。

4.0 移动到 Platform.Windows 后语义必须等价或更强。

## 6. League 验证规则

- 唯一 League discovery/auth/session owner；
- 新模块不得自己读 lockfile/命令行并长期持有第二 session；
- 所有写能力走窄 writer allowlist；
- writer smoke 必须覆盖允许 path/method 和明确拒绝的 path/method；
- Bench 只验证用户点击触发一次 swap，不添加后台自动抢英雄；
- InGame smoke 需要证明网络/图片/磁盘/CPU/prefetch/timer 不超过 Performance Contract。

实时外部 source probe 与 deterministic build 分离。第三方站点故障不得让核心 compile CI 随机失败。

## 7. Cleanup 验证规则

迁移前后都必须证明：

1. selected path -> validated game root；
2. 先生成 plan，再由用户确认执行；
3. 系统目录操作需要 UAC；
4. 目标必须在允许 root/rule 内；
5. reparse-point/junction/symlink 不允许穿透白名单；
6. 执行前重新验证规则，不盲信 UI 预览对象；
7. 阻止项/失败项进入结果，不静默吞掉；
8. UI progress/dialog 不属于 Core 删除逻辑。

## 8. PetHost

PetHost 在 4.0 迁移期间继续是独立辅助进程。构建/发布需要保留：

- win-x64 publish；
- asset/bootstrap self-test；
- embedded/delivered bundle 验证；
- parent-pid / IPC / Job Object 生命周期；
- PetHost 故障不能拖垮主 Shell。

除非有独立 Issue/证据，不在主 WinUI 迁移里顺手重写 PetHost。

## 9. Updater / 发布事务

正式更新仍遵循事务思路：

```text
build candidate
-> deterministic smoke
-> hash/sign/package validation
-> release asset
-> verify asset digest
-> update release metadata
-> verify online manifest
-> enable/update production pointer
```

任何一步失败都不能留下“manifest 指向不存在/未验证资产”的半发布状态。

Updater 必须保持：下载大小上限、多源/镜像 fallback、SHA-256、签名/package validation、validated receipt、等待主进程退出、替换、失败保留旧版、可恢复/rollback。

Gate 13 进行 4.0.0 正式切换前必须做一次 fresh safety check：确认 Gates 0～12、配置迁移、Windows 真机矩阵、Updater rollback、Release asset、在线 manifest 均成立后，才允许修改生产 release controls。

## 10. 真机发布矩阵

GitHub hosted Windows runner 不能替代以下证据：

- 普通非管理员 -> runas UAC -> elevated child；包括取消；
- Windows 10 1809/22H2；
- Windows 11；
- 100/125/150/175/200% DPI；
- 双屏、左右/上下、负坐标、混合 DPI；
- keyboard-only、Tab/Enter/Esc、focus；
- Light/Dark/High Contrast/Text Scaling/basic screen reader；
- Defender/SmartScreen 冷启动与误报；
- 3.5.15 -> 4.0 settings migration；
- interrupted updater replacement/rollback。

这些可以不阻塞前面的工程 Gate，但未关闭时不得声称 Gate 12/13 release-ready。

## 11. 每个 Gate 的关闭流程

1. 从最新 `main` 开一个 task branch；
2. Issue 明确目标/非目标/验收；
3. 在同一 branch 完成代码、测试、canonical docs；
4. PR 上跑 legacy + 4.0 相关门禁；
5. 修到 latest HEAD 全绿；
6. 更新 Issue/PR 状态；
7. merge 到 `main`；
8. verify `main`；
9. 直接进入下一 Gate，不要求用户逐次回复“继续”。

分支删除属于 destructive Git 操作，仍按 `AGENTS.md` 要求另做即时安全检查，不因为 Gate 合并自动强删。
