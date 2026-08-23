# FACM 在线版本与公告管理

## 在线资源

FACM 的正式发布事实仍以仓库 `main` 分支和 GitHub Release 为准：

- `online/version.json`：版本、最低版本、强制更新、GitHub Release 原始下载地址、SHA-256、更新说明。
- `online/mirrors.json`：更新检查与下载可使用的动态镜像前缀列表。
- `online/announcement.json`：公告开关、公告 ID、标题、正文、弹出方式和可选链接。

程序离线或请求失败时，不会影响现有本地功能。强制更新只在成功读取有效版本清单，并确认当前版本低于要求时生效。

## 多镜像更新

FACM 不再把 GitHub 原站当作唯一网络线路。3.4.6 起客户端内置十余条启动级 HTTPS 镜像，并且会尝试读取 `online/mirrors.json` 更新镜像列表。当前维护池包含：

- `gh.dpik.top`
- `gh.felicity.ac.cn`
- `gh.bugdey.us.kg`
- `ghfast.top`
- `ghproxy.net`
- `gh-proxy.com`
- `gh.ddlc.top`
- `gh.xmly.dev`
- `cors.isteed.cc`
- `ghproxy.cc`
- `ghfile.geekertao.top`
- `dockerproxy.link`
- `dockerproxy.net`
- GitHub 原站（始终保留，不能被远程镜像表移除）

这些第三方线路不被假定为永久可用。客户端会记录本机成功率与延迟，失败线路自动降权；某条线路失效不会阻止继续尝试其它镜像和 GitHub 原站。

3.4.7 维护时根据实机日志移除了 `mirror.houlang.cloud`：该线路曾对 FACM 元数据请求返回无法反序列化的内容。动态池和内置池同时移除，仍保留 13 条镜像线路和 GitHub 原站兜底。

镜像使用标准前缀方式代理完整 GitHub URL。例如：

```text
https://ghfast.top/https://github.com/xianyumht-cmd/facm/releases/download/v3.4.7/FACM.exe
```

更新检查的 JSON 很小，客户端会按本机历史线路质量排序，并以最多 3 路为一组竞速请求。拿到第一个结构有效的更新清单后继续处理，失败线路自动降级到下一组。

下载 FACM.exe 时不会同时下载多个副本。客户端按线路质量顺序逐个尝试；单条线路连接超时、连续无数据、HTTP 错误、哈希错误、签名错误或版本错误时，自动删除临时文件并切换下一条线路。GitHub 原站始终是最终兜底之一。

客户端会把动态镜像表缓存到：

```text
runtime/cache/update-mirrors.json
```

并把每台电脑自己的线路成功率和延迟记录到：

```text
runtime/cache/update-mirror-health.json
```

因此不同地区、运营商可以逐渐形成不同的优先线路。镜像评分只是性能优化，缓存损坏或无法写入不会阻止更新。

### 镜像的安全边界

镜像只负责传输，不是发布信任源。

`online/version.json` 中的 `download_url` 必须仍然指向 `github.com/xianyumht-cmd/facm/releases/download/...` 的正式 Release 文件，客户端才会接受。下载得到的 FACM.exe 还必须同时通过：

- `online/version.json` 中正式 SHA-256 校验；
- 与当前 FACM 相同发布证书的签名者指纹校验；
- Windows Authenticode 文件摘要校验；
- EXE 文件版本与清单版本一致校验。

其中任意一项失败，该镜像下载结果都会被丢弃并尝试下一条线路，不会安装。

公告包含可点击链接，因此当前仍只从 GitHub 原始地址读取，不接受第三方镜像返回的公告内容。公告读取失败不会影响版本检查。3.4.7 起公告超时或临时不可达按 best-effort 事件记录，不再作为程序级 `ERROR` 污染日志。

## 维护镜像池

日常更换镜像不需要发布新 FACM。编辑 `online/mirrors.json` 即可。动态镜像必须是公开 DNS 主机上的 HTTPS 前缀；HTTP、localhost 和直接 IP 地址会被客户端拒绝。动态镜像不能移除程序内置的 GitHub 原站兜底。

建议每次维护时至少保留 10 条近期有公开可用证据的不同域名线路，并移除已明确失效的线路。客户端仍会用自己的健康评分处理不同运营商、地区下的实际差异。

如果所有内置线路和缓存线路同时失效，旧客户端无法凭空知道一个从未见过的新域名，因此仍可能需要人工更新一次。这也是同时保留较大的 bootstrap 镜像池和 GitHub 原站的原因。

## 3.4.7 日志问题治理

根据 2026-08-23 实机日志，本版针对高频但可恢复的问题做了收敛：

- 召唤师技能写入不再在首次 180ms 读回仍是旧值时立即判失败；改为有上限的渐进回读窗口，首次观察到目标值后再做一次稳定确认，只有整个窗口都未稳定才执行原有的一次重试。写入次数边界仍保持“初次 + 最多一次重试”。
- 符文页已满时继续 fail-closed：优先复用 FACM 自己创建的页面，找不到 FACM 页面时跳过符文，不覆盖用户已有符文页，并在日志给出释放一个自定义符文页后重试的动作提示。
- 桌宠停止逻辑区分“从未启用的无状态 Stop”和“主消息循环结束后仍在同一 STA/UI 线程上的清理”，避免正常退出或自更新时出现 `Pets module stop skipped` 假警报。
- 公告请求超时属于非关键联网能力，不影响版本检查和本地功能，因此降为普通信息日志。
- `mirror.houlang.cloud` 因实机返回内容无法解析，从内置与动态镜像池移除。

## 控制中心信息密度

3.4.6 起控制中心采用“标题常驻、说明悬停”的紧凑策略：

- `修复工具 / 英雄联盟 / 个性化` 默认只显示入口名称，鼠标悬停时在稳定说明区展示功能摘要；
- 英雄联盟中心左侧只保留 `对局 / 推荐 / 效率` 名称，悬停时在顶部说明区显示对应能力；顶部二级入口同样复用这一说明区；
- `清理环境` 属于会改变本机文件的操作，安全提示继续常驻，不隐藏到悬停里；
- 不使用会遮挡内容的长 Tooltip 作为唯一说明载体。

## 发布新版本

进入仓库：

1. 打开 **Actions**。
2. 选择 **FACM Publish Release**。
3. 点击 **Run workflow**。
4. 填写：
   - `version`：新版本，例如 `3.4.7`。
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

镜像不需要单独上传 FACM.exe。客户端把正式 GitHub Release URL 交给可用镜像做代理，因此 GitHub Release 仍是唯一正式制品源。

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
- **线路选择**：检查更新小文件最多 3 路竞速；EXE 下载逐路失败自动切换。
- **文件校验**：下载完成后必须通过 SHA-256、发布签名、Authenticode 摘要和文件版本校验。
- **替换方式**：更新程序等待当前进程退出，再替换原 EXE 并重新启动。
- **公告**：后台启用后显示在在线中心；新公告可按 `popup` 设置自动弹出。
