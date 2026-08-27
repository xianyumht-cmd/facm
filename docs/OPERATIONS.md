# FACM 构建、验证与发布运行手册

## 1. 当前生产线

FACM 3.5.15 是正式生产版本。在 FACM 4.0 Gate 13 之前：

- `online/version.json` / `release/request.json` 不因迁移 Gate 改动；
- `FACM.sln` / `src/FACM` / Updater / ToolBundle / PetHost 必须继续可构建；
- 4.0 问题只修 4.0 task branch/PR，不拿 3.5.15 生产线做试验场。

Legacy workflow：`FACM Windows Build`；用户可见文字另由 `FACM UI Text Contract` 独立守护。

## 2. FACM 4.0 Foundation CI

工作流：`.github/workflows/facm4-foundation.yml`，名称 `FACM 4.0 Foundation`。

每个 FACM 4.0 PR 至少执行：

```text
1. checkout fetch-depth=0
2. setup .NET 10
3. scripts/check-facm4-architecture.ps1
4. dotnet restore FACM4.sln -p:Platform=x64
5. dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
6. dotnet run FACM.FoundationSmoke
7. dotnet run FACM.WindowsSmoke
8. dotnet publish FACM.App win-x64 self-contained single-file
9. verify FACM.App.exe exists and no DLL leaks
10. upload artifact `facm4-x64`
```

迁移工程默认 `TreatWarningsAsErrors=true`；nullable/API warning 优先修类型与边界，不为过 CI 关闭 warning gate。

Architecture gate 必须阻止：Core UI/platform dependency、错误 ProjectReference 方向、ViewModel 越层、migration branch 修改 `online/version.json` / `release/request.json`。

## 3. 本地 4.0 验证

在 Windows + .NET 10 SDK 环境：

```powershell
pwsh ./scripts/check-facm4-architecture.ps1
dotnet restore FACM4.sln -p:Platform=x64
dotnet build FACM4.sln -c Release -p:Platform=x64 --no-restore
dotnet run --project src/FACM.FoundationSmoke/FACM.FoundationSmoke.csproj -c Release
dotnet run --project src/FACM.WindowsSmoke/FACM.WindowsSmoke.csproj -c Release
dotnet publish src/FACM.App/FACM.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=true -o artifacts/facm4
```

GitHub hosted Windows runner 是 deterministic engineering evidence，不替代 Gate 10/12 的真实硬件矩阵。

## 4. Single-file / Runtime Path

已验证 WinUI 3 unpackaged/self-contained/single-file：

- 分发可以是一个 EXE；
- 首次运行会 self-extract；
- `Environment.ProcessPath` 指向分发 EXE；
- `AppContext.BaseDirectory` 可指向 `%TEMP%/.net/...`。

因此 settings/log/cache/runtime/PetHost/update package/Updater replacement target 必须从 distribution executable 推导，禁止把 extraction path 当稳定目录。

若 Gate 10/12 真机证明 self-extract 的 Defender/SmartScreen/体积/更新体验不可接受，批准 fallback 是“一个签名 installer EXE -> self-contained app directory payload”，不是退回 WinForms。

## 5. Settings 2.0 操作规则

4.0 当前配置：

```text
legacy rollback source: <distribution>/settings.ini
Settings 2.0:          <distribution>/settings.v2.json
```

首次启动逻辑：

```text
settings.v2.json exists
  -> deserialize + exact schema validate

settings.v2.json missing + settings.ini exists
  -> parse 15 legacy keys
  -> migrate typed schema
  -> validate
  -> atomic save settings.v2.json
  -> keep settings.ini unchanged

both missing
  -> validated defaults
  -> atomic save settings.v2.json
```

禁止 migration 成功后删除/改写 `settings.ini`；禁止 v2 损坏时静默用 defaults 覆盖；future/unknown schema 不允许旧程序自动降级写回；Page/ViewModel 不自己读写 JSON/INI/File。

`PhysicalSettings2FileStore`：目标同目录 temp -> write -> flush -> flush-to-disk -> replace/move。异常/取消时旧目标保持，temp 仅 best-effort 清理。

## 6. Product State / Diagnostics 操作规则

`ProductStateStore` 是进程内统一 product-state 聚合 store。它只接受 owner 发布的事实，不主动发现 League、不发网络、不拥有 writer。Page/ViewModel 通过 `IProductStateReader` 读取/订阅；不要为页面新增第二套 polling/state cache。

状态发布规则：

- 只有值真正变化时增加 revision；
- subscriber 在 store lock 外执行；
- League gameflow 事实后续必须来自 Gate 3 的唯一 runtime/session 链；
- diagnostics/state 失败不得扩大 League write 权限。

4.0 structured diagnostics 默认文件：`<distribution>/logs/facm4-events.jsonl`。当前 sink 默认 4 MiB，超限 rotate 到 `.1`。

写诊断必须遵守：

1. 优先通过 `DiagnosticEventFactory` 创建事件；
2. 必须包含 ActionId / Module / Duration / Result / Reason / LeagueState / ClientVersion / Timestamp；
3. token/password/passwd/cookie/authorization/secret/credential/auth 等敏感数据不允许明文；
4. sink 落盘前再次 redaction；
5. 不把完整 LCU lockfile、auth header、cookie、用户 secret 放入 Reason/Data；
6. diagnostics 是 best-effort，日志 IO 失败不得阻止主程序启动或业务退出；
7. Gate 9 导出诊断包只能消费脱敏后的 source，不读取 secret-bearing runtime 对象。

Gate 5 deterministic smoke 覆盖：state transition、duplicate suppression、subscriber lock boundary、parallel revision/snapshot、required diagnostic fields、free-text/key redaction、并发 JSONL write、bounded rotation。

## 7. League 验证规则

- exactly one League discovery/auth/session owner；
- session secret 不进入公共 descriptor/log/diagnostic；
- read/write transport 共用一个 session source；
- credential 只允许 loopback；
- writer smoke 必须覆盖允许 target 与明确拒绝；
- Bench 只验证用户点击触发，不添加后台自动抢英雄；
- InGame 工作不得超过 Performance Contract。

第三方实时 source probe 与 deterministic build 分离；公网故障不得让核心 compile CI 随机失败。

## 8. Cleanup 验证规则

必须证明：selected path -> validated game root；先 plan 再 explicit confirm；系统目录操作需要 UAC；target 在允许 root/rule 内；reparse/junction/symlink 不穿透白名单；执行前重新验证；failure 逐项返回；UI progress/dialog 不拥有删除逻辑。

## 9. PetHost / Single Instance / Hotkey

- PetHost 继续独立 win-x64 helper，保留 asset/bootstrap self-test、parent-pid/IPC/Job Object 生命周期；故障不能拖垮 Shell。
- Single Instance = Ensure Open / Activate，不是 Toggle。
- 快捷键 = RegisterHotKey，不引入低级键盘 hook 或永久 polling。

## 10. Updater / 发布事务

正式更新必须继续满足：

```text
build candidate
-> deterministic smoke
-> size/hash/signature/package validation
-> release asset
-> verify asset digest
-> validated receipt
-> replacement transaction
-> verify online manifest
-> production pointer
```

Updater 必须保留：下载大小上限、多源/镜像 fallback、SHA-256、签名/package validation、validated receipt、等待主进程退出、独立提升替换、失败保旧版、rollback/recovery。

Gate 13 正式切换前必须 fresh safety check；没有 Gates 0～12 + settings migration + real-machine matrix + updater rollback 证据，不得修改生产 release controls。

## 11. 真机发布矩阵

GitHub runner 不能替代：普通非管理员 -> runas UAC -> elevated child（含取消）；Windows 10 1809/22H2；Windows 11；100/125/150/175/200% DPI；双屏左右/上下、负坐标、混合 DPI；keyboard-only、Tab/Enter/Esc、focus；Light/Dark/High Contrast/Text Scaling/basic screen reader；Defender/SmartScreen 冷启动与误报；3.5.15 -> 4.0 settings migration；interrupted updater replacement/rollback。

这些可不阻塞前面的工程 Gate，但未关闭时不得声称 Gate 12/13 release-ready。

## 12. 每个 Gate 关闭流程

1. 从最新 `main` 开一个 task branch；
2. 一个 Issue + 一个 short-lived branch + 一个 reviewable PR；
3. 同一 branch 完成代码、测试、canonical docs；
4. PR 上跑 legacy + 4.0 门禁；
5. latest HEAD 全绿；
6. merge 到 `main`；
7. verify `main`；
8. 直接进入下一 Gate，不要求用户逐次回复“继续”。

分支删除、tag 删除、生产部署/重启属于 destructive/production 操作，仍需 `AGENTS.md` 的 fresh safety check，不因 Gate 合并自动执行。
