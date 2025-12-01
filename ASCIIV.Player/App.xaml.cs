using System.IO;
using System.Windows;

namespace ASCIIV.Player;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        string? initialFile = null;
        if (e.Args.Length > 0 && File.Exists(e.Args[0])) {
            initialFile = e.Args[0];
        }

        PlayerWindow window = new(initialFile);
        window.Show();
    }
}

