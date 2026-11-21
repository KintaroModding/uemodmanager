using System.Windows.Controls;

namespace UEManager.UI.Views;

public partial class InstallDialog : UserControl
{
    public InstallDialog()
    {
        InitializeComponent();
        // The main window keeps this view and gives it each new review: it starts at the top, so the choices near the
        // top are in view, not where the last review was scrolled to (the user's report, 2026-11-21).
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not null)
                Body.ScrollToTop();
        };
    }
}
