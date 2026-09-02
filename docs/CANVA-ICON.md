# GGman / FACM 应用图标

当前品牌图标采用深海军蓝底、电光青色双 `G` 与环形轨迹。任务栏/EXE 和托盘使用同一视觉语言，
但不是同一张图直接缩放：任务栏保留完整发光细节，托盘使用小尺寸专用简化图形，并把主体向左下收，
给右上角状态点保留约 30% 的安全区。

仓库中的源图与最终资源：

```text
src/FACM/Resources/FACM-Canva.png
src/FACM/Resources/GGman-Tray-Source.png
src/FACM/Resources/FACM.ico
src/FACM/Resources/GGman.Tray.Connected.ico
src/FACM/Resources/GGman.Tray.Connecting.ico
src/FACM/Resources/GGman.Tray.Offline.ico
```

`FACM.ico` 包含 16、20、24、32、40、48、64、128 和 256 像素原生层：16–24 像素使用简化双 G，
32 像素及以上使用完整版本。FACM 3.x、FACM.App 4.0 和 Native Bootstrapper 都把它写入 EXE。

托盘图标包含 16、20、24、32 像素原生层，状态语义固定为：

- 灰色：League/LCU 未运行；
- 黄色：正在连接或暂时不可用；
- 绿色：LCU 已连接。

状态点包含深色描边并向内保留一个像素，不得贴边、裁切或从大图自动缩放生成。
