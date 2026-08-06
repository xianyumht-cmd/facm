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

## 正式发布签名

应使用受信任代码签名机构签发的 Authenticode 证书。自签名证书可用于内部测试与确认签名流程，但通常不能建立 SmartScreen 信誉。

构建后运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 `
  -ExePath .\artifacts\FACM.exe `
  -PfxPath C:\secure\facm-signing.pfx `
  -PfxPassword "你的PFX密码"
```

脚本使用 SHA-256 文件摘要与 RFC 3161 时间戳，并在完成后执行签名验证。

## GitHub Actions 自动签名

仓库工作流支持两个 Secrets：

- `FACM_PFX_BASE64`：PFX 文件转换后的 Base64 文本
- `FACM_PFX_PASSWORD`：PFX 密码

生成 Base64：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\facm-signing.pfx")) |
  Set-Content .\FACM_PFX_BASE64.txt -NoNewline
```

把文本内容写入 GitHub Secret，不要把 PFX、密码或 Base64 文件提交到仓库。

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

签名状态应为 `Valid`；发布网站同时展示 SHA-256，便于用户核对文件完整性。
