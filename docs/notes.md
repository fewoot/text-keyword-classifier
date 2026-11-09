# 开发笔记：25 个踩过的坑

> 本文是本项目（[KeywordClassifier](../README.md)）开发过程中真实踩到的坑，
> 按"编译工具链 / 界面 / 高 DPI / 测试 / 功能实现 / 交付"分类整理。
> 做同类 Windows 桌面小工具时可以直接对照，能省掉大量返工。

这份文档记录开发这个工具时**真实踩到的坑**，以及每个坑的最终做法。
拿它当模板做新项目时，建议逐条对照，能省掉大量返工。

---

## 一、编译与工具链

### 1. 编译器可能不在 PATH 里，必须写绝对路径

Visual Studio 可能装在非默认路径（本机就是 `E:\visual studio IDE` 这种自定义目录），
甚至**没有注册到系统**，所以 `vswhere`、PATH、注册表都找不到它。
系统自带的 `csc.exe` 又只支持 C# 5（不支持字符串内插 `$""`、`?.`、表达式体成员等现代语法，实测报 `CS1056`）。

**做法**：`build.bat` 里按顺序找编译器，找到就用绝对路径调用：

```bat
rem 1) WCT_CSC 环境变量  2) PATH 里的 csc  3) 扫描 Program Files 下任意 VS 版本
rem 4) 兜底：%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
for /d %%Y in ("%ProgramFiles%\Microsoft Visual Studio\*") do (
  for /d %%E in ("%%Y\*") do (
    if not defined CSC if exist "%%E\MSBuild\Current\Bin\Roslyn\csc.exe" set "CSC=%%E\MSBuild\Current\Bin\Roslyn\csc.exe"
  )
)
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=..."
```

顺带的好处：源码刻意只用 C# 5 语法，所以就算只找到系统自带的 csc 也能编译，
CI 里不需要额外安装任何东西。

### 2. 没有 .NET Framework 目标包 → 别用 MSBuild，直接用编译器

有些机器**没有装 .NET Framework 目标包**（`Reference Assemblies\...\.NETFramework` 目录不存在），
用 MSBuild 编译传统 csproj 会报 MSB3644。

**做法**：`build.bat` 直接调用 `csc.exe`，只引用运行时目录里的程序集；
写死的引用只有 8 个，全部是 .NET 自带的（**零第三方库、不需要 NuGet**）：

```
System.dll  System.Core.dll  System.Drawing.dll  System.Windows.Forms.dll
System.Web.Extensions.dll          （JSON 配置读写）
System.IO.Compression.dll  System.IO.Compression.FileSystem.dll   （docx 解析）
System.Xml.dll                     （docx 正文提取）
```

### 3. 批处理里绝对不能出现中文

**症状**：`build.bat` 里写了中文注释和提示，执行时报
`'slyn' 不是内部或外部命令`、`'xtensions.dll' 不是...` —— 命令被从中间截断了。

**原因**：cmd.exe 按系统 ANSI 代码页（GBK）逐字节读取 .bat 文件，
UTF-8 的中文字节被当成 GBK 解析，产生半个字符，把后面的引号和命令一起吃掉。
`chcp 65001` 也救不了，因为文件本身是 UTF-8 而 cmd 的解析在 chcp 之前就出错了。

**做法**：所有 `.bat` 一律**纯 ASCII**（提示信息用英文）；中文只放在 README 和界面里。
另外用 `chcp 65001 >nul` + `@echo off` 让控制台能正常显示程序输出。

### 4. 路径里有空格 → 用响应文件（.rsp）

**症状**：`error CS2001: 未能找到源文件"D:\deepseek"`，
源文件列表被空格拆成了好几段。

**原因**：`set "SOURCES=a.cs b.cs"` 然后 `csc %SOURCES%`，
cmd 在传递时会重新分词，带空格的路径直接裂开。

**做法**：生成一个响应文件，每行一个带引号的参数，再 `csc "@file.rsp"`：

```bat
> "%RSP%" echo /nologo
>>"%RSP%" echo /target:winexe
>>"%RSP%" echo /out:"%ROOT%\bin\KeywordClassifier.exe"
>>"%RSP%" echo "%ROOT%\src\Program.cs"
"%CSC%" "@%RSP%"
```

### 5. `%~dp0` 的结尾反斜杠会导致 `tools\tools\`

**症状**：路径变成 `...\tools\tools\build_app.rsp`，找不到文件。

**做法**：循环去掉结尾的反斜杠：

```bat
set "ROOT=%~dp0"
:strip_tail
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%" & goto :strip_tail
```

### 6. 括号块里的 `set` 会延迟生效，别在里面设变量后用

**症状**：`if /i "%~1"=="q" (set "QUIET=1") else (set "ARG1=%~1")` 之后，
`%ARG1%` 一直是空值，`selftest` 参数永远匹配不上。

**原因**：cmd 在执行整个括号块**之前**就把块内所有 `%VAR%` 展开完了。

**做法**：拆成不带括号的单行 `if`：

```bat
set "ARG1=%~1"
if /i "%ARG1%"=="q" set "QUIET=1"
if /i "%ARG1%"=="q" set "ARG1=%~2"
```

### 7. 执行策略可能是 Restricted，脚本文件跑不起来

本机 `Get-ExecutionPolicy -List` 五个作用域全 Undefined，有效策略是 **Restricted**，`.ps1` 双击或直接调用都会报"禁止运行脚本"。

**做法**：自动化入口一律用 `.bat`；确需调用 ps1 时用
`powershell -NoProfile -ExecutionPolicy Bypass -File xxx.ps1`。

---

## 二、界面（最容易反复翻车的地方）

### 8. 绝对不要用坐标硬定位控件

**第一次事故**：早期版本用 `SetBounds(x, y, w, h)` 铺控件，外面套 `SplitContainer`。
运行时 `SplitterDistance` 被钳制，左侧面板只剩 **121 像素宽**，
所有控件叠在一起，用户反馈"挤在一起看不清"。

**做法**：只用 `TableLayoutPanel` / `FlowLayoutPanel` / `Dock`。
三栏结构（顶栏 / 中部 / 底栏）用外层表格的 `RowStyles` 划分，
卡片用 `MakeCard()` 统一生成，内容填进卡片内容区。

### 9. 固定高度的行加起来不能超过容器高度

**第二次事故**：改自动布局后，窗口高 590 像素时三张卡片最低需要 730 像素，
TableLayoutPanel 强行压缩 → **类别列表只剩 36 像素、关键词列表只剩 6 像素、
按钮被拉成 58 像素高**（比正常高一倍多）。

**做法**：
- 关键控件设 `MinimumSize`（列表至少 4 行 / 3 行），物理上不能被压扁；
- 卡片设 `MinimumSize`，与行高对齐；
- 外层面板 `AutoScroll = true` + `AutoScrollMinSize`，空间不够时滚动而不是压缩；
- 窗口 `MinimumSize` 按"内容最低需求 + 边框"设置。

### 10. 按钮不要用 `Dock.Fill`（会被纵向拉伸变形）

**症状**：同一行里如果有个子控件更高，按钮会被拉到同样高，看起来像"胖按钮"。

**做法**：按钮统一 `Dock = DockStyle.Top` + `Height = Ui.S(26)`。

---

## 三、高 DPI（最隐蔽的坑，查了很久）

### 11. 字体随 DPI 放大、像素尺寸不放大 = 文字被切掉

**第三次事故**：用户显示器启用了缩放（注册表 `PerMonitorSettings\DpiValue=2`，
即 125%/150%）。程序开了 DPI 感知，字体放大 1.5 倍，但代码里写死的列宽/按钮宽没变，
于是「类别名：」只显示一个「类」、「删除选中」显示成「删除选」。

**为什么难查**：在 96 DPI 的开发机上**永远复现不出来**。
我用量控件坐标的工具在 96 DPI 下测了各种窗口尺寸，全都是"通过"。

**做法**（`src\Ui.cs`）：
- 启动时算一次缩放比：`Scale = 屏幕DPI / 96`（100%→1.0，125%→1.25，150%→1.5）
- 所有尺寸写 `Ui.S(设计值)`、`Ui.SF(设计值)`（给 ColumnStyle/RowStyle 用）
- 字体统一 `Ui.Font(9F)`，并**关掉 WinForms 的 AutoScale**（`AutoScaleMode.None`），
  否则会和自己的缩放叠加成"缩放两次"
- 启动时探测系统里真实存在的中文字体（雅黑UI → 雅黑 → 黑体 → 宋体），
  字体回退会导致字宽估算错误

### 12. 高 DPI 一定要做自动化验证，否则无法确认

**做法**：`tools\DpiTest.cs` 用反射加载 exe，在 100%/125%/150% 三种比例下
各断言 5 项（文字不截断 / 列表够高 / 按钮不变形 / 不重叠），并渲染 PNG 供肉眼核对。
缩放比例通过环境变量 `WCT_DPI_SCALE` 注入（`Ui.Initialize(true)` 会重读），
不需要真的换显示器。

---

## 四、测试与诊断

### 13. 把"布局不重叠"做成断言，而不是靠眼睛

`tools\LayoutTest.cs` 在 5 种窗口尺寸下遍历控件树，检查：
同级控件是否重叠（排除"完全包含"的容器套子控件）、关键控件是否被压扁、
按钮是否变形、标签文字是否超出宽度。**每次改界面都跑一遍**。

### 14. 截图/探针工具比反复猜有用得多

- `tools\Probe.cs`：打印每个控件的实际坐标尺寸，指出被压扁/文字放不下的控件
- `tools\Shot.cs`：把窗体渲染成 PNG，直接看效果

排查界面问题的正确顺序：**先量 → 再改 → 再量**。

### 15. 测试程序要用控制台子系统

用 `/target:exe`（控制台）而不是 `/target:winexe` 编译测试工具，
否则输出会被吞掉，只剩一句"由于 Exception.ToString() 失败"。

### 16. 反射加载被测 exe 时用 `UnsafeLoadFrom`

**症状**：`Assembly.LoadFrom` 抛 `FileSystemLoadException`，
内层是 `NotSupportedException: 尝试从一个网络位置加载程序集`。

**原因**：路径里有个目录名以 `\h` 开头（例如 `\harness`），.NET Framework 把它当成
UNC 主机名，判定成"网络位置"并拒绝加载（CAS 策略）。

**做法**：测试工具统一用 `Assembly.UnsafeLoadFrom(path)`。

### 17. 某些环境（沙箱 / 云桌面）下文件权限可能被外来条目锁死

**症状**：编译输出报"对路径的访问被拒绝"，但目录明明属于当前用户；
`Remove-Item` 也删不掉。

**原因**：目录 ACL 里残留了早先 AppContainer 包的权限项（`S-1-15-2-*` / `S-1-4-*`），
加上当前用户缺 `WRITE_OWNER`。

**做法**：先用 `icacls 目录` 看一眼有没有异常条目，确认属于"外来包"后再决定是否调整；
如果只是自己的项目，**最省事的办法是换一个输出目录**（本项目就从 `dist\` 换成了 `bin\`），
别和权限较劲。

---

## 五、功能实现上的坑

### 18. "关键词被包含"必须用区间算法，不能边扫边跳

**症状**：A 类含「深红色」、B 类含「红色」，文本「深红色」——
B 类命中数错误地变成 0。

**原因**：扫描到位置 0 命中「深红色」后直接跳过 3 个字符，
B 类在位置 1 的命中永远看不到了。

**做法**（`src\Matcher.cs`）：分四步
1. 逐位置求**每个类别各自**的最长命中长度，收集成区间；
2. 同类内按"起点升序、长的优先"排序，被更长命中包含的短命中丢弃（不重复计数）；
3. 合并跨类别的重叠区间（同一片段只插一次标记）；
4. 把每个类别的命中挂到对应区间上，生成「〔标记〕关键词」。

### 19. `CreateFromDirectory` 生成的 zip 条目用反斜杠

**症状**：docx 读出来是空的。

**原因**：.NET 的 `ZipFile.CreateFromDirectory` 在 Windows 上生成的条目名是
`word\document.xml`（反斜杠），而 OOXML 规范要求正斜杠。

**做法**：读 zip 时统一归一化条目名：

```csharp
string name = e.FullName.Replace('\\', '/').TrimStart('/');
```

### 20. GUI 子系统的 exe 里设置 `Console.OutputEncoding` 会抛异常

**症状**：`--selftest` 什么都不输出。

**原因**：GUI 子系统进程没有控制台，设置输出编码时抛 `IOException`。

**做法**：包一层 try/catch，失败就忽略：

```csharp
try { Console.OutputEncoding = new UTF8Encoding(false); } catch (Exception) { }
```

### 21. 配置文件的路径要基于 exe 而不是工作目录

**做法**：`AppDomain.CurrentDomain.BaseDirectory`（也就是 exe 旁边）。
这样整个程序文件夹可以随便挪动或拷到 U 盘，配置跟着走。
（注意：测试时如果用 `Assembly.LoadFrom` 加载 exe，
`BaseDirectory` 仍是测试程序自己的目录，需要
`AppDomain.CurrentDomain.SetData("APPBASE", 目标目录)`。）

---

## 六、交付与文档

### 22. exe 文件名用英文

界面可以全中文，但**文件名用英文**（`KeywordClassifier.exe`），
因为批处理和 cmd 对中文文件名支持不好，容易踩编码坑。

### 23. 给用户明确的"请点这个文件"路径

编译输出目录可能因为权限、构建方式等原因变化。最终交付时一定要说清楚：
**哪个文件是最新的、哪个是旧的、双击哪一个**。
本项目统一约定：编译输出到 `bin\KeywordClassifier.exe`，只有这一个可执行文件。

### 24. 每一项自动化测试都要能解释"为什么有它"

本项目 167 项断言全部对应真实需求或真实事故，没有一条是凑数的。
这样以后改动时，测试挂了能立刻知道踩到了哪个坑（每套测试的注释里写了事故经过）。

### 25. 图标：用代码画，别引图标库；也别指望覆盖被锁住的旧文件

**做法**：`tools\IconMaker.cs` 直接调 GDI+ 画出 9 个尺寸（16/20/24/32/40/48/64/128/256），
再手写 ICO 文件结构打包（每帧都是 PNG），编译时 `/win32icon:` 嵌进 exe，**零第三方依赖**。
另外两件配套工具：

- `tools\IconPreview.cs`：把各帧排成"浅色背景 / 深色背景"两排对照图，检查小尺寸是否还看得清
- `tools\VerifyExeIcon.cs`：用 `ExtractAssociatedIcon` + `ExtractIconEx` 从 **exe 里**取图标存 PNG，
  确认真的嵌进去了（而不是只生成了 ico 文件）
- `tools\VerifyWindowIcon.cs`：真正启动程序，用 `WM_GETICON` 取回**运行时**的窗口图标，
  确认标题栏和任务栏用的就是它（注意用 `UseShellExecute=false` 启动，否则可能弹"安全警告"卡住）

三个容易忽视的点：

1. **小尺寸要单独简化**：16/24px 上彩色色块只剩 2~3 个像素，会糊成一团。
   本项目的做法是 `size <= 24` 时去掉色块、加粗文字线。
2. **每帧都先按 8 倍超采样再缩小**，否则 16px 上的细线会断。
3. **窗口图标要显式设置**：`Form.Icon` 未必自动用 exe 里的图标，
   稳妥做法是 `Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)`（见 `MainForm.ApplyAppIcon()`）。

**踩到的坑**：早期图标放在项目根目录 `app.ico`，后来这个文件被 ACL 锁住
（父目录继承了一条"拒绝删除子目录和文件"，且当前用户缺 `WRITE_OWNER`），
既不能覆盖也不能删除。解决办法不是硬修权限，而是**把图标换到新目录** `assets\app.ico`
（新文件可正常写入），并让 `build.bat` 只认这个路径。
教训：**遇到删不掉/写不了的旧文件，先换路径，别和权限较劲**。

