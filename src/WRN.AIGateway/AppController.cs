using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private readonly DispatcherTimer _heroTimer = new DispatcherTimer();
        private readonly List<BitmapImage> _heroImages = new List<BitmapImage>();
        private readonly Dictionary<string, ModelInfo> _models = CreateModelInfo();
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
            Find<Button>("ModelsShortcutButton").Click += delegate { ShowPage("Models"); };
            Find<Button>("UpdatesShortcutButton").Click += delegate { ShowPage("Updates"); };

            Find<Button>("ModelDeepSeekButton").Click += delegate { ShowModelDetails("deepseek"); };
            Find<Button>("ModelSonnetButton").Click += delegate { ShowModelDetails("sonnet"); };
            Find<Button>("ModelAstraButton").Click += delegate { ShowModelDetails("astra"); };
            Find<Button>("ModelSolButton").Click += delegate { ShowModelDetails("sol"); };
            Find<Button>("ModelLunaButton").Click += delegate { ShowModelDetails("luna"); };
            Find<Button>("ModelGlmButton").Click += delegate { ShowModelDetails("glm"); };

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
            metrics.Children.Add(MetricCard("AA intelligence", model.Rank + "  ·  " + model.Index, 0));
            metrics.Children.Add(MetricCard("Est. short message*", model.ShortMessage, 1));
            metrics.Children.Add(MetricCard("AA benchmark task", model.TaskCost, 2));
            body.Children.Add(metrics);

            body.Children.Add(SectionTitle("What it is"));
            body.Children.Add(BodyText(model.Summary));

            body.Children.Add(SectionTitle("Good choice for"));
            body.Children.Add(BodyText(model.UseCases));

            body.Children.Add(SectionTitle("Cost & benchmark context"));
            body.Children.Add(BodyText(model.CostContext));

            var caveat = new TextBlock
            {
                Text = "*Short-message estimate assumes 2,000 input + 1,000 output tokens at the API rates shown by Artificial Analysis. Cowork can send substantially more context and may make multiple model/tool calls. AA rank is within the model page's current comparison class, so ranks are not a universal cross-model league table.",
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

            dialog.Content = new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
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
                BorderBrush = Brush("#DDD5E1"),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Foreground = Brush("#3B3340"),
                FontSize = 13.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            body.Children.Add(box);

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

            dialog.Content = body;
            dialog.ShowDialog();
        }

        private Window CreateDialog(string title, double width, double height)
        {
            return new Window
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
                Background = Brush("#F7F5F8"),
                FontFamily = new FontFamily("Segoe UI"),
                ShowInTaskbar = false
            };
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
            var button = new Button
            {
                Content = label,
                Height = 38,
                MinWidth = 96,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = primary ? Brush("#56186F") : Brush("#ECE6EF"),
                Foreground = primary ? Brushes.White : Brush("#4B1464")
            };
            return button;
        }

        private static SolidColorBrush Brush(string value)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(value);
        }

        private static Dictionary<string, ModelInfo> CreateModelInfo()
        {
            return new Dictionary<string, ModelInfo>
            {
                {
                    "deepseek",
                    new ModelInfo(
                        "DeepSeek V4.1 Flash",
                        "DeepSeek",
                        "#7 / 115",
                        "Index 39",
                        "$0.0018",
                        "$0.27",
                        "An open-weight model designed to deliver strong reasoning at very high output speed and low token prices. Artificial Analysis measures it as one of the stronger models in its open-weight comparison class.",
                        "Good for high-volume everyday Cowork work, quick research, drafting, iterative analysis and tasks where speed and cost matter. For the hardest reasoning or highest-value synthesis, GPT-6 Astra may justify its much higher cost.",
                        "Artificial Analysis lists $0.30 / $1.20 per million input/output tokens and about 233 output tokens per second at max reasoning. The model is very fast but can be verbose.",
                        "https://artificialanalysis.ai/models/deepseek-v4-1-flash")
                },
                {
                    "sonnet",
                    new ModelInfo(
                        "Claude Sonnet 5",
                        "Anthropic",
                        "#54 / 211",
                        "Index 38",
                        "$0.014",
                        "$5.09",
                        "Anthropic's general-purpose Claude model in the current WRN list. It combines solid intelligence with good output speed and the familiar Claude style.",
                        "Good for writing, document work, summarisation and tool-heavy workflows where you want a balanced model rather than maximum reasoning. Its max-effort benchmark is unusually verbose, which raises cost on long agentic tasks.",
                        "Artificial Analysis currently lists $2 / $10 per million input/output tokens, around 82 output tokens per second, and a $5.09 Intelligence Index task cost at max effort.",
                        "https://artificialanalysis.ai/models/claude-sonnet-5")
                },
                {
                    "astra",
                    new ModelInfo(
                        "GPT-6 Astra",
                        "OpenAI",
                        "#6 / 211",
                        "Index 53",
                        "$0.070",
                        "$3.26",
                        "A frontier reasoning model and the highest-intelligence OpenAI option currently exposed in WRN Claude. It trades speed and price for stronger performance on difficult tasks.",
                        "Use for the hardest analysis, complex multi-step reasoning, important synthesis and work where getting the best answer matters more than latency or cost. It is usually excessive for routine drafting or simple queries.",
                        "Artificial Analysis lists $10 / $50 per million input/output tokens, an Intelligence Index of 53, and roughly 54 output tokens per second. It is one of the most expensive choices in the current WRN list.",
                        "https://artificialanalysis.ai/models/gpt-6-astra")
                },
                {
                    "sol",
                    new ModelInfo(
                        "GPT-5.6 Sol",
                        "OpenAI",
                        "#19 / 211",
                        "Index 47",
                        "$0.028",
                        "$1.99",
                        "A strong professional reasoning model that remains capable for research, structured analysis and polished knowledge work. It is less expensive than GPT-6 Astra while retaining substantially more capability than lightweight options.",
                        "Good for complex professional work when Astra is unnecessary, including detailed analysis, technical writing and difficult document tasks. Artificial Analysis now flags GPT-5.6 Sol as superseded by GPT-6 Sol, but it remains in the current WRN Claude catalogue.",
                        "Artificial Analysis lists $4 / $20 per million input/output tokens, an Intelligence Index of 47, and roughly 73 output tokens per second at max effort.",
                        "https://artificialanalysis.ai/models/gpt-5-6-sol")
                },
                {
                    "luna",
                    new ModelInfo(
                        "GPT-5.6 Luna",
                        "OpenAI",
                        "#5 / 174",
                        "Index 37",
                        "$0.0016",
                        "$0.18",
                        "A lightweight OpenAI reasoning model designed for cost-sensitive workloads. It is much cheaper and faster than the larger OpenAI options while still scoring well within its price class.",
                        "Good for routine summarisation, drafting, extraction, quick analysis and high-volume tasks. Artificial Analysis now flags GPT-5.6 Luna as superseded by GPT-6 Luna, but it remains in the current WRN Claude catalogue.",
                        "Artificial Analysis lists $0.20 / $1.20 per million input/output tokens, an Intelligence Index of 37, and roughly 126 output tokens per second at max effort.",
                        "https://artificialanalysis.ai/models/gpt-5-6-luna")
                },
                {
                    "glm",
                    new ModelInfo(
                        "GLM 5.3 Flash",
                        "Z AI",
                        "#4 / 115",
                        "Index 42",
                        "$0.0008",
                        "$0.25",
                        "A very low-cost open-weight reasoning model with strong benchmark intelligence for its class. Its main trade-off is slower output and relatively high verbosity.",
                        "Good for inexpensive reasoning, structured analysis and workloads where response time is less important. DeepSeek is usually the better low-cost choice when speed matters.",
                        "Artificial Analysis lists $0.15 / $0.50 per million input/output tokens, an Intelligence Index of 42, and roughly 43 output tokens per second.",
                        "https://artificialanalysis.ai/models/glm-5-3-flash")
                }
            };
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
            Find<TextBlock>("PageHeading").Text = key == "Home" ? "Home" : key;
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
