using System.Windows;
namespace PulseMouse.App;
public partial class App : System.Windows.Application
{
    private MainWindow? _main;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _main = new MainWindow();
        MainWindow = _main;
        _main.Show();
    }
}
