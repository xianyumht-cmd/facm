# FACM 便携目录布局

FACM 3.1+ 默认使用程序目录保存运行文件，不再把新的运行数据写入 `%LocalAppData%\FACM` 或 Windows TEMP。

正式发布的 `FACM.exe` 已内嵌与该构建匹配的完整 PetHost publish 包，因此正常安装、下载包和旧版单 EXE 在线升级都**不再要求**旁边额外存在 `PetHost\FACM.PetHost.exe`。

首次启动并启用 VPet 桌宠后，典型目录结构：

```text
FACM\
├─ FACM.exe
├─ settings.ini
├─ ui-text.ini
├─ logs\
└─ runtime\
   ├─ cache\
   │  └─ mayhem-images\
   ├─ pethost-host\
   │  └─ <FACM-MVID>\
   │     ├─ FACM.PetHost.exe
   │     └─ ... .NET 8 self-contained 运行文件
   ├─ pethost\
   │  ├─ Assets\
   │  │  └─ vpet-ac77ba14\
   │  └─ Cache\
   ├─ animal-sprites\
   ├─ updates\
   ├─ FACM-Tool-A.exe
   ├─ FACM-Mode-Tool.exe
   ├─ FACM-Mode-1.cmd
   ├─ FACM-Mode-2.cmd
   ├─ FACM-Mode-3.cmd
   └─ FACM-Mode-4.cmd
```

## PetHost 交付方式

正式构建流程先发布并自检 `FACM.PetHost`，再把完整 publish 目录压缩成 `PetHostBundle.zip`，以固定资源名 `FACM.Resources.PetHost.zip` 嵌入 `FACM.exe`。

第一次需要 VPet Core 时：

1. 如果应用目录存在历史/开发用的 `PetHost\FACM.PetHost.exe`，FACM 仍可直接使用它；
2. 否则 FACM 从自身内嵌资源释放完整 PetHost 到 `runtime\pethost-host\<FACM-MVID>`；
3. `<FACM-MVID>` 绑定当前 `FACM.exe` 的精确构建内容，因此新版本不会误复用旧版本 PetHost；
4. 释放使用私有 staging 目录并校验目标路径，完成后才切换为正式目录，避免半解压状态被当成可运行宿主；
5. CI 会让构建后的 FACM 自己执行一次内嵌释放，并运行释放后的 `FACM.PetHost.exe --self-test`。

这套设计保留了原来的“在线更新只下载并替换一个 `FACM.exe`”协议，同时保证从旧版本在线升级后也能得到与新 FACM 匹配的 PetHost。

## 行为

- 设置写入 `settings.ini`。
- 可自定义界面文字写入 `ui-text.ini`。
- 日志写入 `logs`。
- 工具、PetHost 运行宿主和更新文件写入 `runtime`。
- 旧 Sprite 资源缓存写入 `runtime\animal-sprites`。
- VPet 动作资源和生成缓存写入 `runtime\pethost`。
- 海斗图片磁盘缓存写入 `runtime\cache\mayhem-images`。
- FACM 所在目录不可写时，海斗图片缓存只保留在内存，不再回退 Windows TEMP。

## 旧数据迁移

为避免升级后重新下载/生成桌宠资源，PetHost 会在第一次使用新版时检查旧的：

`%LOCALAPPDATA%\FACM\PetHost`

如果存在，会复制到：

`FACM\runtime\pethost`

迁移逐文件检查长度，完成后再尝试删除旧目录。迁移失败不会阻止桌宠启动，PetHost 会在新的便携目录重新准备资源。

旧版 `settings.ini` / `ui-text.ini` 仍保留一次性兼容迁移读取；完成迁移后正常读写均使用 FACM 程序目录。

## 例外

旧 Desktop Homunculus 是已经退出正式路线的外部桌宠程序。FACM 仍保留历史安装位置探测兼容代码，因此可能读取 Program Files / LocalAppData Programs 中的历史安装信息，但当前 VPet PetHost 不依赖它，也不会把新的 VPet 数据写入这些目录。

应用目录下外置的 `PetHost\FACM.PetHost.exe` 只作为旧包兼容和开发调试入口保留，不是正式发布包的必需文件。

## 部署要求

整个 FACM 文件夹必须位于当前用户可写的目录，例如 `D:\FACM`。不建议直接放入 `Program Files` 等需要管理员权限才能写入的系统目录，因为 FACM 需要写入设置、日志、工具运行文件、PetHost 释放目录和缓存。
