// tools/difftest/AppShim.cs - 生产代码 Storage.SaveClips 引用 ClipboardExe.App.AppName/AppVersion(WPF 主类)，
// difftest 是纯控制台不链 App.xaml.cs，这里提供同命名空间 shim 满足编译。
namespace ClipboardExe;

public static class App
{
    public const string AppName = "clipboard-tool";
    public static string AppVersion => "0.0.0-difftest";
}
