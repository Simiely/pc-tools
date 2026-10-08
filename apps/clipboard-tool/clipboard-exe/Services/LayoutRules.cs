// Services/LayoutRules.cs - 布局规则纯函数（M1 抽离：把"窗口宽度 → 布局参数"模式立起来）
// 对齐 Web 版 .view：max-width 三档（<1280→960 / ≥1280→1440 / ≥1920→1920）。
// 纯函数无 UI 依赖，可单测；卡片墙列数（Web .list auto-fill minmax(280px,1fr)）同族规则，M3a 起在此加 ColumnsFor。
namespace ClipboardExe.Services;

public static class LayoutRules
{
    /// <summary>按客户区宽度取内容区最大宽度（三档，对齐 Web .view max-width）。</summary>
    public static double MaxWidthFor(double clientWidth) =>
        clientWidth >= 1920 ? 1920 : clientWidth >= 1280 ? 1440 : 960;

    /// <summary>
    /// 卡片墙列数（镜像 Web .list）：
    /// · auto（maxColumns≤0）：CSS `repeat(auto-fill, minmax(280px,1fr))`——基准卡片最小宽 280 + gap 16，
    ///   自适应几列；取整 (w+gap)/(280+gap) 对齐 auto-fill 语义（278px 仍 1 列，296px 才 2 列），钳 1~4。
    /// · 锁定（maxColumns 1~4）：精确强制该列数（镜像 Web data-cols=N → `repeat(N,minmax(0,1fr))`），
    ///   卡片随列数缩窄、不被容器宽度压制——窄窗锁 3/4 也要真 3/4 列（v0.7.2 修复：旧实现把 maxColumns 当"上限"，
    ///   窄窗被 280 下限压回 2 列，与 Web 双版本不一致）。
    /// </summary>
    public static int ColumnsFor(double contentWidth, int maxColumns = 0)
    {
        const double min = 280.0, gap = 16.0;
        if (maxColumns > 0)
            return Math.Clamp(maxColumns, 1, 4); // 锁定：精确 N 列
        var n = (int)Math.Floor((contentWidth + gap) / (min + gap)); // 自动：minmax(280px,1fr)
        return Math.Clamp(n, 1, 4);
    }
}
