# 把这个项目改造成你自己的工具

本项目的骨架与业务无关，可以直接当作**Windows 桌面小工具模板**使用：

- 单个 exe，零第三方依赖（只用 .NET Framework 自带程序集，不需要 NuGet）
- 启动毫秒级，不装运行时、不装解释器、不联网
- `build.bat` 自动找编译器，不要求安装 Visual Studio
- 167 项自动化测试（算法 / 端到端 / 界面 / 配置 / 布局 / DPI）
- 高 DPI 适配（125% / 150% 不挤压）
- 中文界面、中文文档

---

## 一、复制步骤

1. 把整个仓库复制一份，改成新项目名（建议**纯英文路径**，避免编码坑）
2. 删掉 `bin\`（编译输出）、`config.json`（示例配置）
3. 改 `build.bat` 里的输出文件名：`KeywordClassifier.exe` → 你的名字
4. 改 `KeywordClassifier.csproj` 里的 `<AssemblyName>`、`<RootNamespace>`
5. 清空 `src\Settings.cs` 的 `CreateDefault()`，换成你的默认类别/参数
6. 换图标：改 `tools\IconMaker.cs` 的 `Draw()`，或把现成 `.ico` 覆盖到 `assets\app.ico`
7. 跑 `build.bat`，再跑 `test-all.bat`，确认基线是绿的
8. 改 `README.md`、`LICENSE`（把版权人换成你）、`CHANGELOG.md`

---

## 二、可以照搬的部分

| 能力 | 文件 | 说明 |
|---|---|---|
| 一键编译 | `build.bat` | 自动找编译器 + 响应文件避开空格路径 + 可嵌入图标 |
| 高 DPI 适配 | `src\Ui.cs` | `Ui.S()` / `Ui.SF()` / `Ui.Font()`，配合 `AutoScaleMode.None` |
| 三栏布局骨架 | `MainForm.BuildCenter / BuildLeftPanel / BuildRightPanel` | 顶栏 + 左设置 + 右结果 |
| 卡片式分组 | `MainForm.MakeCard / FillCard` | 带标题的白色卡片，内部用表格布局 |
| 后台处理 + 进度 | `Processor.RunBatch` + 主窗体定时器 | 后台线程只收集数据，UI 定时刷新，避免跨线程问题 |
| 暂停 / 继续 / 取消 | `Processor.Pause / Resume / Cancel` | `Monitor.Wait` 实现，不占 CPU |
| 配置持久化 | `src\Settings.cs` | JSON 存在 exe 旁边，损坏时回退默认 |
| 日志与结果表 | `MainForm` 的 `_logBox` / `_grid` | 时间戳日志 + 可双击、可右键的明细表 |
| 编码嗅探 | `src\TextReaders.cs` | BOM → 严格 UTF-8 → GBK 回退 |
| 程序图标 | `tools\IconMaker.cs` + `assets\app.ico` | 用代码画 9 个尺寸，手写 ICO，零依赖 |
| 测试脚手架 | `tools\*.cs` + `test-all.bat` | 六类测试，改完就能验证 |

---

## 三、换成自己的业务时，改哪里

| 你想做的 | 对应改动 |
|---|---|
| 换文件类型（pdf、csv…） | `src\TextReaders.cs` 加 `ReadXxx()` 并注册扩展名；`Processor.EnumerateFiles` 里加判断 |
| 换匹配/统计规则 | `src\Matcher.cs`，保持"返回命中明细 + 待替换区间"的接口不变，测试可复用 |
| 换输出方式（只出报表、不改名） | `src\Processor.cs` 的 `ProcessFile()` / `BuildOutputName()` |
| 换界面字段 | `src\MainForm.cs` 的 `BuildXxxCard()`；新选项在 `src\Settings.cs` 加字段（自动进 JSON） |
| 换图标 | 改 `tools\IconMaker.cs` 的 `Draw()`，删掉 `assets\app.ico` 后重新 `build.bat` |
| 加测试 | 复制 `src\SelfTest.cs` 里的 `Check(...)` 写法；端到端参考 `tools\E2ETest.cs` |

---

## 四、开发流程建议

1. **改算法先跑自检**：`bin\KeywordClassifier.exe --selftest`（43 项，秒级）
2. **改界面必跑布局与 DPI 测试**：`tools\LayoutTest.cs` 抓重叠/压扁，`tools\DpiTest.cs` 抓文字截断
3. **发布前跑 `test-all.bat`**：六套 167 项全绿再交付
4. **界面问题先用 `tools\Probe.cs` 量**，别靠猜：
   `bin\tools\probe.exe bin\KeywordClassifier.exe 1180x860`
5. **交付时说清楚双击哪个文件**（输出目录可能变，见 `notes.md` 第 23 条）

---

## 五、环境要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 7 SP1 及以上（在 Windows 10 Enterprise 25H2 上验证） |
| 运行时 | .NET Framework 4.8（Windows 10/11 自带） |
| 编译器 | .NET Framework 自带 `csc.exe`（C# 5）或任意 Visual Studio 的 Roslyn |
| 第三方库 | **无** |
| 语言特性 | 源码兼容 C# 5，因此两种编译器都能编 |

> 如果你确定目标机器都有 Roslyn，可以放开使用新语法（`build.bat` 已带 `/langversion:latest`），
> 但保留 C# 5 兼容能换来"在只有 Windows 的机器上也能编译"这个便利。
