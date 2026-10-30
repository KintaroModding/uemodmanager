using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;
#if CATALOG
using UEManager.UI.Catalog;
#endif

namespace UEManager.UI.Tests;

[Collection("Language")]
public class LanguageSwitchTests
{
    [Fact]
    public void Existing_window_switches_languages_without_losing_state_and_cached_errors_follow_it()
    {
        Exception? failure = null;
        var stage = "starting the STA thread";
        var thread = new Thread(() =>
        {
            try { VerifySwitch(value => stage = value); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"The WPF language check did not finish: {stage}.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifySwitch(Action<string> stage)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var originalLanguage = Localizer.Current.Language;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var folder = Path.Combine(Path.GetTempPath(), "uemm-ui-test-" + Guid.NewGuid().ToString("N"));
        // Use an inert WPF host: constructing the real App queues its production startup on the dispatcher.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            stage("loading application resources");
            foreach (var resource in new[] { "Theme/Palette.xaml", "Theme/Controls.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/UEModManager;component/{resource}", UriKind.Relative),
                });
#if CATALOG
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/UEModManager;component/Catalog/CatalogResources.xaml", UriKind.Relative),
            });
#endif
            Localizer.Current.SetLanguage("en");
            var paths = new AppPaths(folder);
            paths.EnsureWritable();
            stage("creating the view model");
            var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths,
                new ArtSet(null, null));
            model.SelectedPage = "Settings";
            model.SearchText = "蓝图";
            var row = new ModItemViewModel(model, new ModRecord
            {
                Id = "blueprint",
                Name = "User mod {0} 中文",
                Files = [new ModFileRecord { Target = DeployTarget.LogicMods, FileName = "User.pak" }],
            }, Path.Combine(folder, "library"));
            model.Mods.Add(row);
            model.ModsView.Refresh();
            Assert.True(model.ModsView.IsEmpty);

            stage("constructing the window");
            window = new MainWindow { DataContext = model };
            FlushBindings();
            var surface = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            stage("laying out the window");
            surface.Measure(new Size(1360, 800));
            surface.Arrange(new Rect(0, 0, 1360, 800));
            FlushBindings();
            var selector = Descendants(surface).OfType<ComboBox>().Single();
            Assert.Equal("en", selector.SelectedValue);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "Settings");

            // The control changes the existing model and saves the choice, without replacing the window or page.
            stage("switching to Chinese");
            selector.SelectedValue = "zh-CN";
            FlushBindings();
            Assert.Same(model, window.DataContext);
            Assert.Equal("Settings", model.SelectedPage);
            Assert.Equal("蓝图", model.SearchText);
            Assert.Equal("zh-CN", AppSettings.Load(paths).Language);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "设置");
            Assert.Equal("zh-CN", window.Language.IetfLanguageTag, ignoreCase: true);
            Assert.Equal("影之刃零", model.GameDisplayName);
            Assert.Equal("User mod {0} 中文", row.Name);
            Assert.Equal("蓝图", row.KindText);
            Assert.Single(model.ModsView.Cast<ModItemViewModel>());
            Assert.True(row.Matches("Blueprint"));

            using var http = new HttpClient(new MissingHandler());
            var update = new AppUpdateViewModel(model, http, paths, "https://example.invalid/latest", "uemm_");
            stage("checking an unavailable release");
            WaitFor(update.CheckAsync());
            Assert.Equal("尚无发布版本。", update.StatusHint);
#if CATALOG
            var catalog = new CatalogViewModel(model, http, "https://example.invalid/catalog", null, true,
                CatalogSortField.Name, false);
            stage("loading an unavailable catalog");
            WaitFor(catalog.LoadAsync());
            Assert.StartsWith("测试列表：", catalog.TestCatalogText);
            Assert.StartsWith("无法加载 openmods 列表：", catalog.Error);
#endif
            stage("switching back to English");
            selector.SelectedValue = "en";
            FlushBindings();
            Assert.Equal("en", AppSettings.Load(paths).Language);
            Assert.Equal("Settings", model.SelectedPage);
            Assert.Equal("蓝图", model.SearchText);
            Assert.True(model.ModsView.IsEmpty);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "Settings");
            Assert.Equal("No release is published yet.", update.StatusHint);
#if CATALOG
            Assert.StartsWith("Test list:", catalog.TestCatalogText);
            Assert.StartsWith("The openmods list could not be loaded:", catalog.Error);
#endif
        }
        finally
        {
            stage("closing the window");
            window?.Close();
            Localizer.Current.SetLanguage(originalLanguage);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefault;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
            stage("shutting down the test application");
            app.Shutdown();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private sealed class MissingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
    }
}
