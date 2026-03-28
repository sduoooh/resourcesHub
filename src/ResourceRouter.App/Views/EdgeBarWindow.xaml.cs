using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ResourceRouter.App.Behaviors;
using ResourceRouter.App.Interop;
using ResourceRouter.App.Interop.Ole;
using ResourceRouter.App.Services;
using ResourceRouter.App.State;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;

namespace ResourceRouter.App.Views;

public partial class EdgeBarWindow : Window
{
    private const double SensorStripWidth = 10;
    private const double SensorHostWidthCollapsed = 10;
    private const double SensorHostWidthExpanded = 24;
    private const double PopupOffsetCollapsed = -10;
    private const double PopupOffsetExpanded = -7;
    private const int CardListLimit = 200;
    private const string ConfigTag = "config";
    private const double DragStartThreshold = 6;
    private const double CardListPopupMinHeight = 150;
    private const double CardListPopupMaxHeight = 640;

    private readonly AppRuntime _runtime;
    private readonly ProximityFadeBehavior _proximityFadeBehavior = new();
    private readonly DampedDragBehavior _dampedDragBehavior = new();
    private readonly EdgeBarStateMachine _stateMachine = new();
    private readonly DropIngressCoordinator _dropIngressCoordinator = new();
    private readonly System.Threading.SemaphoreSlim _resourceConfigChangeLock = new(1, 1);
    private readonly DispatcherTimer _dragLeaveCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly DispatcherTimer _dropIngressRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private OleDropTargetRegistration? _oleDropTargetRegistration;

    private bool _isMouseDown;
    private bool _isDragMoveStarted;
    private bool _isDragOutInProgress;
    private IntPtr _windowHandle;
    private DateTimeOffset _dropIngressSuppressedUntil = DateTimeOffset.MinValue;
    private int _dropIngressSuppressVersion;
    private Point _mouseDownScreenPoint;
    private IReadOnlyList<RawDropData>? _pendingDrops;
    private DropIngressChannel _pendingDropChannel = DropIngressChannel.Wpf;
    private readonly HashSet<string> _pinnedTags = new(StringComparer.OrdinalIgnoreCase);
    private string _currentCardQuery = string.Empty;

    private enum SensorStripMode
    {
        Collapsed,
        DropPanel,
        MainPanel
    }

    public EdgeBarWindow(AppRuntime runtime)
    {
        _runtime = runtime;

        InitializeComponent();
        CardListControl.SetProcessedRouteResolver(_runtime.GetProcessedRouteOptions);
        ConfigureWindowPosition();
        BindBehaviors();
        BindStateMachine();
        BindPipelineEvents();
        BindUiEvents();

        _ = RefreshCardsAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _oleDropTargetRegistration?.Dispose();
        _resourceConfigChangeLock.Dispose();
        _proximityFadeBehavior.Dispose();
        _dampedDragBehavior.Dispose();
        _dropIngressRecoveryTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        Win32Helpers.SetToolWindowStyle(_windowHandle);
        Win32Helpers.EnsureTopMostNoActivate(_windowHandle);

        _oleDropTargetRegistration = OleDropTargetRegistration.TryRegister(
            _windowHandle,
            onDragEnter: () => Dispatcher.Invoke(() =>
            {
                if (_isDragOutInProgress || IsDropIngressSuppressed())
                {
                    return;
                }

                EnsureWindowVisible();
                _stateMachine.DragEnter();
            }),
            onDragLeave: () => Dispatcher.Invoke(() =>
            {
                if (_isDragOutInProgress || IsDropIngressSuppressed())
                {
                    return;
                }

                if (HasPendingDrop())
                {
                    return;
                }

                _stateMachine.DragLeave();
            }),
            onDrop: dataObject =>
            {
                if (_isDragOutInProgress || IsDropIngressSuppressed())
                {
                    return;
                }

                _ = Dispatcher.InvokeAsync(() => StagePendingDrop(dataObject, DropIngressChannel.Com));
            },
            logger: _runtime.Logger);
    }

    public bool IsPanelExpanded => DropPopup.IsOpen || CardListPopup.IsOpen;

    public void ToggleMainPanelFromTray()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ToggleMainPanelFromTray);
            return;
        }

        TogglePanelFromSensorClick();
    }

    private void ConfigureWindowPosition()
    {
        Width = SensorHostWidthCollapsed;
        Left = SystemParameters.WorkArea.Right - Width;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
        Opacity = 0;
        SetSensorStripPresentation(SensorStripMode.Collapsed);
    }

    private void BindBehaviors()
    {
        _proximityFadeBehavior.Attach(this);
        _proximityFadeBehavior.OpacityChanged += (_, opacity) => _stateMachine.MouseProximityChanged(opacity);

        _dampedDragBehavior.Attach(this);

        _dropIngressRecoveryTimer.Tick += (_, _) => EnsureDropIngressListenerReady();
        _dropIngressRecoveryTimer.Start();

        _dragLeaveCollapseTimer.Tick += (_, _) =>
        {
            _dragLeaveCollapseTimer.Stop();
            if (HasPendingDrop())
            {
                return;
            }

            _stateMachine.DragLeave();
        };
    }

    private void BindStateMachine()
    {
        _stateMachine.SetOpacityRequested += opacity =>
        {
            var animation = new DoubleAnimation
            {
                To = opacity,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        };
        _stateMachine.ExpandDropPanelRequested += () =>
        {
            SetSensorStripPresentation(SensorStripMode.DropPanel);
            EnsureWindowVisible();
            DropPopup.IsOpen = true;
            CardListPopup.IsOpen = false;
            PlayPopupOpenAnimation(DropPopupBorder, DropPopupScale);
        };
        _stateMachine.ShowCardListRequested += () =>
        {
            SetSensorStripPresentation(SensorStripMode.MainPanel);
            ActivateForInteractiveInput();
            EnsureWindowVisible();
            CardListPopup.IsOpen = true;
            DropPopup.IsOpen = false;
            PlayPopupOpenAnimation(CardListPopupBorder, CardListPopupScale);
            CardListControl.FocusSearchBox();
            _ = RefreshCardsAsync();
        };
        _stateMachine.CollapseAllRequested += () =>
        {
            SetSensorStripPresentation(SensorStripMode.Collapsed);
            DropPopup.IsOpen = false;
            CardListPopup.IsOpen = false;
            ClearPendingDrops();
            CardListControl.CollapseCardInteractions();
        };
    }

    private void BindPipelineEvents()
    {
        _runtime.PipelineEngine.OnResourceEnterWaiting += (_, _) =>
            _ = Dispatcher.InvokeAsync(async () => await RefreshCardsAsync());

        _runtime.PipelineEngine.OnResourceReady += (_, _) =>
            _ = Dispatcher.InvokeAsync(async () => await RefreshCardsAsync());

        _runtime.PipelineEngine.OnResourceError += (_, args) =>
        {
            _runtime.Logger.LogError("Pipeline 处理失败", args.Exception);
            _ = Dispatcher.InvokeAsync(async () => await RefreshCardsAsync());
        };

        _runtime.PipelineEngine.OnOpenConfigDialog += (_, resource) =>
        {
            Dispatcher.Invoke(() =>
            {
                var dialog = new ConfigDialog(resource) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    _ = _runtime.PipelineEngine.ExecutePipelineAsync(resource);
                    return;
                }

                _runtime.PipelineEngine.ResumePending(resource.Id);
            });
        };
    }

    private void BindUiEvents()
    {
        LocationChanged += (_, _) => RefreshPopupPlacement();

        DropPanelControl.PrivateDrop += async (_, args) =>
        {
            ClearPendingDrops();
            await HandleDropAsync(args.DataObject, PermissionPreset.PrivatePresetId, DropIngressChannel.Wpf);
        };
        DropPanelControl.PublicDrop += async (_, args) =>
        {
            ClearPendingDrops();
            await HandleDropAsync(args.DataObject, PermissionPreset.PublicPresetId, DropIngressChannel.Wpf);
        };
        DropPanelControl.PrivateClickRequested += async (_, _) => await HandlePendingDropAsync(PermissionPreset.PrivatePresetId);
        DropPanelControl.PublicClickRequested += async (_, _) => await HandlePendingDropAsync(PermissionPreset.PublicPresetId);
        DropPanelControl.CollapseRequested += (_, _) => _stateMachine.Collapse();

        CardListControl.SearchRequested += async (_, query) => await SearchCardsAsync(query);
        CardListControl.TagToggleRequested += async (_, args) => await OnTagToggleRequestedAsync(args);
        CardListControl.RawDragRequested += (_, args) => StartDragOut(args.Resource, DragVariant.Raw);
        CardListControl.ProcessedDragRequested += (_, args) => StartDragOut(args.Resource, DragVariant.Processed);
        CardListControl.ResourceConfigChanged += OnCardResourceConfigChanged;
        CardListControl.CollectionDeleteRequested += async (_, args) => await DeleteCollectionRecordAsync(args.Resource);

        PreviewMouseDown += (_, _) =>
        {
            if (CardListPopup.IsOpen && !this.IsActive)
            {
                ActivateForInteractiveInput();
            }
        };

        ManualInputControl.Submitted += async (_, args) =>
        {
            var defaultPreset = PermissionPresetProvider.Resolve(_runtime.Config.DefaultPermissionPresetId);
            var options = BuildOptionsFromPreset(defaultPreset, ResourceSource.Manual);
            options = new ResourceIngestOptions
            {
                PermissionPresetId = options.PermissionPresetId,
                Privacy = options.Privacy,
                SyncPolicy = options.SyncPolicy,
                ProcessingModel = options.ProcessingModel,
                Source = options.Source,
                UserTitle = args.UserTitle
            };

            await _runtime.PipelineEngine.IngestResourceAsync(args.RawDropData, options);
            await RefreshCardsAsync();
        };
    }

    private async Task HandleDropAsync(IDataObject dataObject, string presetId, DropIngressChannel channel = DropIngressChannel.Wpf)
    {
        var drops = DragDropBridge.Extract(dataObject);
        await HandleDropsAsync(drops, presetId, channel);
    }

    private async Task HandleDropsAsync(IReadOnlyList<RawDropData> drops, string presetId, DropIngressChannel channel = DropIngressChannel.Wpf)
    {
        if (drops.Count == 0)
        {
            return;
        }

        var shouldProcess = await _dropIngressCoordinator
            .ShouldProcessAsync(drops, channel, comEnabled: _oleDropTargetRegistration is not null)
            .ConfigureAwait(true);
        if (!shouldProcess)
        {
            _stateMachine.DropCompleted();
            await RefreshCardsAsync();
            return;
        }

        var preset = PermissionPresetProvider.Resolve(presetId);
        foreach (var drop in drops)
        {
            var options = BuildOptionsFromPreset(preset, ResourceSource.Unknown);
            await _runtime.PipelineEngine.IngestResourceAsync(drop, options);
        }

        await RefreshCardsAsync();
        _stateMachine.DropCompleted();
    }

    private async Task HandlePendingDropAsync(string presetId)
    {
        if (!HasPendingDrop())
        {
            return;
        }

        var stagedDrops = _pendingDrops!;
        var channel = _pendingDropChannel;
        ClearPendingDrops();
        await HandleDropsAsync(stagedDrops, presetId, channel);
    }

    private async Task SearchCardsAsync(string query)
    {
        _currentCardQuery = query ?? string.Empty;
        await RefreshCardsAsync();
    }

    private async Task RefreshCardsAsync()
    {
        var (searchText, queryTagFilters) = ParseSearchQuery(_currentCardQuery);
        var resources = string.IsNullOrWhiteSpace(searchText)
            ? await _runtime.ResourceManager.ListRecentAsync(CardListLimit)
            : await _runtime.SearchIndex.QueryAsync(searchText, limit: CardListLimit, offset: 0);

        var tagUniverseResources = await _runtime.ResourceManager.ListRecentAsync(CardListLimit);
        var existingTags = CollectExistingTags(tagUniverseResources);
        _pinnedTags.RemoveWhere(tag => !existingTags.Contains(tag));

        var effectiveTagFilters = BuildEffectiveTagFilters(queryTagFilters);
        var visibleResources = ApplyTagVisibilityFilter(resources, effectiveTagFilters);

        var displayTags = BuildDisplayTags(existingTags, _pinnedTags, queryTagFilters);

        CardListControl.SetResources(visibleResources);
        CardListControl.SetTagChips(
            displayTags,
            _pinnedTags,
            queryTagFilters);

        UpdateCardListPopupHeight();
        if (CardListPopup.IsOpen)
        {
            CardListControl.FocusSearchBox();
        }
    }

    private static (string SearchText, IReadOnlyList<string> TagFilters) ParseSearchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (string.Empty, Array.Empty<string>());
        }

        var searchTerms = new List<string>();
        var tagFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (TryExtractTagFilter(token, out var tag))
            {
                tagFilters.Add(tag);
                continue;
            }

            searchTerms.Add(token);
        }

        return (string.Join(' ', searchTerms), tagFilters.ToArray());
    }

    private static bool TryExtractTagFilter(string token, out string tag)
    {
        tag = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            var value = token[4..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                tag = value;
                return true;
            }

            return false;
        }

        if (token.StartsWith('#') && token.Length > 1)
        {
            tag = token[1..].Trim();
            return !string.IsNullOrWhiteSpace(tag);
        }

        return false;
    }

    private static IReadOnlyList<Resource> ApplyTagVisibilityFilter(IReadOnlyList<Resource> resources, IReadOnlyList<string> tagFilters)
    {
        var filters = new HashSet<string>(tagFilters, StringComparer.OrdinalIgnoreCase);
        var showConfigResources = filters.Any(filter => string.Equals(filter, ConfigTag, StringComparison.OrdinalIgnoreCase));

        var query = resources.Where(resource => showConfigResources || !HasTagExact(resource, ConfigTag));
        if (filters.Count > 0)
        {
            query = query.Where(resource => filters.All(filter => HasTagContains(resource, filter)));
        }

        return query.ToArray();
    }

    private static bool HasTagExact(Resource resource, string tag)
    {
        return resource.UserTags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) ||
            resource.AutoTags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTagContains(Resource resource, string tagFilter)
    {
        var filter = tagFilter.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return resource.UserTags
            .Concat(resource.AutoTags)
            .Any(tag => !string.IsNullOrWhiteSpace(tag) &&
                tag.Trim().TrimStart('#').Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void OnSensorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isMouseDown = true;
        _isDragMoveStarted = false;
        _mouseDownScreenPoint = Win32Helpers.GetCursorScreenPosition();
        EnsureDropIngressListenerReady();
        Mouse.Capture(SensorStrip);
        e.Handled = true;
    }

    private void OnSensorMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown)
        {
            return;
        }

        var cursor = Win32Helpers.GetCursorScreenPosition();
        if (!_isDragMoveStarted)
        {
            var deltaY = Math.Abs(cursor.Y - _mouseDownScreenPoint.Y);
            if (deltaY < DragStartThreshold)
            {
                return;
            }

            _isDragMoveStarted = true;
            _stateMachine.BeginDragMove();
            _dampedDragBehavior.BeginDrag(cursor);
        }

        _dampedDragBehavior.UpdateTarget(cursor);
        RefreshPopupPlacement();
        e.Handled = true;
    }

    private void OnSensorMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown)
        {
            return;
        }

        _isMouseDown = false;
        Mouse.Capture(null);

        if (_isDragMoveStarted)
        {
            _dampedDragBehavior.EndDrag();
            _stateMachine.EndDragMove();
            RefreshPopupPlacement();
        }
        else
        {
            TogglePanelFromSensorClick();
        }

        _isDragMoveStarted = false;

        e.Handled = true;
    }

    private void OnSensorMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DropPopup.IsOpen || CardListPopup.IsOpen)
        {
            _stateMachine.Collapse();
        }

        e.Handled = true;
    }

    private void OnRootDragEnter(object sender, DragEventArgs e)
    {
        if (_isDragOutInProgress)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (IsDropIngressSuppressed())
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        _dragLeaveCollapseTimer.Stop();
        EnsureWindowVisible();
        _stateMachine.DragEnter();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (_isDragOutInProgress)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (IsDropIngressSuppressed())
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnRootDragLeave(object sender, DragEventArgs e)
    {
        if (_isDragOutInProgress || IsDropIngressSuppressed())
        {
            e.Handled = true;
            return;
        }

        _dragLeaveCollapseTimer.Stop();
        if (HasPendingDrop())
        {
            e.Handled = true;
            return;
        }

        _dragLeaveCollapseTimer.Start();
        e.Handled = true;
    }

    private void OnRootDrop(object sender, DragEventArgs e)
    {
        if (_isDragOutInProgress || IsDropIngressSuppressed())
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;

        StagePendingDrop(e.Data, DropIngressChannel.Wpf);
    }

    private void StagePendingDrop(IDataObject dataObject, DropIngressChannel channel)
    {
        if (_isDragOutInProgress || IsDropIngressSuppressed())
        {
            return;
        }

        var drops = DragDropBridge.Extract(dataObject);
        if (drops.Count == 0)
        {
            return;
        }

        _pendingDrops = drops.ToArray();
        _pendingDropChannel = channel;
        DropPanelControl.SetPendingState(true, _pendingDrops.Count);

        _dragLeaveCollapseTimer.Stop();
        _stateMachine.DragEnter();
    }

    private bool HasPendingDrop()
    {
        return _pendingDrops is { Count: > 0 };
    }

    private void ClearPendingDrops()
    {
        _pendingDrops = null;
        _pendingDropChannel = DropIngressChannel.Wpf;
        DropPanelControl.SetPendingState(false);
    }

    private void EnsureWindowVisible()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            Win32Helpers.EnsureTopMostNoActivate(_windowHandle);
        }

        var animation = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ActivateForInteractiveInput()
    {
        EnsureDropIngressListenerReady();

        if (_windowHandle != IntPtr.Zero)
        {
            Win32Helpers.EnsureTopMost(_windowHandle);
        }

        Activate();
    }

    private static void PlayPopupOpenAnimation(FrameworkElement popupBorder, ScaleTransform popupScale)
    {
        popupBorder.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        popupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            From = 0.96,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        popupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            From = 0.96,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (!DropPopup.IsOpen && !CardListPopup.IsOpen)
        {
            return;
        }

        _stateMachine.Collapse();
        e.Handled = true;
    }

    private void TogglePanelFromSensorClick()
    {
        if (DropPopup.IsOpen || CardListPopup.IsOpen)
        {
            _stateMachine.Collapse();
            return;
        }

        if (HasPendingDrop())
        {
            _stateMachine.DragEnter();
            return;
        }

        _stateMachine.Click();
    }

    private void StartDragOut(Resource resource, DragVariant variant)
    {
        SuppressDropIngress(TimeSpan.FromMilliseconds(1800));
        _isDragOutInProgress = true;

        try
        {
            if (variant == DragVariant.Processed && string.IsNullOrWhiteSpace(resource.ProcessedFilePath))
            {
                return;
            }

            var effect = DragDropBridge.DoDragOut(this, resource, variant);
            if (effect != DragDropEffects.None || !IsCursorInsideMainPanel())
            {
                _stateMachine.Collapse();
            }
        }
        catch (Exception ex)
        {
            _runtime.Logger.LogError("拖拽导出失败", ex);
        }
        finally
        {
            _isDragOutInProgress = false;
            SuppressDropIngress(TimeSpan.FromMilliseconds(700));
        }
    }

    private bool IsCursorInsideMainPanel()
    {
        var screen = Win32Helpers.GetCursorScreenPosition();

        if (CardListPopup.IsOpen)
        {
            try
            {
                var pt = CardListPopupBorder.PointToScreen(new Point(0, 0));
                var r = new Rect(pt, new Size(CardListPopupBorder.ActualWidth, CardListPopupBorder.ActualHeight));
                r.Inflate(56, 56);
                if (r.Contains(screen)) return true;
            }
            catch { }
        }

        if (DropPopup.IsOpen)
        {
            try
            {
                var pt = DropPopupBorder.PointToScreen(new Point(0, 0));
                var r = new Rect(pt, new Size(DropPopupBorder.ActualWidth, DropPopupBorder.ActualHeight));
                r.Inflate(56, 56);
                if (r.Contains(screen)) return true;
            }
            catch { }
        }

        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(ActualWidth, ActualHeight));
            rect.Inflate(56, 56);
            return rect.Contains(screen);
        }
        catch
        {
            return false;
        }
    }

    private async Task DeleteCollectionRecordAsync(Resource resource)
    {
        _runtime.PipelineEngine.SuppressResource(resource.Id);
        await _runtime.ResourceManager.DeleteAsync(resource.Id).ConfigureAwait(true);
        await RefreshCardsAsync().ConfigureAwait(true);
    }

    private async Task OnTagToggleRequestedAsync(TagToggleEventArgs args)
    {
        var tag = args.Tag.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (args.IsSelected)
        {
            _pinnedTags.Add(tag);
        }
        else
        {
            _pinnedTags.Remove(tag);
        }

        await RefreshCardsAsync();
    }

    private async Task OnCardResourceConfigChangedAsync(ResourceConfigChangedEventArgs args)
    {
        _runtime.PipelineEngine.ApplyPendingResourceConfiguration(args.Resource);
        await _runtime.ResourceManager.UpdateAsync(args.Resource).ConfigureAwait(true);

        await _runtime.HandleResourceConfigChangedAsync(
                args.Resource,
                args.PreviousPrivacy,
                args.PreviousSyncPolicy,
                args.PreviousProcessingModel,
                args.PreviousPermissionPresetId,
                args.PreviousPersistencePolicy)
            .ConfigureAwait(true);
    }

    private async void OnCardResourceConfigChanged(object? sender, ResourceConfigChangedEventArgs args)
    {
        await _resourceConfigChangeLock.WaitAsync().ConfigureAwait(true);
        try
        {
            await OnCardResourceConfigChangedAsync(args).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _runtime.Logger.LogError("资源配置变更处理失败", ex);
            MessageBox.Show(
                "资源配置保存失败，请查看日志并重试。",
                "Resource Router",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _resourceConfigChangeLock.Release();
        }
    }

    private IReadOnlyList<string> BuildEffectiveTagFilters(IReadOnlyList<string> queryTagFilters)
    {
        var effectiveTags = new HashSet<string>(_pinnedTags, StringComparer.OrdinalIgnoreCase);
        foreach (var queryTag in queryTagFilters)
        {
            var normalized = queryTag.Trim().TrimStart('#');
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                effectiveTags.Add(normalized);
            }
        }

        return effectiveTags.ToArray();
    }

    private static HashSet<string> CollectExistingTags(IReadOnlyList<Resource> resources)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            foreach (var tag in resource.UserTags.Concat(resource.AutoTags))
            {
                var normalized = tag?.Trim().TrimStart('#');
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    tags.Add(normalized);
                }
            }
        }

        return tags;
    }

    private static IReadOnlyList<string> BuildDisplayTags(
        IReadOnlyCollection<string> existingTags,
        IReadOnlyCollection<string> activeTags,
        IReadOnlyCollection<string> queryTags)
    {
        var existingSet = new HashSet<string>(existingTags, StringComparer.OrdinalIgnoreCase);
        var display = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in activeTags)
        {
            if (!string.IsNullOrWhiteSpace(tag) && existingSet.Contains(tag))
            {
                display.Add(tag);
            }
        }

        foreach (var tag in queryTags)
        {
            var normalized = tag.Trim().TrimStart('#');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            foreach (var existingTag in existingSet)
            {
                if (existingTag.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    display.Add(existingTag);
                }
            }
        }

        return display.OrderBy(static t => t, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void UpdateCardListPopupHeight()
    {
        var desiredHeight = CardListControl.GetDesiredPopupHeight();
        if (double.IsNaN(desiredHeight) || double.IsInfinity(desiredHeight) || desiredHeight <= 0)
        {
            desiredHeight = CardListPopupMinHeight;
        }

        CardListPopupBorder.Height = Math.Clamp(Math.Ceiling(desiredHeight), CardListPopupMinHeight, CardListPopupMaxHeight);
    }

    private void SetSensorStripPresentation(SensorStripMode mode)
    {
        var panelOpen = mode != SensorStripMode.Collapsed;
        var targetHostWidth = panelOpen ? SensorHostWidthExpanded : SensorHostWidthCollapsed;
        ResizeHostWidthKeepingRightEdge(targetHostWidth);

        SensorStrip.HorizontalAlignment = mode == SensorStripMode.MainPanel
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Right;
        SensorStrip.Width = SensorStripWidth;
        SensorStrip.Opacity = panelOpen ? 1.0 : 0.86;

        var popupOffset = panelOpen ? PopupOffsetExpanded : PopupOffsetCollapsed;
        DropPopup.HorizontalOffset = popupOffset;
        CardListPopup.HorizontalOffset = popupOffset;

        RefreshPopupPlacement();
    }

    private void ResizeHostWidthKeepingRightEdge(double targetWidth)
    {
        if (Math.Abs(Width - targetWidth) < 0.01)
        {
            return;
        }

        var right = Left + Width;
        Width = targetWidth;
        Left = right - Width;
    }

    private void RefreshPopupPlacement()
    {
        RefreshPopupPlacement(DropPopup);
        RefreshPopupPlacement(CardListPopup);
    }

    private static void RefreshPopupPlacement(Popup popup)
    {
        if (!popup.IsOpen)
        {
            return;
        }

        var horizontalOffset = popup.HorizontalOffset;
        popup.HorizontalOffset = horizontalOffset + 0.1;
        popup.HorizontalOffset = horizontalOffset;

        var verticalOffset = popup.VerticalOffset;
        popup.VerticalOffset = verticalOffset + 0.1;
        popup.VerticalOffset = verticalOffset;
    }

    private bool IsDropIngressSuppressed()
    {
        return DateTimeOffset.UtcNow < _dropIngressSuppressedUntil;
    }

    private void EnsureDropIngressListenerReady()
    {
        if (!IsDropIngressSuppressed())
        {
            RootGrid.AllowDrop = true;
        }
    }

    private void SuppressDropIngress(TimeSpan duration)
    {
        var until = DateTimeOffset.UtcNow.Add(duration);
        if (until > _dropIngressSuppressedUntil)
        {
            _dropIngressSuppressedUntil = until;
        }

        RootGrid.AllowDrop = false;
        var version = ++_dropIngressSuppressVersion;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var remaining = _dropIngressSuppressedUntil - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining);
            }

            if (version != _dropIngressSuppressVersion)
            {
                return;
            }

            if (!IsDropIngressSuppressed())
            {
                EnsureDropIngressListenerReady();
            }
        });
    }

    private ResourceIngestOptions BuildOptionsFromPreset(PermissionPreset preset, ResourceSource source)
    {
        var baseOptions = PermissionPresetProvider.ToIngestOptions(preset, source);
        if (_runtime.Config.EnableAI)
        {
            return baseOptions;
        }

        return new ResourceIngestOptions
        {
            PermissionPresetId = baseOptions.PermissionPresetId,
            Privacy = baseOptions.Privacy,
            SyncPolicy = baseOptions.SyncPolicy,
            ProcessingModel = ModelType.None,
            Source = baseOptions.Source
        };
    }
}