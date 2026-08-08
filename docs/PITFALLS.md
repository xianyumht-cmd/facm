# FACM 常见陷阱与防回归规则

## 不要在 WinForms UI 线程递归扫描文件系统

### 根因

`GameLocator.ResolveGameRoot` 曾直接在控制中心事件处理器里执行向下 BFS。用户选择盘符根目录或大型上级目录时，同步 `Directory.EnumerateDirectories` 会冻结 UI；只有最大深度而没有时间、目录数量和取消预算，并不能限制同一层级的目录数量。

Windows 路径规范化还有一个容易忽略的语义差异：`C:\` 是盘符根目录，而 `C:` 是该盘当前工作目录。对根路径无条件 `TrimEnd('\\')` 会改变含义。

### 防回归规则

- 递归/广范围文件系统探测必须离开 WinForms UI 线程运行，同时保留可响应的进度/取消界面。
- 除深度限制外，必须同时有明确的时间预算和已发现/检查目录数量预算。
- 枚举目录时就执行预算检查，不能先把无限量子目录全部塞进队列。
- 跳过 reparse point；从盘符根等宽范围搜索时，不递归进入 Windows、Program Files、Program Files (x86)、ProgramData 等系统目录。
- 路径规范化必须保留盘符/UNC 根路径的根分隔符，不能把 `C:\` 变成 `C:`。
- `FACM.exe --game-locator-test` 必须继续验证：标记目录定位、目录数量预算、取消和根盘规范化。

### 关联

- Issue #16
- PR #22
