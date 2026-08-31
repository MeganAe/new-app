using Avalonia;
using Avalonia.Controls;
using testApp.ViewModels;

namespace testApp.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => WireStorageProvider();
        DataContextChanged += (_, _) => WireStorageProvider();
    }

    private void WireStorageProvider()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.StorageProvider ??= TopLevel.GetTopLevel(this)?.StorageProvider;
        }
    }
}
