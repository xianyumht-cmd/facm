# FACM 在线版本与公告管理

## 在线资源

FACM 从仓库 `main` 分支读取：

- `online/version.json`：版本、最低版本、强制更新、下载地址、SHA-256、更新说明。
- `online/announcement.json`：公告开关、公告 ID、标题、正文、弹出方式和可选链接。

程序离线或请求失败时，不会影响现有本地功能。强制更新只在成功读取有效版本清单，并确认当前版本低于要求时生效。

## 发布新版本

进入仓库：

1. 打开 **Actions**。
2. 选择 **FACM Publish Release**。
3. 点击 **Run workflow**。
4. 填写：
   - `version`：新版本，例如 `3.1.1`。
   - `minimum_version`：允许继续运行的最低版本。
   - `force_update`：是否要求旧版本必须更新。
   - `prerelease`：是否为预发布。
   - `release_notes`：更新说明。
5. 运行工作流。

工作流会：

1. 更新程序集版本。
2. 校验并打包工具资源 DLL。
3. 编译和签名 `FACM.exe`。
4. 创建 GitHub Release，并上传 `FACM.exe`。
5. 计算发布文件 SHA-256。
6. 自动更新 `online/version.json` 并提交到 `main`。

发布工作流要求仓库 Secrets 中已经配置：

- `FACM_PFX_BASE64`
- `FACM_PFX_PASSWORD`

## 修改公告

### 使用后台表单

1. 打开 **Actions**。
2. 选择 **FACM Online Management**。
3. 点击 **Run workflow**。
4. 填写公告开关、标题、正文、级别、是否启动时弹出和可选链接。
5. 运行后，工作流会自动修改并提交 `online/announcement.json`。

每次新公告应使用新的公告 ID。留空时，后台会自动按时间生成新 ID。

### 直接编辑 JSON

也可以直接在 GitHub 网页编辑 `online/announcement.json`：

```json
{
  "enabled": true,
  "id": "announcement-20260807-01",
  "title": "FACM 公告",
  "body": "这里填写公告正文。",
  "level": "info",
  "popup": true,
  "updated_at": "2026-08-07T00:00:00+08:00",
  "link_url": ""
}
```

字段说明：

- `enabled`：是否显示公告。
- `id`：公告唯一 ID；更换后客户端会把它当作新公告。
- `title`：公告标题。
- `body`：公告正文。
- `level`：`info`、`warning` 或 `critical`。
- `popup`：是否在启动时自动打开在线中心。
- `updated_at`：更新时间。
- `link_url`：可选 HTTPS 链接。

## 客户端行为

- **手动更新**：悬浮球右键菜单 → `在线中心` 或 `检查更新`。
- **自动更新**：默认启动时检查；检测到新版本后提示下载和安装。
- **强制更新**：无法跳过，只能完成更新或退出程序。
- **文件校验**：下载完成后必须与版本清单中的 SHA-256 一致，否则停止安装。
- **替换方式**：更新程序等待当前进程退出，再替换原 EXE 并重新启动。
- **公告**：后台启用后显示在在线中心；新公告可按 `popup` 设置自动弹出。
