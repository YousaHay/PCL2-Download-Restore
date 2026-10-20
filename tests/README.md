# 测试程序

测试使用外部 PCL 样本，仓库不附带完整启动器或用户文件。

- `MakeAdaptiveFixtures.cs`：从真实的受限样本和已恢复样本生成结构变化、异常分支及正常入口等测试文件。
- `AdaptiveTests.cs`：验证动态定位、输出、原文件保留、重复处理及覆盖保护、错误处理和界面状态。
- `VerifyRuntime.cs`：在 Windows .NET Framework 下加载生成文件的内嵌依赖，对原版下载、版本修改、两个整合包入口和在线列表方法进行 JIT 校验；不启动游戏。
- `ModpackRuntimeTests.cs`：运行生成 EXE 中的原有包解析器与解压逻辑，验证根目录/包裹目录、在线安装与另存为回调，以及普通资源行为。
- `MakeModpackProbes.cs`：生成只用于测试的受控文件选择/带启动器分流探针镜像，不作为修复后的启动器发布。

用 .NET Framework `csc.exe` 构建测试程序。Fixture/Probe 生成器引用仓库的 `dnlib.dll`；自动化测试引用已构建的工具 EXE 和 `System.Windows.Forms.dll`。整合包运行测试另引用 System.IO.Compression、System.IO.Compression.FileSystem、System.Xaml 和 WPF 的 PresentationFramework、PresentationCore、WindowsBase。测试程序声明 .NET Framework 4.8 目标，以正确处理 PCL 使用的扩展路径格式。运行时参数与入口见各文件的 `Main` 方法。

已验证结果见仓库根目录的验证记录。自动化与 JIT 检查不等价于完成真实游戏安装测试。
