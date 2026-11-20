using System.Windows;
using Cells.Core;

namespace Cells;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
        {
            Scan.WriteReport();
            Shutdown();
        }
    }
}
