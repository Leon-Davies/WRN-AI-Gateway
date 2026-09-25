using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace WRN.AIGateway
{
    internal sealed class AppController
    {
        private readonly Window _window;
        private readonly string _baseDir;
        private readonly string _stateRoot;
        private readonly Dictionary<string, FrameworkElement> _pages = new Dictionary<string, FrameworkElement>();
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer();
        private readonly DispatcherTimer _typingTimer = new DispatcherTimer();
        private readonly DispatcherTimer _heroTimer = new DispatcherTimer();
        private readonly DispatcherTimer _catalogueTimer = new DispatcherTimer();
        private CatalogueLoadResult _catalogueLoad;
        private bool _catalogueRefreshInFlight;
        private bool _credentialOperationInFlight;
        private readonly List<BitmapImage> _heroImages = new List<BitmapImage>();
        private readonly Dictionary<string, ModelInfo> _models = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        private string _greetingTarget = string.Empty;
        private int _greetingIndex;
        private int _heroIndex;
        private bool _heroAActive = true;
        private Image _heroA;
        private Image _heroB;
        private Border _toast;
        private TextBlock _toastTitle;
        private TextBlock _toastMessage;

        public AppController(Window window, string baseDir)
        {
            _window = window;
            _baseDir = baseDir;
            _stateRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WRN-AI-Gateway");
        }

        public void Initialize()
        {
            LoadCatalogue();
            ConfigureWindowChrome();
            WireWindowControls();
            WireNavigation();
            WireActions();
            ApplyPersonalisation();
            TryLoadBrandHero();
            ConfigureToast();
            RenderCatalogue();
            RefreshCredentialStatus();
            ShowPage("Home");
            ConfigureCatalogueRefresh();
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
            RegisterPage("Settings", "SettingsPage", "SettingsNavButton");
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
                var credential =
                    OpenRouterCredentialStore.Inspect(_stateRoot);
                if (!credential.Configured
                    || !credential.Decryptable)
                {
                    ShowPage("Settings");
                    ShowToast(
                        "Connect OpenRouter",
                        "Connect your OpenRouter key before WRN Claude can be enabled on this PC.");
                    return;
                }

                ShowToast(
                    "WRN Claude",
                    "Your OpenRouter connection is ready. Claude switching remains disabled until managed-Claude qualification is complete.");
            };
            Find<Button>("ModelsShortcutButton").Click += delegate { ShowPage("Models"); };
            Find<Button>("UpdatesShortcutButton").Click += delegate { ShowPage("Updates"); };

            Find<Button>("CredentialConnectButton").Click += delegate
            {
                ShowOpenRouterCredentialDialog();
            };
            Find<Button>("CredentialTestButton").Click += delegate
            {
                TestOpenRouterCredential();
            };
            Find<Button>("CredentialRemoveButton").Click += delegate
            {
                ShowRemoveOpenRouterCredentialDialog();
            };

            Find<Button>("ModelRequestButton").Click += delegate
            {
                ShowSupportTemplate(
                    "Request a model",
                    "WRN AI model request",
                    "WRN AI model request" + Environment.NewLine + Environment.NewLine +
                    "Model:" + Environment.NewLine +
                    "What would you like to use it for?" + Environment.NewLine +
                    "Why would it be useful for your work?" + Environment.NewLine +
                    "Anything else we should know?");
            };

            Find<Button>("ReportBugButton").Click += delegate
            {
                ShowSupportTemplate(
                    "Report a problem",
                    "WRN AI Gateway issue",
                    "WRN AI Gateway issue" + Environment.NewLine + Environment.NewLine +
                    "What happened?" + Environment.NewLine +
                    "What did you expect to happen?" + Environment.NewLine +
                    "Which mode were you using? (WTW Claude / WRN Claude)" + Environment.NewLine +
                    "Which model were you using, if relevant?" + Environment.NewLine +
                    "When did it happen?" + Environment.NewLine +
                    Environment.NewLine +
                    "Please do not include API keys, passwords, or confidential prompt/file contents.");
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
            _heroA = Find<Image>("HeroImageA");
            _heroB = Find<Image>("HeroImageB");

            var searchDirectories = new[]
            {
                Path.Combine(_baseDir, "assets"),
                Path.Combine(_baseDir, "local-assets")
            };

            var paths = new List<string>();
            foreach (var directory in searchDirectories)
            {
                if (!Directory.Exists(directory)) continue;

                var numbered = Directory.GetFiles(directory, "wrn-hero-*.png");
                Array.Sort(numbered, StringComparer.OrdinalIgnoreCase);
                if (numbered.Length > 0)
                {
                    paths.AddRange(numbered);
                    break;
                }

                var legacy = Path.Combine(directory, "wrn-hero.png");
                if (File.Exists(legacy))
                {
                    paths.Add(legacy);
                    break;
                }
            }

            foreach (var path in paths)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _heroImages.Add(bitmap);
                }
                catch { }
            }

            if (_heroImages.Count == 0) return;

            _heroIndex = 0;
            _heroA.Source = _heroImages[0];
            _heroA.Visibility = Visibility.Visible;
            _heroA.Opacity = 0.92;
            _heroB.Visibility = Visibility.Visible;
            _heroB.Opacity = 0;
            Find<Border>("HeroImageOverlay").Visibility = Visibility.Visible;

            if (_heroImages.Count > 1)
            {
                _heroTimer.Interval = TimeSpan.FromSeconds(6);
                _heroTimer.Tick += delegate { AdvanceHeroImage(); };
                _heroTimer.Start();
            }
        }

        private void AdvanceHeroImage()
        {
            if (_heroImages.Count < 2 || _heroA == null || _heroB == null) return;

            _heroIndex = (_heroIndex + 1) % _heroImages.Count;
            var current = _heroAActive ? _heroA : _heroB;
            var next = _heroAActive ? _heroB : _heroA;

            next.Source = _heroImages[_heroIndex];
            next.Visibility = Visibility.Visible;

            if (!SystemParameters.ClientAreaAnimation)
            {
                current.Opacity = 0;
                next.Opacity = 0.92;
                _heroAActive = !_heroAActive;
                return;
            }

            current.BeginAnimation(UIElement.OpacityProperty, null);
            next.BeginAnimation(UIElement.OpacityProperty, null);
            current.Opacity = 0.92;
            next.Opacity = 0;

            var duration = TimeSpan.FromMilliseconds(750);
            var fadeOut = new DoubleAnimation(0.92, 0, duration);
            var fadeIn = new DoubleAnimation(0, 0.92, duration);

            fadeIn.Completed += delegate
            {
                current.BeginAnimation(UIElement.OpacityProperty, null);
                next.BeginAnimation(UIElement.OpacityProperty, null);
                current.Opacity = 0;
                next.Opacity = 0.92;
            };

            current.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            next.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            _heroAActive = !_heroAActive;
        }

        private void RefreshCredentialStatus()
        {
            CredentialStatus status;
            try
            {
                status =
                    OpenRouterCredentialStore.Inspect(
                        _stateRoot);
            }
            catch
            {
                ApplyCredentialStatus(
                    "Needs attention",
                    "The saved OpenRouter connection could not be inspected.",
                    "Connection status unavailable",
                    "Local gateway status unavailable.",
                    "#FDECEC",
                    "#9C3D3D",
                    false);
                return;
            }

            var ready =
                status.Configured
                && status.Decryptable;

            if (!status.Configured)
            {
                ApplyCredentialStatus(
                    "Not connected",
                    "Connect an OpenRouter API key before WRN Claude can use the WRN model service.",
                    "No OpenRouter key saved",
                    status.GatewayConfigured
                        ? "Local gateway setup is ready."
                        : "Local gateway will be configured automatically.",
                    "#FFF4E8",
                    "#985A20",
                    false);
            }
            else if (!status.Decryptable)
            {
                ApplyCredentialStatus(
                    "Needs attention",
                    "The saved OpenRouter key cannot be unlocked for this Windows account. Replace it to continue.",
                    "Saved key cannot be unlocked",
                    status.GatewayConfigured
                        ? "Local gateway setup is ready."
                        : "Local gateway will be configured automatically.",
                    "#FDECEC",
                    "#9C3D3D",
                    false);
            }
            else
            {
                var validated =
                    FormatValidatedAt(
                        status.ValidatedAtUtc);
                ApplyCredentialStatus(
                    "Connected",
                    "Your OpenRouter key is protected and ready for WRN Claude.",
                    validated,
                    status.GatewayConfigured
                        ? "Local gateway setup is ready."
                        : "Local gateway will be configured automatically.",
                    "#EAF6EF",
                    "#2E6A49",
                    true);
            }

            Find<Button>("CredentialConnectButton").Content =
                ready
                    ? "Replace key"
                    : "Connect OpenRouter";
        }

        private void ApplyCredentialStatus(
            string status,
            string detail,
            string validated,
            string gateway,
            string badgeBackground,
            string badgeForeground,
            bool canTestOrRemove)
        {
            Find<TextBlock>("CredentialStatusText").Text =
                status;
            Find<TextBlock>("CredentialStatusDetail").Text =
                detail;
            Find<TextBlock>("CredentialValidatedText").Text =
                validated;
            Find<TextBlock>("CredentialGatewayText").Text =
                gateway;

            Find<Border>("CredentialStatusBadge").Background =
                Brush(badgeBackground);
            Find<TextBlock>("CredentialStatusText").Foreground =
                Brush(badgeForeground);

            Find<Button>("CredentialTestButton").IsEnabled =
                canTestOrRemove
                && !_credentialOperationInFlight;
            Find<Button>("CredentialRemoveButton").IsEnabled =
                canTestOrRemove
                && !_credentialOperationInFlight;
            Find<Button>("CredentialConnectButton").IsEnabled =
                !_credentialOperationInFlight;
        }

        private static string FormatValidatedAt(
            string value)
        {
            DateTimeOffset parsed;
            if (!string.IsNullOrWhiteSpace(value)
                && DateTimeOffset.TryParse(
                    value,
                    out parsed))
            {
                return "Validated "
                    + parsed.ToLocalTime().ToString(
                        "d MMM yyyy, HH:mm",
                        CultureInfo.InvariantCulture);
            }

            return "Saved key is ready";
        }

        private void ShowOpenRouterCredentialDialog()
        {
            var current =
                OpenRouterCredentialStore.Inspect(
                    _stateRoot);
            var replacing =
                current.Configured
                && current.Decryptable;

            var dialog = CreateDialog(
                replacing
                    ? "Replace OpenRouter key"
                    : "Connect OpenRouter",
                600,
                400);

            var body =
                new StackPanel
                {
                    Margin = new Thickness(28)
                };

            body.Children.Add(
                new TextBlock
                {
                    Text = replacing
                        ? "Replace OpenRouter key"
                        : "Connect OpenRouter",
                    FontSize = 24,
                    FontWeight =
                        FontWeights.SemiBold,
                    Foreground = Brush("#302536")
                });

            body.Children.Add(
                new TextBlock
                {
                    Text = replacing
                        ? "Your existing key stays active unless the replacement is successfully validated."
                        : "Paste the OpenRouter API key you were given. It will be validated before it is saved.",
                    FontSize = 13.5,
                    Foreground = Brush("#6F6574"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin =
                        new Thickness(0, 7, 0, 20)
                });

            body.Children.Add(
                new TextBlock
                {
                    Text = "OpenRouter API key",
                    FontSize = 13,
                    FontWeight =
                        FontWeights.SemiBold,
                    Foreground = Brush("#3B3340"),
                    Margin =
                        new Thickness(0, 0, 0, 7)
                });

            var password =
                new PasswordBox
                {
                    Height = 46,
                    Padding = new Thickness(
                        12,
                        10,
                        12,
                        10),
                    FontSize = 14,
                    BorderBrush = Brush("#D7CFDC"),
                    BorderThickness =
                        new Thickness(1),
                    Background = Brushes.White,
                    Foreground = Brush("#302536")
                };

            body.Children.Add(
                new Border
                {
                    Background = Brushes.White,
                    BorderBrush = Brush("#D7CFDC"),
                    BorderThickness =
                        new Thickness(1),
                    CornerRadius =
                        new CornerRadius(9),
                    Child = password
                });

            var resultText =
                new TextBlock
                {
                    Text =
                        "The key is protected for your Windows account and is never displayed again after saving.",
                    FontSize = 12.5,
                    Foreground = Brush("#817787"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin =
                        new Thickness(0, 10, 0, 18)
                };
            body.Children.Add(resultText);

            var actions =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Right
                };

            var cancel =
                DialogButton("Cancel", false);
            cancel.Click += delegate
            {
                if (!_credentialOperationInFlight)
                    dialog.Close();
            };
            actions.Children.Add(cancel);

            var save =
                DialogButton(
                    replacing
                        ? "Validate & replace"
                        : "Validate & save",
                    true);
            save.Margin =
                new Thickness(10, 0, 0, 0);
            save.Click += delegate
            {
                if (_credentialOperationInFlight)
                    return;

                var candidate =
                    (password.Password
                        ?? string.Empty).Trim();
                password.Clear();

                if (candidate.Length < 16)
                {
                    resultText.Text =
                        "Enter a valid OpenRouter API key.";
                    resultText.Foreground =
                        Brush("#9C3D3D");
                    candidate = null;
                    password.Focus();
                    return;
                }

                _credentialOperationInFlight = true;
                save.IsEnabled = false;
                cancel.IsEnabled = false;
                password.IsEnabled = false;
                resultText.Text =
                    "Checking the key with OpenRouter…";
                resultText.Foreground =
                    Brush("#5A1A75");

                var task = Task.Run(
                    delegate
                    {
                        try
                        {
                            return
                                OpenRouterCredentialStore
                                    .ValidateAndSave(
                                        candidate,
                                        _stateRoot);
                        }
                        finally
                        {
                            candidate = null;
                        }
                    });

                task.ContinueWith(
                    delegate(Task<OpenRouterKeyValidation> completed)
                    {
                        _window.Dispatcher.BeginInvoke(
                            new Action(
                                delegate
                                {
                                    _credentialOperationInFlight = false;

                                    if (completed.Status
                                            == TaskStatus.RanToCompletion
                                        && completed.Result != null
                                        && completed.Result.Valid)
                                    {
                                        try
                                        {
                                            GatewayLifecycle.StopOwned(
                                                _baseDir,
                                                _stateRoot);
                                        }
                                        catch
                                        {
                                        }

                                        RefreshCredentialStatus();
                                        dialog.Close();
                                        ShowToast(
                                            "OpenRouter connected",
                                            "Your key is protected and ready for WRN Claude.");
                                        return;
                                    }

                                    save.IsEnabled = true;
                                    cancel.IsEnabled = true;
                                    password.IsEnabled = true;
                                    password.Focus();

                                    var validation =
                                        completed.Status
                                            == TaskStatus.RanToCompletion
                                            ? completed.Result
                                            : null;

                                    resultText.Text =
                                        FriendlyCredentialError(
                                            validation == null
                                                ? "KEY_VALIDATION_FAILED"
                                                : validation.Status);
                                    resultText.Foreground =
                                        Brush("#9C3D3D");
                                    RefreshCredentialStatus();
                                }));
                    });
            };
            actions.Children.Add(save);
            body.Children.Add(actions);

            dialog.Content =
                CreateDialogShell(
                    dialog,
                    body);
            dialog.Loaded += delegate
            {
                password.Focus();
            };
            dialog.ShowDialog();
        }

        private void TestOpenRouterCredential()
        {
            if (_credentialOperationInFlight)
                return;

            _credentialOperationInFlight = true;
            ApplyCredentialBusyState(
                "Testing the saved key with OpenRouter…");

            Task.Run(
                delegate
                {
                    return
                        OpenRouterCredentialStore
                            .TestStored(_stateRoot);
                })
                .ContinueWith(
                    delegate(Task<OpenRouterKeyValidation> completed)
                    {
                        _window.Dispatcher.BeginInvoke(
                            new Action(
                                delegate
                                {
                                    _credentialOperationInFlight = false;
                                    RefreshCredentialStatus();

                                    if (completed.Status
                                            == TaskStatus.RanToCompletion
                                        && completed.Result != null
                                        && completed.Result.Valid)
                                    {
                                        ShowToast(
                                            "Connection ready",
                                            "OpenRouter accepted the saved key.");
                                    }
                                    else
                                    {
                                        var validation =
                                            completed.Status
                                                == TaskStatus.RanToCompletion
                                                ? completed.Result
                                                : null;
                                        Find<TextBlock>(
                                            "CredentialStatusDetail").Text =
                                            FriendlyCredentialError(
                                                validation == null
                                                    ? "KEY_VALIDATION_FAILED"
                                                    : validation.Status);
                                        Find<TextBlock>(
                                            "CredentialStatusText").Text =
                                            "Needs attention";
                                        Find<Border>(
                                            "CredentialStatusBadge").Background =
                                            Brush("#FDECEC");
                                        Find<TextBlock>(
                                            "CredentialStatusText").Foreground =
                                            Brush("#9C3D3D");
                                    }
                                }));
                    });
        }

        private void ApplyCredentialBusyState(
            string message)
        {
            Find<TextBlock>(
                "CredentialStatusDetail").Text =
                message;
            Find<Button>(
                "CredentialConnectButton").IsEnabled =
                false;
            Find<Button>(
                "CredentialTestButton").IsEnabled =
                false;
            Find<Button>(
                "CredentialRemoveButton").IsEnabled =
                false;
        }

        private void
            ShowRemoveOpenRouterCredentialDialog()
        {
            var status =
                OpenRouterCredentialStore.Inspect(
                    _stateRoot);
            if (!status.Configured)
                return;

            var dialog = CreateDialog(
                "Remove OpenRouter key",
                560,
                350);
            var body =
                new StackPanel
                {
                    Margin = new Thickness(28)
                };

            body.Children.Add(
                new TextBlock
                {
                    Text = "Remove OpenRouter key?",
                    FontSize = 24,
                    FontWeight =
                        FontWeights.SemiBold,
                    Foreground = Brush("#302536")
                });
            body.Children.Add(
                new TextBlock
                {
                    Text =
                        "WRN Claude will no longer be able to use OpenRouter on this PC until another key is connected. Your model catalogue and local app settings are not removed.",
                    FontSize = 13.5,
                    Foreground = Brush("#6F6574"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin =
                        new Thickness(0, 9, 0, 24)
                });

            var actions =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Right
                };

            var cancel =
                DialogButton("Cancel", false);
            cancel.Click += delegate
            {
                dialog.Close();
            };
            actions.Children.Add(cancel);

            var remove =
                DialogButton("Remove key", true);
            remove.Margin =
                new Thickness(10, 0, 0, 0);
            remove.Click += delegate
            {
                try
                {
                    GatewayLifecycle.StopOwned(
                        _baseDir,
                        _stateRoot);
                    OpenRouterCredentialStore.Remove(
                        _stateRoot);
                    dialog.Close();
                    RefreshCredentialStatus();
                    ShowToast(
                        "OpenRouter key removed",
                        "WRN Claude is no longer connected to OpenRouter on this PC.");
                }
                catch
                {
                    ShowToast(
                        "Could not remove the key",
                        "WRN AI Gateway could not safely stop its local connection. Try again after closing WRN Claude.");
                }
            };
            actions.Children.Add(remove);
            body.Children.Add(actions);

            dialog.Content =
                CreateDialogShell(
                    dialog,
                    body);
            dialog.ShowDialog();
        }

        private static string FriendlyCredentialError(
            string status)
        {
            switch (status)
            {
                case "KEY_UNAUTHORIZED":
                    return "OpenRouter did not accept that key. Check the key and try again.";
                case "INFERENCE_KEY_REQUIRED":
                    return "Use a normal OpenRouter inference API key rather than a management or provisioning key.";
                case "KEY_VALIDATION_NETWORK_ERROR":
                    return "OpenRouter could not be reached. Check your connection and try again.";
                case "CREDENTIAL_NOT_CONFIGURED":
                    return "No OpenRouter key is saved on this PC.";
                default:
                    return "The key could not be validated. Nothing was changed.";
            }
        }

        private void ShowModelDetails(string key)
        {
            ModelInfo model;
            if (!_models.TryGetValue(key, out model)) return;

            var dialog = CreateDialog(model.Name, 650, 570);
            var body = new StackPanel { Margin = new Thickness(28) };

            var heading = new TextBlock
            {
                Text = model.Name,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#302536")
            };
            body.Children.Add(heading);

            var maker = new TextBlock
            {
                Text = model.Maker + "  ·  Available in WRN Claude",
                FontSize = 12.5,
                Foreground = Brush("#817787"),
                Margin = new Thickness(0, 5, 0, 20)
            };
            body.Children.Add(maker);

            var metrics = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.Children.Add(MetricCard("AA Intelligence", model.Index, 0));
            metrics.Children.Add(MetricCard("AA rank ↓", model.Rank, 1));
            metrics.Children.Add(MetricCard("Est. short message*", model.ShortMessage, 2));
            body.Children.Add(metrics);

            body.Children.Add(SectionTitle("What it is"));
            body.Children.Add(BodyText(model.Summary));

            body.Children.Add(SectionTitle("Good choice for"));
            body.Children.Add(BodyText(model.UseCases));

            body.Children.Add(SectionTitle("Cost & benchmark context"));
            body.Children.Add(BodyText(model.CostContext));

            var caveat = new TextBlock
            {
                Text = "*Short-message estimate assumes 2,000 input + 1,000 output tokens at the listed API rates. Artificial Analysis Intelligence Index: higher is better. AA rank: lower is better within that model page's comparison class; ranks from different classes are not directly comparable.",
                FontSize = 13,
                Foreground = Brush("#8A818E"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 20)
            };
            body.Children.Add(caveat);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var aa = DialogButton("Artificial Analysis ↗", true);
            aa.Click += delegate { OpenExternalUrl(model.Url); };
            actions.Children.Add(aa);

            var close = DialogButton("Close", false);
            close.Margin = new Thickness(10, 0, 0, 0);
            close.Click += delegate { dialog.Close(); };
            actions.Children.Add(close);
            body.Children.Add(actions);

            var modelScroll = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            dialog.Content = CreateDialogShell(dialog, modelScroll);
            dialog.ShowDialog();
        }

        private void ShowSupportTemplate(string title, string subject, string template)
        {
            var dialog = CreateDialog(title, 610, 500);
            var body = new StackPanel { Margin = new Thickness(28) };

            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#302536")
            });

            body.Children.Add(new TextBlock
            {
                Text = "Please send this request to Leon.Davies@wtwco.com",
                FontSize = 13.5,
                Foreground = Brush("#5A1A75"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 18)
            });

            var box = new TextBox
            {
                Text = template,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 255,
                Padding = new Thickness(14),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Brush("#3B3340"),
                FontSize = 13.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            body.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#DDD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = box
            });

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var outlook = DialogButton("Open in Outlook", true);
            outlook.Click += delegate
            {
                OpenOutlookDraft("Leon.Davies@wtwco.com", subject, template);
            };
            actions.Children.Add(outlook);

            var copy = DialogButton("Copy template", false);
            copy.Margin = new Thickness(10, 0, 0, 0);
            copy.Click += delegate
            {
                Clipboard.SetText(template);
                ShowToast("Template copied", "Send it to Leon.Davies@wtwco.com");
            };
            actions.Children.Add(copy);

            var close = DialogButton("Close", false);
            close.Margin = new Thickness(10, 0, 0, 0);
            close.Click += delegate { dialog.Close(); };
            actions.Children.Add(close);
            body.Children.Add(actions);

            dialog.Content = CreateDialogShell(dialog, body);
            dialog.ShowDialog();
        }

        private Window CreateDialog(string title, double width, double height)
        {
            var dialog = new Window
            {
                Title = title + " — WRN AI Gateway",
                Owner = _window,
                Width = width,
                Height = height,
                MinWidth = width,
                MinHeight = height,
                MaxWidth = width,
                MaxHeight = height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Segoe UI"),
                ShowInTaskbar = false
            };

            var iconPath = Path.Combine(_baseDir, "assets", "wrn-ai-gateway.ico");
            if (File.Exists(iconPath))
            {
                try
                {
                    dialog.Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
                }
                catch { }
            }

            return dialog;
        }

        private FrameworkElement CreateDialogShell(Window dialog, FrameworkElement content)
        {
            var outer = new Border
            {
                Background = Brush("#F7F5F8"),
                BorderBrush = Brush("#DCCFE1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 6,
                    Opacity = 0.22,
                    Color = (Color)ColorConverter.ConvertFromString("#2B1533")
                }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleBar = new Border
            {
                Background = Brush("#45125D"),
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Cursor = Cursors.SizeAll
            };
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                try { dialog.DragMove(); } catch { }
            };

            var titleGrid = new Grid { Margin = new Thickness(14, 0, 8, 0) };
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            mark.Child = new TextBlock
            {
                Text = "W",
                Foreground = Brush("#4B1464"),
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleGrid.Children.Add(mark);

            var appTitle = new TextBlock
            {
                Text = "WRN AI Gateway",
                Foreground = Brushes.White,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(appTitle, 1);
            titleGrid.Children.Add(appTitle);

            var close = new Button
            {
                Content = "×",
                Width = 40,
                Height = 36,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 19,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                ToolTip = "Close",
                Template = CreateFlatButtonTemplate(8)
            };
            close.MouseEnter += delegate { close.Background = Brush("#24FFFFFF"); };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; };
            close.Click += delegate { dialog.Close(); };
            Grid.SetColumn(close, 2);
            titleGrid.Children.Add(close);

            titleBar.Child = titleGrid;
            layout.Children.Add(titleBar);

            var contentFrame = new Border
            {
                Background = Brush("#F7F5F8"),
                CornerRadius = new CornerRadius(0, 0, 13, 13),
                Child = content
            };
            Grid.SetRow(contentFrame, 1);
            layout.Children.Add(contentFrame);

            outer.Child = layout;
            return outer;
        }

        private static Border MetricCard(string label, string value, int column)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = Brush("#8E8492")
            });
            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#382D3D"),
                Margin = new Thickness(0, 4, 0, 0)
            });

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#E5DFE8"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(13),
                Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 2 ? 0 : 6, 0),
                Child = panel
            };
            Grid.SetColumn(card, column);
            return card;
        }

        private static TextBlock SectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#3B3340"),
                Margin = new Thickness(0, 0, 0, 5)
            };
        }

        private static TextBlock BodyText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13.5,
                Foreground = Brush("#6F6574"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 17)
            };
        }

        private static Button DialogButton(string label, bool primary)
        {
            var normal = primary ? Brush("#56186F") : Brush("#ECE6EF");
            var hover = primary ? Brush("#6A2286") : Brush("#E3D8E8");

            var button = new Button
            {
                Content = label,
                Height = 40,
                MinWidth = 96,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = normal,
                Foreground = primary ? Brushes.White : Brush("#4B1464"),
                Template = CreateFlatButtonTemplate(9)
            };
            button.MouseEnter += delegate { button.Background = hover; };
            button.MouseLeave += delegate { button.Background = normal; };
            return button;
        }

        private static ControlTemplate CreateFlatButtonTemplate(double radius)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.AppendChild(presenter);

            return new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };
        }

        private static SolidColorBrush Brush(string value)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(value);
        }

        private void LoadCatalogue()
        {
            try
            {
                _catalogueLoad = new CatalogueStore(_baseDir).LoadBestAvailable();
                RebuildModelInfo();
            }
            catch
            {
                _catalogueLoad = null;
                _models.Clear();
            }
        }

        private void RebuildModelInfo()
        {
            _models.Clear();
            if (_catalogueLoad == null || _catalogueLoad.Catalogue == null || _catalogueLoad.Catalogue.models == null)
                return;

            foreach (var item in _catalogueLoad.Catalogue.models.Where(delegate(CatalogueModel m) { return m.visible; }))
            {
                var costContext = string.Format(
                    CultureInfo.InvariantCulture,
                    "Artificial Analysis snapshot {0}: input USD {1:0.00}/M, output USD {2:0.00}/M; benchmark task cost USD {3:0.00}. AA effort: {4}. WRN default effort: {5}. {6}",
                    _catalogueLoad.Catalogue.benchmarkSnapshot,
                    item.inputUsdPerMillion,
                    item.outputUsdPerMillion,
                    item.aaTaskCostUsd,
                    item.aaEffort,
                    item.defaultEffort,
                    item.limitations);

                _models[item.key] = new ModelInfo(
                    item.label,
                    item.maker,
                    "#" + item.aaRank + " / " + item.aaClassSize + " ↓",
                    "Index " + item.aaIndex,
                    FormatUsd(item.shortMessageUsd),
                    FormatUsd(item.aaTaskCostUsd),
                    item.description,
                    item.goodFor,
                    costContext,
                    item.aaUrl);
            }
        }

        private void RenderCatalogue()
        {
            var grid = Find<UniformGrid>("ModelsGrid");
            grid.Children.Clear();

            if (_catalogueLoad == null || _catalogueLoad.Catalogue == null)
            {
                Find<TextBlock>("ModelsFootnote").Text =
                    "The signed WRN model catalogue could not be loaded. The existing Claude installation is unaffected.";
                return;
            }

            var visible = _catalogueLoad.Catalogue.models
                .Where(delegate(CatalogueModel m) { return m.visible; })
                .ToArray();

            for (var i = 0; i < visible.Length; i++)
                grid.Children.Add(CreateModelCatalogueButton(visible[i], i));

            Find<TextBlock>("ModelsFootnote").Text =
                "*Artificial Analysis snapshot: " + _catalogueLoad.Catalogue.benchmarkSnapshot +
                ". Intelligence Index: higher is better. AA rank: lower is better within each model page's comparison class; " +
                "ranks across different classes are not directly comparable. Short-message estimate uses 2k input + 1k output tokens. " +
                "Signed catalogue release " + _catalogueLoad.Catalogue.release + ".";

            var defaultEntry = visible.FirstOrDefault(delegate(CatalogueModel m)
            {
                return string.Equals(m.key, _catalogueLoad.Catalogue.defaultModelKey, StringComparison.OrdinalIgnoreCase);
            });
            if (defaultEntry != null)
            {
                Find<Border>("SpotlightBadge").Background = Brush(defaultEntry.accent);
                Find<TextBlock>("SpotlightInitial").Text = defaultEntry.initial;
                Find<TextBlock>("SpotlightName").Text = defaultEntry.label;
                Find<TextBlock>("SpotlightTagline").Text = defaultEntry.tagline;
            }

            var latest = _catalogueLoad.Catalogue.changelog == null
                ? null
                : _catalogueLoad.Catalogue.changelog.FirstOrDefault();
            if (latest != null)
            {
                DateTime parsed;
                if (DateTime.TryParse(latest.date, out parsed))
                {
                    Find<TextBlock>("CatalogueUpdateDate").Text = parsed.ToString("dd MMM", CultureInfo.InvariantCulture);
                    Find<TextBlock>("CatalogueUpdateYear").Text = parsed.ToString("yyyy", CultureInfo.InvariantCulture);
                }
                Find<TextBlock>("CatalogueUpdateTitle").Text = latest.title;
                Find<TextBlock>("CatalogueUpdateBody").Text = latest.body;
            }
        }

        private Button CreateModelCatalogueButton(CatalogueModel entry, int index)
        {
            ModelInfo model;
            if (!_models.TryGetValue(entry.key, out model))
                throw new InvalidOperationException("Catalogue model metadata missing: " + entry.key);

            var button = new Button
            {
                Style = (Style)_window.FindResource("ActionCardButtonStyle"),
                Margin = index % 2 == 0 ? new Thickness(0, 0, 8, 12) : new Thickness(8, 0, 0, 12),
                Padding = new Thickness(19),
                Tag = entry.key
            };
            AutomationProperties.SetName(button, "About " + model.Name);
            var key = entry.key;
            button.Click += delegate { ShowModelDetails(key); };

            var stack = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());

            var badge = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(11),
                Background = Brush(entry.accent)
            };
            badge.Child = new TextBlock
            {
                Text = entry.initial,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(badge);

            var title = new StackPanel
            {
                Margin = new Thickness(13, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            title.Children.Add(new TextBlock
            {
                Text = model.Name,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#302536")
            });
            title.Children.Add(new TextBlock
            {
                Text = entry.tagline,
                FontSize = 12,
                Foreground = Brush("#817787"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            stack.Children.Add(header);

            var metrics = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());

            var aa = new StackPanel();
            aa.Children.Add(new TextBlock
            {
                Text = "AA Intelligence",
                Foreground = Brush("#9A909F"),
                FontSize = 12
            });
            aa.Children.Add(new TextBlock
            {
                Text = model.Index + "  ·  " + model.Rank,
                Foreground = Brush("#3B3340"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            metrics.Children.Add(aa);

            var cost = new StackPanel();
            cost.Children.Add(new TextBlock
            {
                Text = "Est. short message*",
                Foreground = Brush("#9A909F"),
                FontSize = 12
            });
            cost.Children.Add(new TextBlock
            {
                Text = model.ShortMessage,
                Foreground = Brush("#3B3340"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(cost, 1);
            metrics.Children.Add(cost);
            stack.Children.Add(metrics);

            stack.Children.Add(new TextBlock
            {
                Text = "Details →",
                Foreground = Brush("#5A1A75"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            });

            button.Content = stack;
            return button;
        }

        private void ConfigureCatalogueRefresh()
        {
            _catalogueTimer.Interval = TimeSpan.FromMinutes(30);
            _catalogueTimer.Tick += delegate { RefreshCatalogueInBackground(); };
            _catalogueTimer.Start();
            RefreshCatalogueInBackground();
        }

        private void RefreshCatalogueInBackground()
        {
            if (_catalogueRefreshInFlight) return;
            _catalogueRefreshInFlight = true;

            Task.Run(delegate { return CatalogueRemote.Refresh(_baseDir); })
                .ContinueWith(delegate(Task<CatalogueRefreshResult> task)
                {
                    _window.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _catalogueRefreshInFlight = false;
                        if (task.Status != TaskStatus.RanToCompletion
                            || task.Result == null
                            || !task.Result.Success
                            || !task.Result.Changed)
                            return;

                        _catalogueLoad = task.Result.Current;
                        RebuildModelInfo();
                        RenderCatalogue();
                        ShowToast(
                            "Models updated",
                            "WRN model catalogue release " + _catalogueLoad.Catalogue.release + " is ready.");
                    }));
                });
        }

        private static string FormatUsd(double value)
        {
            if (value < 0.01)
                return value.ToString("$0.0000", CultureInfo.InvariantCulture);
            if (value < 0.1)
                return value.ToString("$0.000", CultureInfo.InvariantCulture);
            return value.ToString("$0.00", CultureInfo.InvariantCulture);
        }

        private static void OpenOutlookDraft(string to, string subject, string body)
        {
            var mailto =
                "mailto:" + Uri.EscapeDataString(to) +
                "?subject=" + Uri.EscapeDataString(subject) +
                "&body=" + Uri.EscapeDataString(body);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mailto,
                    UseShellExecute = true
                });
                return;
            }
            catch { }

            var outlookUri =
                "ms-outlook://compose?to=" + Uri.EscapeDataString(to) +
                "&subject=" + Uri.EscapeDataString(subject) +
                "&body=" + Uri.EscapeDataString(body);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = outlookUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                MessageBox.Show(
                    "Outlook could not be opened. Copy the template and send it to Leon.Davies@wtwco.com.",
                    "WRN AI Gateway",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
            Find<TextBlock>("PageHeading").Text =
                key == "Home" ? "Home" : key;

            if (key == "Settings")
                RefreshCredentialStatus();
        }

        private sealed class ModelInfo
        {
            public readonly string Name;
            public readonly string Maker;
            public readonly string Rank;
            public readonly string Index;
            public readonly string ShortMessage;
            public readonly string TaskCost;
            public readonly string Summary;
            public readonly string UseCases;
            public readonly string CostContext;
            public readonly string Url;

            public ModelInfo(
                string name,
                string maker,
                string rank,
                string index,
                string shortMessage,
                string taskCost,
                string summary,
                string useCases,
                string costContext,
                string url)
            {
                Name = name;
                Maker = maker;
                Rank = rank;
                Index = index;
                ShortMessage = shortMessage;
                TaskCost = taskCost;
                Summary = summary;
                UseCases = useCases;
                CostContext = costContext;
                Url = url;
            }
        }

        private T Find<T>(string name) where T : FrameworkElement
        {
            var value = _window.FindName(name) as T;
            if (value == null) throw new InvalidOperationException("UI element not found: " + name);
            return value;
        }
    }
}
