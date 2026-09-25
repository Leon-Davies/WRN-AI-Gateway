using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace WRN.AIGateway
{
    internal sealed class AppController
    {
        private readonly Window _window;
        private readonly string _baseDir;
        private readonly Dictionary<string, FrameworkElement> _pages = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer();
        private readonly DispatcherTimer _typingTimer = new DispatcherTimer();
        private string _greetingTarget = string.Empty;
        private int _greetingIndex;
        private Border _toast;
        private TextBlock _toastTitle;
        private TextBlock _toastMessage;

        public AppController(Window window, string baseDir)
        {
            _window = window;
            _baseDir = baseDir;
        }

        public void Initialize()
        {
            ConfigureWindowChrome();
            WireWindowControls();
            WireNavigation();
            WireActions();
            ApplyPersonalisation();
            TryLoadBrandHero();
            ConfigureToast();
            ShowPage("Home");
        }

        private void ConfigureWindowChrome()
        {
            WindowChrome.SetWindowChrome(_window, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(12),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false
            });
        }

        private void WireWindowControls()
        {
            var titleBar = Find<Border>("TitleBar");
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                    _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else
                    _window.DragMove();
            };

            Find<Button>("MinimizeButton").Click += delegate { _window.WindowState = WindowState.Minimized; };
            Find<Button>("MaximizeButton").Click += delegate
            {
                _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            };
            Find<Button>("CloseButton").Click += delegate { _window.Close(); };
        }

        private void WireNavigation()
        {
            RegisterPage("Home", "HomePage", "HomeNavButton");
            RegisterPage("Models", "ModelsPage", "ModelsNavButton");
            RegisterPage("Updates", "UpdatesPage", "UpdatesNavButton");
            RegisterPage("Support", "SupportPage", "SupportNavButton");

            foreach (var pair in _navButtons)
            {
                var key = pair.Key;
                pair.Value.Click += delegate { ShowPage(key); };
            }
        }

        private void RegisterPage(string key, string pageName, string buttonName)
        {
            _pages[key] = Find<FrameworkElement>(pageName);
            _navButtons[key] = Find<Button>(buttonName);
        }

        private void WireActions()
        {
            Find<Button>("WTWLaunchButton").Click += delegate
            {
                ShowToast("WTW Claude", "WTW launch wiring is not enabled yet.");
            };
            Find<Button>("WRNLaunchButton").Click += delegate
            {
                ShowToast("WRN Claude", "WRN launch wiring is not enabled yet.");
            };
            Find<Button>("ModelsShortcutButton").Click += delegate
            {
                ShowPage("Models");
            };
            Find<Button>("UpdatesShortcutButton").Click += delegate
            {
                ShowPage("Updates");
            };
            Find<Button>("ArtificialAnalysisButton").Click += delegate
            {
                OpenExternalUrl("https://artificialanalysis.ai/models?cost=intelligence-vs-cost-per-task");
            };
            Find<Button>("ModelRequestButton").Click += delegate
            {
                Clipboard.SetText(
                    "WRN AI model request" + Environment.NewLine + Environment.NewLine +
                    "Model:" + Environment.NewLine +
                    "Use case:" + Environment.NewLine +
                    "Why it would help:");
                ShowToast("Request copied", "Paste the template into a message to WRN AI support.");
            };
            Find<Button>("ReportBugButton").Click += delegate
            {
                Clipboard.SetText(
                    "WRN AI Gateway issue" + Environment.NewLine + Environment.NewLine +
                    "What happened:" + Environment.NewLine +
                    "What I expected:" + Environment.NewLine +
                    "When: " + DateTime.Now.ToString("g"));
                ShowToast("Issue template copied", "Paste it into a message to WRN AI support.");
            };
        }

        private void ApplyPersonalisation()
        {
            var firstName = ResolveFirstName();
            var hour = DateTime.Now.Hour;
            var greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            _greetingTarget = string.IsNullOrWhiteSpace(firstName) ? greeting : greeting + ", " + firstName;

            var greetingText = Find<TextBlock>("GreetingText");
            if (!SystemParameters.ClientAreaAnimation)
            {
                greetingText.Text = _greetingTarget;
                return;
            }

            greetingText.Text = string.Empty;
            _greetingIndex = 0;
            _typingTimer.Interval = TimeSpan.FromMilliseconds(42);
            _typingTimer.Tick += delegate
            {
                if (_greetingIndex >= _greetingTarget.Length)
                {
                    _typingTimer.Stop();
                    return;
                }

                greetingText.Text += _greetingTarget[_greetingIndex];
                _greetingIndex++;
            };
            _typingTimer.Start();
        }

        private static string ResolveFirstName()
        {
            try
            {
                using (var principal = UserPrincipal.Current)
                {
                    if (principal != null && !string.IsNullOrWhiteSpace(principal.GivenName))
                        return principal.GivenName.Trim();
                    if (principal != null && !string.IsNullOrWhiteSpace(principal.DisplayName))
                    {
                        var pieces = principal.DisplayName.Trim().Split(' ');
                        if (pieces.Length > 0) return pieces[0];
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private void TryLoadBrandHero()
        {
            var candidates = new[]
            {
                Path.Combine(_baseDir, "assets", "wrn-hero.png"),
                Path.Combine(_baseDir, "local-assets", "wrn-hero.png")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    var image = Find<Image>("HeroImage");
                    image.Source = bitmap;
                    image.Visibility = Visibility.Visible;
                    Find<Border>("HeroImageOverlay").Visibility = Visibility.Visible;
                    return;
                }
                catch { }
            }
        }

        private static void OpenExternalUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show(
                    "The link could not be opened in your browser.",
                    "WRN AI Gateway",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void ConfigureToast()
        {
            _toast = Find<Border>("ToastBorder");
            _toastTitle = Find<TextBlock>("ToastTitle");
            _toastMessage = Find<TextBlock>("ToastMessage");
            _toastTimer.Interval = TimeSpan.FromSeconds(4);
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _toast.Visibility = Visibility.Collapsed;
            };
        }

        private void ShowToast(string title, string message)
        {
            _toastTitle.Text = title;
            _toastMessage.Text = message;
            _toast.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ShowPage(string key)
        {
            foreach (var page in _pages.Values) page.Visibility = Visibility.Collapsed;
            foreach (var button in _navButtons.Values) button.Tag = "inactive";

            _pages[key].Visibility = Visibility.Visible;
            _navButtons[key].Tag = "active";
            Find<TextBlock>("PageHeading").Text = key == "Home" ? "Home" : key;
        }

        private T Find<T>(string name) where T : FrameworkElement
        {
            var value = _window.FindName(name) as T;
            if (value == null) throw new InvalidOperationException("UI element not found: " + name);
            return value;
        }
    }
}
