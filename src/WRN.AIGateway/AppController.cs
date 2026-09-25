using System;
using System.Collections.Generic;
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
            RegisterPage("Settings", "SettingsPage", "SettingsNavButton");

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
                ShowToast("WTW Claude", "Phase 1 is UI-only. No Claude configuration has been changed.");
            };
            Find<Button>("WRNLaunchButton").Click += delegate
            {
                ShowToast("WRN Claude", "The safe mode-switching engine will be connected in a later phase.");
            };
            Find<Button>("CheckUpdatesButton").Click += delegate
            {
                ShowToast("You're up to date", "Prototype catalogue 2026.09.25 is loaded locally.");
            };
            Find<Button>("ModelRequestButton").Click += delegate
            {
                ShowToast("Model request", "Support workflow will be connected before pilot rollout.");
            };
            Find<Button>("ReportBugButton").Click += delegate
            {
                ShowToast("Report a problem", "Privacy-safe diagnostic export will be added before pilot rollout.");
            };
            Find<Button>("TestConnectionButton").Click += delegate
            {
                ShowToast("Connection check", "OpenRouter checks are intentionally mocked in Phase 1.");
            };
        }

        private void ApplyPersonalisation()
        {
            var firstName = ResolveFirstName();
            var hour = DateTime.Now.Hour;
            var greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            Find<TextBlock>("GreetingText").Text = string.IsNullOrWhiteSpace(firstName) ? greeting : greeting + ", " + firstName;
            Find<TextBlock>("TodayText").Text = DateTime.Now.ToString("dddd, d MMMM");
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
