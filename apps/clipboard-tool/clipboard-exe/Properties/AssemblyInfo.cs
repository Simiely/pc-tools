using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;

// 显式程序集元数据。
// 背景：WPF temp 项目 (ClipboardExe_*_wpftmp) 与 SDK 自动生成的 AssemblyInfo 冲突，
// 因此 csproj 设 GenerateAssemblyInfo=false —— 但该开关同时会让 csproj 里手写的
// <AssemblyAttribute> ItemGroup 整体失效（属性注入依赖自动生成目标），导致程序集
// 版本恒为 0.0.0.0（日志/About 显示 v0.0.0）。改为本文件显式声明，最可靠。

[assembly: AssemblyTitle("Clipboard")]
[assembly: AssemblyProduct("Clipboard")]
[assembly: AssemblyVersion("0.7.6.0")]
[assembly: AssemblyFileVersion("0.7.6.0")]
[assembly: AssemblyInformationalVersion("0.7.6")]
[assembly: TargetPlatform("Windows")]
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
