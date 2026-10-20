# 测试程序

测试使用外部 PCL 样本，仓库不附带完整启动器或用户文件。

- `MakeAdaptiveFixtures.cs`：从真实的受限样本和已恢复样本生成结构变化、异常分支及正常入口等测试文件。
- `AdaptiveTests.cs`：验证动态定位、输出、原文件保留、重复处理及覆盖保护、错误处理和界面状态。
- `VerifyRuntime.cs`：在 Windows .NET Framework 下加载生成文件的内嵌依赖，对两处相关方法进行 JIT 校验；不启动游戏。

用 .NET Framework `csc.exe` 构建测试程序。Fixture 生成器引用仓库的 `dnlib.dll`；自动化测试引用已构建的工具 EXE 和 `System.Windows.Forms.dll`。运行时参数与入口见各文件的 `Main` 方法。

已验证结果见仓库根目录的验证记录。自动化与 JIT 检查不等价于完成真实游戏安装测试。
