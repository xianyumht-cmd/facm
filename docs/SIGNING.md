# FACM 3.0 构建签名说明

## 先说明结论

没有 Authenticode 签名会显示“未知发布者”，但签名不是安全软件判断的唯一依据。FACM 3.0 同时采用以下方式降低不必要的告警：

- 主程序使用标准 .NET Framework 4.8 WinForms，不加壳、不混淆。
- 默认不联网、不创建服务、不设置计划任务和开机启动。
- 仅在用户主动开始清理时请求管理员权限。
- 内置资源释放到固定目录，不使用随机文件名。
- 内置可执行资源在运行前进行固定 SHA-256 校验。
- 清理目标来自编译期白名单，先预览、再确认、最后重新校验。
- 发布包附带 SHA-256 与签名状态报告。

## 仓库是否保存证书

仓库不会保存 `.pfx`、`.cer`、密码或 PFX 的 Base64 文本；`.gitignore` 已明确排除这些文件。私钥不应提交到公开仓库。

仓库提供自签名证书生成脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\create-self-signed-certificate.ps1
```

默认输出到：

```text
local-signing\
```

生成内容包括：

- `FACM-SelfSigned-CodeSigning.pfx`
- `FACM-SelfSigned-CodeSigning.cer`
- `FACM_PFX_BASE64.txt`
- `FACM_PFX_PASSWORD.txt`
- `README.txt`

自签名证书仅适合开发、内部测试和验证签名流程，通常不能建立 SmartScreen 信誉。

## 正式发布签名

应使用受信任代码签名机构签发的 Authenticode 证书。构建后运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx `
  -PfxPassword "你的PFX密码"
```

脚本使用 SHA-256 文件摘要与 RFC 3161 时间戳，并在完成后执行签名验证。

## GitHub Actions 自动签名

仓库工作流支持两个 Repository Secrets：

- `FACM_PFX_BASE64`：PFX 文件转换后的 Base64 文本
- `FACM_PFX_PASSWORD`：PFX 密码

使用生成脚本后，直接复制以下两个文件的完整内容：

```text
local-signing\FACM_PFX_BASE64.txt
local-signing\FACM_PFX_PASSWORD.txt
```

在 GitHub 仓库中依次进入：

```text
Settings → Secrets and variables → Actions → New repository secret
```

分别创建上面的两个 Secret。不要把 PFX、密码或 Base64 文件提交到仓库。

也可以手动生成 Base64：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\facm-signing.pfx")) |
  Set-Content .\FACM_PFX_BASE64.txt -NoNewline
```

## 手动触发构建

仓库的 `.github/workflows/build.yml` 已配置 `workflow_dispatch`。在 GitHub 网页中：

1. 打开仓库的 `Actions` 页面。
2. 左侧选择 `FACM Windows Build`。
3. 点击右侧的 `Run workflow`。
4. 分支选择 `main`。
5. 再点击绿色的 `Run workflow`。

完成后打开对应运行记录，在页面底部的 `Artifacts` 下载 `FACM-Windows-x64-运行编号`。

## 内置工具

主程序签名不会改变已经内嵌工具自身的签名状态。正式发布时，推荐流程是：

1. 对你拥有发布权的每个内置可执行文件分别完成代码签名。
2. 再把签名后的文件压缩嵌入 FACM。
3. 更新对应 SHA-256 常量。
4. 构建 FACM。
5. 最后对 FACM.exe 自身签名并加时间戳。

不要在每次运行时生成或修改内置工具，否则哈希和签名都会失效，也更容易触发告警。

## 发布前检查

```powershell
Get-AuthenticodeSignature .\artifacts\FACM.exe | Format-List *
Get-FileHash .\artifacts\FACM.exe -Algorithm SHA256
```

使用受信任证书时，签名状态应为 `Valid`；发布网站同时展示 SHA-256，便于用户核对文件完整性。使用自签名证书时，未安装对应公钥证书的电脑可能显示证书链不受信任，这是自签名证书的正常限制。

## FACM 4.0 清单签名

FACM 4.0 还会对 `manifest.json` 和组件清单做 RSA-2048 PKCS#1/SHA-256 detached 签名。该签名不是
Authenticode，不能直接使用 3.5 PFX；4.0 启动器只信任编译进自身的 `facm-production-r1` 公钥。
本地自签名发布时，4.0 私钥保存在仓库外的 `local-signing` 目录，公钥随 native bootstrapper 编译，
私钥必须单独备份且不得进入仓库、Release 资产或日志。

## 云端 ChatGPT 交接边界

云端 ChatGPT 可以管理源码、构建和远端 Release，但只有在运行环境明确提供受控的签名凭据时才能完成正式上传。
本机 `local-signing` 目录和 Windows Git credential manager 不会自动出现在云端任务中；缺少它们时，任务必须停在签名前，
输出待签名文件的 SHA-256 和期望证书/keyId，不能上传未签名包，也不能要求用户在聊天中发送私钥或密码。

云端恢复发布时，先验证签名后的 native `FACM.exe` 的 Authenticode 证书指纹、`FileVersionInfo` 和 SHA-256，再验证四个
detached 清单签名，最后才允许更新在线指针。3.5 PFX 与 4.0 detached 私钥始终是两条独立信任链。
