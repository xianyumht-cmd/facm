# FACM 3.x → 4.0 迁移桥接

本次桥接解决的是版本拓扑变化，不是简单的版本号更新：旧版是一个 .NET Framework 4.8 单文件，FACM 4.0 是原生 `FACM.exe` 启动器加 `.facm` 组件组合。

## 迁移顺序

1. 旧版 `online/version.json` 仍指向一个正常签名的 3.5.17 `FACM.exe`。
2. 3.5.16 通过原有单文件更新器安装 3.5.17。
3. 3.5.17 启动时只消费清单中的可选 `migration` 对象。
4. 桥接下载并校验 4.0 原生启动器：GitHub Release 路径、SHA-256、旧版 FACM Authenticode 签名和 `4.0.0` 文件版本必须同时通过。
5. 桥接把旧版 `settings.ini` 复制到 `.facm\settings.ini`，不删除或改写旧文件；同时原子写入 4.0 `bootstrap.json`。
6. 内置更新器以 migration 模式原子替换根目录 `FACM.exe`，保留完整旧文件作为 rollback image。
7. 启动器执行 `--update`，只有在目标 `active.json`、版本目录和匹配的 `FACM.App.exe` 进程均出现后才提交迁移；否则恢复旧版并写入失败状态。

## 清单字段

`online/version.json` 的 `migration` 对象：

```json
{
  "enabled": true,
  "version": "4.0.0",
  "bootstrapper_url": "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/FACM.exe",
  "bootstrapper_sha256": "<64 位十六进制 SHA-256>",
  "manifest_url": "https://github.com/xianyumht-cmd/facm/releases/download/v4.0.0/manifest.json",
  "release_notes": "FACM 4.0 组件迁移"
}
```

旧版清单的 `version`/`download_url` 在桥接阶段必须继续指向 3.5.17。不能把它们直接改成 4.0 组件清单，因为旧客户端只会按单文件协议下载和替换 `FACM.exe`。

## 发布门禁

- 3.5.17 桥接包必须使用现有 FACM Authenticode 发布证书签名。
- 4.0 原生启动器也必须使用同一发布签名，并提供 `4.0.0.0` 文件版本。
- 4.0 `manifest.json`、组件清单和 CAB 包仍须通过原生启动器内嵌的 `facm-production-r1` detached-signature 校验。
- 当前 4.0 Gate 13 仍有真实 Windows/迁移/最终签名证据缺口；在这些证据完成前，不得把 `online/version.json` 切换到生产 4.0，也不得退休 3.5.x 回滚资产。

## 本地验证

```text
FACM.exe --facm4-migration-test
FACM.Updater.exe --self-test
```

这两个自检只验证桥接参数、URL/哈希规则、配置生成和原子更新原语，不会联网迁移或修改生产指针。
