# 内置工具接入目录

把需要从原程序保留的二进制文件放在本目录。支持：

- `.exe`
- `.bat`
- `.cmd`

每个文件必须在 `payloads.manifest.json` 中登记，并填写真实 SHA-256。未登记、扩展名不允许、文件名包含路径、哈希格式错误或释放后哈希不一致时，FACM 都会拒绝运行。

示例：

```json
{
  "schemaVersion": 1,
  "payloads": [
    {
      "id": "tool-one",
      "displayName": "工具一",
      "description": "功能说明",
      "fileName": "tool-one.exe",
      "sha256": "填写64位SHA256",
      "arguments": "",
      "requiresElevation": false
    }
  ]
}
```

计算哈希：

```powershell
Get-FileHash .\src\FACM.App\Payloads\tool-one.exe -Algorithm SHA256
```

发布前应先对每个原始 `.exe` 签名，再执行 FACM 构建；签名会改变文件哈希，因此必须在签名完成后重新生成清单中的 SHA-256。

不要提交证书、私钥或证书密码。
