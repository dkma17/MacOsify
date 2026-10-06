using MacOSify.Win11.Models;
using MacOSify.Win11.Modules;
using MacOSify.Win11.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text;
using Windows.Graphics;

namespace MacOSify.Win11;

public sealed partial class MainWindow : Window
{
    private readonly OperationJournalService _journal;
    private readonly SystemSafetyService _safety;
    private readonly InstallationOrchestrator _orchestrator;
    private readonly SystemActionsService _systemActions = new();
    private readonly Queue<string> _logLines = new();
    private CancellationTokenSource? _operationCancellation;
    private bool _isBusy;
    private bool _startupRecoveryChecked;

    public MainWindow()
    {
        InitializeComponent();

        _journal = new OperationJournalService();
        var processRunner = new ProcessRunner();
        var registry = new RegistryService(_journal);
        var winget = new WingetService(processRunner, _journal);
        var recipes = new VerifiedRecipeService(processRunner, _journal);
        _safety = new SystemSafetyService(processRunner, winget, recipes);
        var rollback = new RollbackService(_journal, registry, winget, recipes);
        ICustomizationModule[] modules =
        [
            new DockModule(),
            new TopBarModule(),
            new ThemeModule(),
            new IconModule(),
            new CursorModule(),
            new WindowEffectsModule()
        ];
        _orchestrator = new InstallationOrchestrator(
            modules,
            _safety,
            _journal,
            rollback,
            registry,
            winget,
            recipes);

        ConfigureWindow();
        ConfigureModuleAvailability();
        AdminStatusText.Text = _safety.IsAdministrator ? "Administrator access" : "Administrator required";
        AppWindow.Closing += AppWindow_Closing;
        Activated += MainWindow_Activated;
    }

    private void ConfigureWindow()
    {
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragRegion);
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
            AppWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(32, 0, 0, 0);
        }

        AppWindow.Title = "macOSify Win11";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = true;
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            }
        }

        const int preferredWidth = 1120;
        const int preferredHeight = 760;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var width = Math.Max(1, Math.Min(preferredWidth, display.WorkArea.Width));
        var height = Math.Max(1, Math.Min(preferredHeight, display.WorkArea.Height));
        var x = display.WorkArea.X + Math.Max(0, (display.WorkArea.Width - width) / 2);
        var y = display.WorkArea.Y + Math.Max(0, (display.WorkArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void ConfigureModuleAvailability()
    {
        LoadPreviewImages();

        var iconsRecipe = Path.Combine(AppPaths.Recipes, "icons", "recipe.json");
        var cursorsRecipe = Path.Combine(AppPaths.Recipes, "cursors", "recipe.json");

        IconsToggle.IsEnabled = File.Exists(iconsRecipe);
        IconsToggle.IsOn = IconsToggle.IsEnabled;
        IconsAvailabilityText.Text = IconsToggle.IsEnabled ? "Verified pack ready" : "Verified recipe required";
        ToolTipService.SetToolTip(
            IconsToggle,
            IconsToggle.IsEnabled
                ? "A checksum-pinned macOS icon pack is ready to install."
                : "Add a verified recipe under Assets\\Recipes\\icons to enable this module.");

        CursorsToggle.IsEnabled = File.Exists(cursorsRecipe);
        CursorsToggle.IsOn = CursorsToggle.IsEnabled;
        CursorsAvailabilityText.Text = CursorsToggle.IsEnabled ? "Verified pack ready" : "Verified recipe required";
        ToolTipService.SetToolTip(
            CursorsToggle,
            CursorsToggle.IsEnabled
                ? "A checksum-pinned macOS cursor scheme is ready to install."
                : "Add a verified recipe under Assets\\Recipes\\cursors to enable this module.");
    }

    private void LoadPreviewImages()
    {
        TrySetImage(DockPreviewImage, "dock.jpg");
        TrySetImage(TopBarPreviewImage, "topbar.jpg");
        TrySetImage(ThemePreviewImage, "theme.jpg");
        TrySetImage(IconsPreviewImage, "icons.jpg");
        TrySetImage(CursorsPreviewImage, "cursors.jpg");
        TrySetImage(EffectsPreviewImage, "effects.jpg");
    }

    private static void TrySetImage(Image imageControl, string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Previews", fileName);
            if (File.Exists(path))
            {
                imageControl.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
            }
        }
        catch
        {
            // Ignore preview image load issues if any
        }
    }

    private void ShowStep(int step)
    {
        WelcomePage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        OptionsPage.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ExecutionPage.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        CompletePage.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        StepLabel.Text = step switch
        {
            1 => "Welcome · 1 of 4",
            2 => "Options · 2 of 4",
            3 => "Installation · 3 of 4",
            4 => "Complete · 4 of 4",
            _ => string.Empty
        };
    }

    private void BeginButton_Click(object sender, RoutedEventArgs e) => ShowStep(2);

    private void BackToWelcomeButton_Click(object sender, RoutedEventArgs e) => ShowStep(1);

    private void ReturnHomeButton_Click(object sender, RoutedEventArgs e) => ShowStep(1);

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var selectedModules = GetSelectedModules();
        if (selectedModules.Count == 0)
        {
            await ShowDialogAsync("Choose at least one module", "Turn on one or more customization modules before installing.");
            return;
        }

        var moduleNames = string.Join(", ", selectedModules.Select(GetModuleDisplayName));
        var disclosures = new StringBuilder();
        disclosures.Append($"Selected: {moduleNames}\n\nmacOSify will require a newly verified restore point, record every original value, and safely apply reversible customizations.");
        if (selectedModules.Contains(ModuleId.Dock))
        {
            disclosures.Append("\n\n• Taskbar & Dock: Auto-hides Windows taskbar and installs Nexus Dock with pre-made macOS Sonoma theme profile.");
        }
        if (selectedModules.Contains(ModuleId.TopBar))
        {
            disclosures.Append("\n\n• Top Menu Bar: Installs Rainmeter and activates the bundled macOS Top Menu Bar suite with live clock and system controls.");
        }
        if (selectedModules.Contains(ModuleId.Theme))
        {
            disclosures.Append("\n\n• System Theme: Applies macOS Sonoma wallpaper, Apple Aqua accent palette, light surfaces, and transparency.");
        }
        if (selectedModules.Contains(ModuleId.Icons))
        {
            disclosures.Append("\n\n• Icons: Applies checksum-verified macOS system folder, drive, and Recycle Bin icons.");
        }
        if (selectedModules.Contains(ModuleId.Cursors))
        {
            disclosures.Append("\n\n• Cursors: Applies checksum-verified macOS cursor scheme with beachball wait indicator.");
        }
        if (selectedModules.Contains(ModuleId.WindowEffects))
        {
            disclosures.Append("\n\n• Window Effects: Enables system transparency and configures Mica For Everyone with rounded corners and Acrylic blur.");
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = OptionsPage.XamlRoot,
            Title = "Ready to modify Windows?",
            Content = disclosures.ToString(),
            PrimaryButtonText = "Create restore point & install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        ResetExecutionUi("Starting safety checks...");
        ShowStep(3);
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<InstallUpdate>(HandleProgress);

        try
        {
            await _orchestrator.InstallAsync(selectedModules, progress, _operationCancellation.Token);
            ShowCompletion(
                "Customization complete",
                "Your selected modules were installed successfully. Restart Explorer to refresh the desktop, or reboot Windows to finish pending installer changes.",
                "The operation journal is saved. Revert restores recorded settings and removes directly owned packages; package-manager dependencies may remain when shared.",
                enableSystemActions: true);
        }
        catch (OperationCanceledException)
        {
            ShowCompletion(
                "Installation cancelled safely",
                "The installation was cancelled and completed operations were rolled back.",
                "Review the activity log if a component reported that manual attention is required.",
                enableSystemActions: false);
        }
        catch (Exception exception)
        {
            AppendLog($"[error] {GetFriendlyMessage(exception)}");
            ShowCompletion(
                "Installation stopped",
                "macOSify stopped because a safety check or module failed. Automatic rollback was attempted before this screen appeared.",
                GetFriendlyMessage(exception),
                enableSystemActions: false);
        }
        finally
        {
            _isBusy = false;
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private async void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var existing = await _journal.LoadAsync();
        if (existing is null || existing.Status == JournalStatus.RolledBack)
        {
            await ShowDialogAsync("Nothing to revert", "No applied macOSify operation journal was found on this PC.");
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = WelcomePage.XamlRoot,
            Title = "Revert macOSify changes?",
            Content = "Recorded registry values will be restored and only packages installed by macOSify will be removed. Packages that existed beforehand will be kept.",
            PrimaryButtonText = "Revert changes",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunRevertWorkflowAsync();
    }

    private async Task RunRevertWorkflowAsync()
    {
        ResetExecutionUi("Preparing recorded rollback...");
        ShowStep(3);
        _isBusy = true;
        _operationCancellation = new CancellationTokenSource();
        var progress = new Progress<InstallUpdate>(HandleProgress);

        try
        {
            await _orchestrator.RevertAsync(progress, _operationCancellation.Token);
            ShowCompletion(
                "Windows settings restored",
                "macOSify restored the recorded settings and removed the packages it owned.",
                "Restart Explorer or reboot if the desktop has not refreshed yet.",
                enableSystemActions: true);
        }
        catch (Exception exception)
        {
            AppendLog($"[error] {GetFriendlyMessage(exception)}");
            ShowCompletion(
                "Rollback needs attention",
                "Some recorded operations could not be reverted automatically. The journal has been preserved for recovery.",
                GetFriendlyMessage(exception),
                enableSystemActions: false);
        }
        finally
        {
            _isBusy = false;
            _operationCancellation.Dispose();
            _operationCancellation = null;
        }
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_startupRecoveryChecked || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _startupRecoveryChecked = true;
        try
        {
            var existing = await _journal.LoadAsync();
            if (existing?.Status is not (
                JournalStatus.Planned or
                JournalStatus.RestorePointCreated or
                JournalStatus.Applying or
                JournalStatus.RollingBack or
                JournalStatus.RecoveryRequired))
            {
                return;
            }

            var recoveryDialog = new ContentDialog
            {
                XamlRoot = WelcomePage.XamlRoot,
                Title = "An unfinished operation was found",
                Content = "macOSify found a durable journal from an interrupted installation or rollback. Recover recorded changes before starting a new installation.",
                PrimaryButtonText = "Recover now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await recoveryDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await RunRevertWorkflowAsync();
            }
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("Recovery check failed", exception.Message);
        }
    }

    private void CancelInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy || _operationCancellation is null)
        {
            return;
        }

        CancelInstallButton.IsEnabled = false;
        CancelInstallButton.Content = "Cancelling safely...";
        AppendLog("[warning] Cancellation requested. Waiting for the current operation, then rolling back.");
        _operationCancellation.Cancel();
    }

    private async void RestartExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        var confirmation = new ContentDialog
        {
            XamlRoot = CompletePage.XamlRoot,
            Title = "Restart Windows Explorer?",
            Content = "The taskbar and desktop will disappear briefly, then return.",
            PrimaryButtonText = "Restart Explorer",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            _isBusy = false;
            return;
        }

        try
        {
            RestartExplorerButton.IsEnabled = false;
            await _systemActions.RestartExplorerAsync(CancellationToken.None);
            CompletionNoteText.Text = "Windows Explorer restarted successfully.";
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("Explorer did not restart", exception.Message);
        }
        finally
        {
            RestartExplorerButton.IsEnabled = true;
            _isBusy = false;
        }
    }

    private async void RebootButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        var confirmation = new ContentDialog
        {
            XamlRoot = CompletePage.XamlRoot,
            Title = "Restart Windows in 30 seconds?",
            Content = "Save open work before continuing. Windows will show a 30-second restart countdown.",
            PrimaryButtonText = "Schedule restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            _isBusy = false;
            return;
        }

        try
        {
            _systemActions.ScheduleReboot();
            CompletionNoteText.Text = "Restart scheduled. Save your work; Windows will reboot in 30 seconds.";
            RebootButton.IsEnabled = false;
        }
        catch (Exception exception)
        {
            await ShowDialogAsync("Restart was not scheduled", exception.Message);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private IReadOnlyCollection<ModuleId> GetSelectedModules()
    {
        var modules = new List<ModuleId>();
        if (DockToggle.IsOn) modules.Add(ModuleId.Dock);
        if (TopBarToggle.IsOn) modules.Add(ModuleId.TopBar);
        if (ThemeToggle.IsOn) modules.Add(ModuleId.Theme);
        if (IconsToggle.IsOn) modules.Add(ModuleId.Icons);
        if (CursorsToggle.IsOn) modules.Add(ModuleId.Cursors);
        if (EffectsToggle.IsOn) modules.Add(ModuleId.WindowEffects);
        return modules;
    }

    private void HandleProgress(InstallUpdate update)
    {
        InstallProgress.Value = update.Percent;
        ProgressText.Text = $"{Math.Round(update.Percent):0}%";
        ExecutionStatusText.Text = update.Status;
        CancelInstallButton.IsEnabled = _isBusy && update.CanCancel;
        CancelInstallButton.Content = update.CanCancel ? "Cancel and roll back" : "Finishing current step...";

        var prefix = update.Level switch
        {
            InstallLogLevel.Success => "done",
            InstallLogLevel.Warning => "warning",
            InstallLogLevel.Error => "error",
            _ => "info"
        };
        AppendLog($"[{prefix}] {update.Message}");
    }

    private void ResetExecutionUi(string status)
    {
        _logLines.Clear();
        LogText.Text = string.Empty;
        InstallProgress.Value = 0;
        ProgressText.Text = "0%";
        ExecutionStatusText.Text = status;
        CancelInstallButton.IsEnabled = true;
        CancelInstallButton.Content = "Cancel and roll back";
        AppendLog("[ready] macOSify started the protected installation workflow.");
    }

    private void AppendLog(string message)
    {
        _logLines.Enqueue(message);
        while (_logLines.Count > 400)
        {
            _logLines.Dequeue();
        }

        var builder = new StringBuilder();
        foreach (var line in _logLines)
        {
            builder.AppendLine(line);
        }

        LogText.Text = builder.ToString().TrimEnd();
        _ = LogScrollViewer.ChangeView(null, double.MaxValue, null, disableAnimation: true);
    }

    private void ShowCompletion(
        string title,
        string description,
        string note,
        bool enableSystemActions)
    {
        CompleteTitleText.Text = title;
        CompleteDescriptionText.Text = description;
        CompletionNoteText.Text = note;
        RestartExplorerButton.IsEnabled = enableSystemActions;
        RebootButton.IsEnabled = enableSystemActions;
        ShowStep(4);
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = WelcomePage.XamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private static string GetModuleDisplayName(ModuleId module) => module switch
    {
        ModuleId.Dock => "Taskbar & Dock",
        ModuleId.TopBar => "Top Menu Bar",
        ModuleId.Theme => "System Theme",
        ModuleId.Icons => "Icons",
        ModuleId.Cursors => "Cursors",
        ModuleId.WindowEffects => "Window Effects",
        _ => module.ToString()
    };

    private static string GetFriendlyMessage(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return string.Join(" ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message).Distinct());
        }

        return exception.Message;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isBusy)
        {
            return;
        }

        args.Cancel = true;
        _operationCancellation?.Cancel();
        AppendLog("[warning] Close requested. macOSify must finish rollback before the window can close.");
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            _operationCancellation?.Cancel();
            AppendLog("[warning] Close requested. Waiting for safe rollback.");
            return;
        }

        Close();
    }
}
