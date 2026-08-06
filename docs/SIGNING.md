# Windows 发布、签名与误报治理

## 结论

没有数字签名会明显降低发布可信度，但“签名”不是消除所有安全软件告警的保证。Windows 会同时考虑文件信誉、发布者信誉、下载来源和程序行为。

Microsoft 的 SmartScreen 开发者说明：未签名文件通常会显示“Windows 已保护你的电脑”提示；每个公开发行版本都应签名，信誉需要逐步积累。过去 EV 证书可在首次下载时直接获得正面信誉的机制已经取消。

官方说明：

- https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/windows/win32/seccrypto/signtool
- https://learn.microsoft.com/windows/win32/seccrypto/time-stamping-authenticode-signatures

## 推荐发布结构

本项目默认使用普通文件夹发布，不启用单文件自解压，也不使用压缩壳或代码混淆壳。这样可以减少运行时释放大量随机文件、隐藏子进程和内存解包等高风险特征。

主程序采用 `asInvoker`：

- 启动 FACM 时不自动申请管理员权限；
- 只有清单中明确设置 `requiresElevation: true` 的单个工具，在用户点击后才申请权限；
- 每次执行前都展示确认信息并记录结果。

## 证书选择

公开分发时应使用受 Windows 信任的代码签名证书或 Microsoft 提供的签名服务。自己生成的自签名证书只适合内部测试；除非目标电脑预先信任该根证书，否则它不能建立面向普通用户的发布者信任。

私钥不得进入 Git 仓库。推荐把证书安装在 Windows 证书存储区，并通过证书指纹调用签名脚本，避免在命令行或脚本中传递 PFX 明文密码。

## 正确发布顺序

### 1. 签名待嵌入的 EXE

```powershell
.\scripts\sign-release.ps1 `
  -InputDirectory .\src\FACM.App\Payloads `
  -CertificateThumbprint "你的证书指纹" `
  -TimestampUrl "证书机构提供的 RFC 3161 地址"
```

### 2. 更新清单哈希

签名会改变二进制内容，因此必须在签名后更新 SHA-256：

```powershell
.\scripts\update-payload-hashes.ps1
```

### 3. 构建 FACM

```powershell
.\scripts\build-release.ps1
```

### 4. 签名发布目录

```powershell
.\scripts\sign-release.ps1 `
  -InputDirectory .\artifacts\win-x64 `
  -CertificateThumbprint "你的证书指纹" `
  -TimestampUrl "证书机构提供的 RFC 3161 地址"
```

脚本使用：

- 文件摘要：SHA-256；
- RFC 3161 时间戳：`/tr`；
- 时间戳摘要：SHA-256；
- 验证策略：`signtool verify /pa /all /v`。

## 其他降低误报的措施

- 不使用 UPX、Themida、VMProtect 等加壳方式；
- 不把主程序伪装成系统文件；
- 不在随机临时路径中静默执行；
- 不自动关闭安全软件、修改其排除项或停用系统保护；
- 不使用隐藏窗口启动命令解释器；
- 保持稳定的产品名、公司名、版本号和签名主体；
- 每个版本公开 SHA-256，并保留可复现构建记录；
- 对确认属于误报的正式签名样本，通过对应安全厂商的样本申诉渠道提交复核。
