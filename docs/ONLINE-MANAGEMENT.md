# FACM 3.5 在线版本与公告管理

FACM 当前正式产品线是 **3.5.x lightweight**。生产事实以 `main`、GitHub Release 与 `online/version.json` 为准。当前正式版是 3.5.19；下一次正常发布使用 3.5.20。4.x bootstrap、CAB、迁移清单和 migration updater mode 已退出当前产品线。

## 在线资源

- `online/version.json`：当前版本、最低版本、强制更新、正式下载地址、SHA-256、更新说明和发布时间。
- `online/mirrors.json`：版本检查与下载可使用的镜像前缀。
- `online/announcement.json`：公告开关、标题、正文、级别、弹出设置和可选 HTTPS 链接。

`online/version.json` 当前只描述普通 3.5 更新，不再包含 4.x migration 字段。客户端离线或检查失败不会影响本地功能。

## 发布 3.5.x

唯一正式发布工作流是：

```text
.github/workflows/publish-3.5-lightweight.yml
Actions → FACM 3.5 Lightweight Release
```

它只接受 `3.5.x` 版本，并保持 WinForms / .NET Framework 4.8 轻量单 EXE 发布边界。发布前会验证工具输入、编译 Release、执行 release smoke、检查 `FACM.exe` 小于 10 MiB、确认 ToolBundle 已嵌入且大型 PetHost runtime 未嵌入，并要求生产签名证书。

发布有两种入口：

1. 修改并提交 `release/3.5-request.json` 到 `main`；
2. 在 Actions 中手动运行 `FACM 3.5 Lightweight Release` 并填写相同参数。

发布请求字段：

```json
{
  "version": "3.5.20",
  "minimum_version": "3.0.0",
  "force_update": false,
  "prerelease": false,
  "release_notes": "这里填写本次更新说明"
}
```

不要复用已经存在的版本号。工作流会拒绝已有 GitHub Release，例如已经存在 `v3.5.19` 时不能再次发布 3.5.19。

### 发布事务顺序

工作流采用“先禁用、发布验证成功后再启用”的顺序：

1. 冻结当前 `main` 作为发布基础提交；
2. 将程序集版本改为目标 3.5.x；
3. 编译、执行 smoke、检查轻量资源边界并签名；
4. 生成目标版本 `online/version.json`，此时 `enabled=false`，并提交到 `main`；
5. 创建并公开 GitHub Release；
6. 重新下载公开 `FACM.exe`，验证文件大小、SHA-256 和签名者；
7. 只有公开制品验证通过后才把 `online/version.json` 改为 `enabled=true`；
8. 同步更新 `docs/PROJECT_STATE.md` 中由发布工作流维护的正式版本状态。

因此发布中途失败时，不应把未验证的候选版本推给在线更新客户端。

### 发布签名

正式发布需要仓库 Secrets：

```text
FACM_PFX_BASE64
FACM_PFX_PASSWORD
```

发布工作流缺少任一值都会失败，而不是发布未签名正式包。

## 普通在线更新链

3.5 客户端下载新的 `FACM.exe` 后执行当前普通 updater replacement 流程：等待主程序退出、替换 EXE、失败时按当前回滚逻辑处理并重新启动。当前代码中没有 FACM 4 migration/bootstrap 分支。

镜像只负责传输，不是发布事实来源。正式 `download_url` 仍必须指向本仓库 GitHub Release。客户端会对下载结果执行发布清单 SHA-256、签名/Authenticode 与版本身份校验；失败结果不会安装。

## 镜像管理

`online/mirrors.json` 可以独立维护，不需要为了更换镜像发布新客户端。GitHub 原站仍是正式制品源和最终兜底之一。

镜像健康评分与缓存是性能优化，不能改变正式文件身份。缓存损坏、单条镜像超时或返回异常内容时，应继续尝试其他线路或 GitHub 原站。

## 公告管理

公告后台工作流：

```text
.github/workflows/manage-online.yml
Actions → FACM Online Management
```

可填写：

- `enabled`：是否启用公告；
- `popup`：新公告是否在启动时弹出；
- `title`：标题；
- `body`：正文；
- `level`：`info` / `warning` / `critical`；
- `link_url`：可选 HTTPS 链接；
- `announcement_id`：可选，留空时自动生成。

工作流会校验标题、正文和链接，然后只修改 `online/announcement.json` 并推送到 `main`。发布工作流和公告工作流共享 `facm-main-writer` concurrency group，避免两个自动化流程同时写 `main`。

也可以直接编辑 `online/announcement.json`，但新公告应使用新的 ID。

## 发布前后检查

发布新版本前至少确认：

- `FACM Windows Build` 通过；
- `FACM UI Text Contract` 通过；
- 涉及 Mayhem/海符时 `FACM Mayhem Source Probe` 通过；
- `release/3.5-request.json` 的版本号未被使用；
- 发布说明与实际提交一致；
- 不存在重新引入 4.x migration/bootstrap 的修改。

发布完成后确认 GitHub Release、`online/version.json` 的版本、SHA-256、下载地址和 `enabled=true` 完全一致，并确认清单仍是 migration-free。
