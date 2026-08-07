# FACM 便携目录布局

FACM 3.1 默认使用程序目录保存运行文件，不再把新的运行数据写入 `%LocalAppData%\FACM` 或 Windows TEMP。

首次启动后典型目录结构：

```text
FACM\
├─ FACM.exe
├─ PetHost\
│  └─ FACM.PetHost.exe
├─ settings.ini
├─ ui-text.ini
├─ logs\
└─ runtime\
   ├─ cache\
   │  └─ mayhem-images\
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

## 行为

- 设置写入 `settings.ini`。
- 可自定义界面文字写入 `ui-text.ini`。
- 日志写入 `logs`。
- 工具和更新文件写入 `runtime`。
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

## 部署要求

整个 FACM 文件夹必须位于当前用户可写的目录，例如 `D:\FACM`。不建议直接放入 `Program Files` 等需要管理员权限才能写入的系统目录。
