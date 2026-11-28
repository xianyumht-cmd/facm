# 解包出的插件 JS Bundle（研究资产）

本目录存放从**本机国服客户端**解包出来的插件 JavaScript，用于查证客户端自身的实现方式
（端点、请求体、显隐与门禁逻辑）。**不是产品代码，不参与编译，不被应用引用。**

| 项 | 值 |
|---|---|
| 文件 | `plugin-bundles-16.23-tencent.zip`（5.2 MB，原始 25.3 MB） |
| 来源 | `D:\WeGameApps\英雄联盟\LeagueClient\plugins\<plugin>\assets.wad` |
| 客户端版本 | LeagueClient **16.23**（Tencent / Releases 分支） |
| 解包方式 | `RW 0x0304` 容器 → 逐帧 zstd 解压 → 取每个插件最大的 JS 帧 |
| 内容 | 43 个 `rcp-fe-*` / `rcp-be-*` 插件的 JS bundle（另有 5 个插件无 JS） |
| 重新生成 | `tools/re_all_bundles.py`（见 `../PLUGIN-BUNDLE-INDEX.md` 的清单） |

## 为什么放在仓库里

它们是**证据**，不是依赖：

- `FEATURE-PLAYBOOK.md` 里每个功能的请求体，都能在这里找到源码出处；
- 遇到"为什么这个端点在大厅能用、在选人不能用"这类问题，可以直接查实现而不是猜；
- 客户端升级后，可以重新解包并 diff，快速定位行为变化。

## 使用注意

- 这些是 Riot / 腾讯客户端的发行资源，仅作为本机逆向研究的分析对象留档，不要对外分发；
- 里面包含客户端的压缩后代码，**没有**账号凭据、token 或个人信息；
- 大文件（5.2 MB zip）通过普通 Git 提交；后续如果频繁更新，应改用 release 附件而不是继续
  往历史里堆二进制。

## 文件对应关系

zip 内文件名即插件目录名（`rcp-fe-lol-champ-select.js` 对应 `plugins/rcp-fe-lol-champ-select/`）。
各插件的帧数、JS 体积、路由数见 `../PLUGIN-BUNDLE-INDEX.md`。
