using Avalonia.Controls;
using AVASpyVsSpy.Views;

namespace AVASpyVsSpy;

public partial class MainWindow : Window
{
    public MainWindow() : this([]) { }

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Content = new GameCanvas(silent: args.Contains("--no-audio"));
    }
}
