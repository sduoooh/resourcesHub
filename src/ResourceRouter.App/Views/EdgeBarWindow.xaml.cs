using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    private readonly AppRuntime _runtime;
    private readonly ProximityFadeBehavior _proximityFadeBehavior = new();
    private readonly DampedDragBehavior _dampedDragBehavior = new();
    private readonly EdgeBarLayoutTokens _layoutTokens = new();
    private readonly EdgeBarLayoutPolicy _layoutPolicy;
    private readonly EdgeBarStateMachine _stateMachine = new();
    private readonly DropIngressTimingPolicy _dropIngressTimingPolicy = new();
    private readonly DropIngressCoordinator _dropIngressCoordinator = new();
    private readonly System.Threading.SemaphoreSlim _resourceConfigChangeLock = new(1, 1);
    private readonly DispatcherTimer _dragLeaveCollapseTimer = new() { Interval = AppInteractionDefaults.EdgeBar.DragLeaveCollapseDelay };
    private readonly DispatcherTimer _dropIngressRecoveryTimer = new() { Interval = AppInteractionDefaults.EdgeBar.DropIngressRecoveryDelay };
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
    private SensorStripMode _sensorStripMode = SensorStripMode.Collapsed;

    private enum SensorStripMode
    {
        Collapsed,
        DropPanel,
        MainPanel
    }

    public EdgeBarWindow(AppRuntime runtime)
    {
        _runtime = runtime;
        _layoutPolicy = new EdgeBarLayoutPolicy(_layoutTokens);

        InitializeComponent();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SensorStrip.LostMouseCapture += OnSensorLostMouseCapture;
        CardListControl.SetProcessedRouteResolver(_runtime.GetProcessedRouteOptions);
        CardListControl.SetMetadataFacetPolicy(_runtime.MetadataFacetPolicy);
        ConfigureWindowPosition();
        BindBehaviors();
        BindStateMachine();
        BindPipelineEvents();
        BindUiEvents();

        _ = RefreshCardsAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
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
        var workArea = SystemParameters.WorkArea;
        Top = _layoutPolicy.ComputeInitialTop(workArea, Height);
        Opacity = 0;
        SetSensorStripPresentation(SensorStripMode.Collapsed);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SystemParameters.WorkArea), StringComparison.Ordinal))
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - Height);
        Top = Math.Clamp(Top, workArea.Top, maxTop);

        var panelOpen = _sensorStripMode != SensorStripMode.Collapsed;
        var hostHeight = ComputeTargetHostHeight(_sensorStripMode);
        var hostLayout = _layoutPolicy.ComputeHostLayout(workArea, panelOpen, hostHeight);
        ApplyHostLayout(hostLayout);
        RepositionOpenPopups();
    }

    private void BindBehaviors()
    {
        _proximityFadeBehavior.Attach(this);
        _proximityFadeBehavior.OpacityChanged += (_, opacity) => _stateMachine.MouseProximityChanged(opacity);

        _dampedDragBehavior.MaxNormalTopProvider = () =>
        {
            var workArea = SystemParameters.WorkArea;
            var panelHeight = 0.0;
            if (_sensorStripMode == SensorStripMode.DropPanel)
            {
                panelHeight = DropPopupBorder.ActualHeight > 0 ? DropPopupBorder.ActualHeight : DropPopupBorder.DesiredSize.Height;
            }
            else if (_sensorStripMode == SensorStripMode.MainPanel)
            {
                panelHeight = CardListPopupBorder.ActualHeight > 0 ? CardListPopupBorder.ActualHeight : CardListPopupBorder.DesiredSize.Height;
            }
            
            var effectiveMaxHeight = Math.Max(Height, panelHeight);
            return workArea.Bottom - effectiveMaxHeight;
        };
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
                Duration = AppInteractionDefaults.EdgeBar.FadeAnimationDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        };
        _stateMachine.ExpandDropPanelRequested += () =>
        {
            SetSensorStripPresentation(SensorStripMode.DropPanel);
            EnsureWindowVisible();
            OpenPopup(
                popupToOpen: DropPopup,
                popupToClose: CardListPopup,
                popupBorder: DropPopupBorder,
                popupScale: DropPopupScale);
        };
        _stateMachine.ShowCardListRequested += () =>
        {
            SetSensorStripPresentation(SensorStripMode.MainPanel);
            ActivateForInteractiveInput();
            EnsureWindowVisible();
            OpenPopup(
                popupToOpen: CardListPopup,
                popupToClose: DropPopup,
                popupBorder: CardListPopupBorder,
                popupScale: CardListPopupScale);
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
                var dialog = new ConfigDialog(resource, _runtime.MetadataFacetPolicy) { Owner = this };
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
        LocationChanged += (_, _) => RepositionOpenPopups();
        DropPopup.Opened += (_, _) => PositionPopup(DropPopup, DropPopupBorder);
        CardListPopup.Opened += (_, _) => PositionPopup(CardListPopup, CardListPopupBorder);
        DropPopupBorder.SizeChanged += (_, _) => RepositionOpenPopups();
        CardListPopupBorder.SizeChanged += (_, _) => RepositionOpenPopups();

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
        CardListControl.InlineInputChanged += (_, _) => UpdateCardListPopupHeight();
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
                TitleOverride = args.TitleOverride
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
        var effectiveTagFilters = BuildEffectiveTagFilters(queryTagFilters);

        var resources = string.IsNullOrWhiteSpace(searchText)
            ? await _runtime.ResourceManager.ListRecentAsync(
                AppInteractionDefaults.EdgeBar.CardListLimit,
                effectiveTagFilters,
                applyConditionVisibility: true)
            : await _runtime.SearchIndex.QueryAsync(
                searchText,
                limit: AppInteractionDefaults.EdgeBar.CardListLimit,
                offset: 0,
                tagFilters: effectiveTagFilters,
                applyConditionVisibility: true);

        var tagUniverseResources = await _runtime.ResourceManager.ListRecentAsync(
            AppInteractionDefaults.EdgeBar.CardListLimit,
            tagFilters: Array.Empty<string>(),
            applyConditionVisibility: false);
        var (conditionTags, propertyTags) = CollectExistingTags(tagUniverseResources);
        var existingTags = new HashSet<string>(conditionTags, StringComparer.OrdinalIgnoreCase);
        existingTags.UnionWith(propertyTags);
        _pinnedTags.RemoveWhere(tag => !existingTags.Contains(tag));

        var displayTags = BuildDisplayTags(existingTags, _pinnedTags, queryTagFilters);

        CardListControl.SetResources(resources);
        CardListControl.SetTagCatalog(conditionTags, propertyTags);
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

    private Point GetCursorScreenPositionDip()
    {
        var cursorPhys = Win32Helpers.GetCursorScreenPosition();
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            return source.CompositionTarget.TransformFromDevice.Transform(cursorPhys);
        }
        return cursorPhys;
    }

    private void OnSensorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EnsureWindowVisible();
        _stateMachine.PinVisibility();
        _isMouseDown = true;
        _isDragMoveStarted = false;
        _mouseDownScreenPoint = GetCursorScreenPositionDip();
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

        var cursor = GetCursorScreenPositionDip();
        if (!_isDragMoveStarted)
        {
            var deltaX = cursor.X - _mouseDownScreenPoint.X;
            var deltaY = cursor.Y - _mouseDownScreenPoint.Y;
            if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < AppInteractionDefaults.EdgeBar.DragStartThresholdDip)
            {
                return;
            }

            _isDragMoveStarted = true;
            _stateMachine.BeginDragMove();
            _dampedDragBehavior.BeginDrag(cursor);
        }

        _dampedDragBehavior.UpdateTarget(cursor);
        RepositionOpenPopups();
        e.Handled = true;
    }

    private void OnSensorMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown)
        {
            return;
        }

        var wasDragging = _isDragMoveStarted;
        _isMouseDown = false;

        // Perform logic before Mouse.Capture(null) to avoid LostMouseCapture wiping flags
        if (wasDragging)
        {
            _dampedDragBehavior.EndDrag();
            _stateMachine.EndDragMove();
            RepositionOpenPopups();
        }
        else
        {
            TogglePanelFromSensorClick();
        }

        _isDragMoveStarted = false;
        _stateMachine.UnpinVisibility();

        Mouse.Capture(null);
        e.Handled = true;
    }

    private void OnSensorLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown && !_isDragMoveStarted)
        {
            return;
        }

        _isMouseDown = false;
        _isDragMoveStarted = false;
        _stateMachine.UnpinVisibility();
    }

    private void OnSensorMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DropPopup.IsOpen || CardListPopup.IsOpen)
        {
            _stateMachine.Collapse();
        }
        else
        {
            try
            {
                var dataObject = Clipboard.GetDataObject();
                if (dataObject != null)
                {
                    StagePendingDrop(dataObject, DropIngressChannel.Wpf);
                }
            }
            catch
            {
            }
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
            Duration = AppInteractionDefaults.EdgeBar.RevealAnimationDuration,
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
            Duration = AppInteractionDefaults.EdgeBar.PopupOpenAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        popupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            From = AppInteractionDefaults.EdgeBar.PopupOpenScaleFrom,
            To = 1,
            Duration = AppInteractionDefaults.EdgeBar.PopupOpenAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        popupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            From = AppInteractionDefaults.EdgeBar.PopupOpenScaleFrom,
            To = 1,
            Duration = AppInteractionDefaults.EdgeBar.PopupOpenAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void OpenPopup(
        Popup popupToOpen,
        Popup popupToClose,
        FrameworkElement popupBorder,
        ScaleTransform popupScale)
    {
        popupToClose.IsOpen = false;
        PositionPopup(popupToOpen, popupBorder);
        popupToOpen.IsOpen = true;

        // The first open can happen before all visual measurements settle.
        // Reposition once on the dispatcher after layout to avoid first-frame drift.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!popupToOpen.IsOpen)
            {
                return;
            }

            PositionPopup(popupToOpen, popupBorder);
        }), DispatcherPriority.Loaded);

        PlayPopupOpenAnimation(popupBorder, popupScale);
    }

    private void RepositionOpenPopups()
    {
        if (DropPopup.IsOpen)
        {
            PositionPopup(DropPopup, DropPopupBorder);
        }

        if (CardListPopup.IsOpen)
        {
            PositionPopup(CardListPopup, CardListPopupBorder);
        }
    }

    private void PositionPopup(Popup popup, FrameworkElement popupBorder)
    {
        RootGrid.UpdateLayout();
        popupBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var popupWidth = popupBorder.ActualWidth > 0 ? popupBorder.ActualWidth : popupBorder.DesiredSize.Width;
        var popupHeight = popupBorder.ActualHeight > 0 ? popupBorder.ActualHeight : popupBorder.DesiredSize.Height;
        if (popupWidth <= 0 || popupHeight <= 0)
        {
            return;
        }

        var sensorBoundsInWindow = GetStableSensorBoundsInWindow();

        var absolutePos = _layoutPolicy.ComputePopupAbsolutePosition(
            SystemParameters.WorkArea,
            new Rect(Left, Top, Width, Height),
            sensorBoundsInWindow,
            new Size(popupWidth, popupHeight));

        if (Math.Abs(popup.HorizontalOffset - absolutePos.X) < 0.1 && Math.Abs(popup.VerticalOffset - absolutePos.Y) < 0.1)
        {
            popup.HorizontalOffset = absolutePos.X + 0.1;
            popup.VerticalOffset = absolutePos.Y + 0.1;
        }

        popup.HorizontalOffset = absolutePos.X;
        popup.VerticalOffset = absolutePos.Y;
    }

    private Rect GetStableSensorBoundsInWindow()
    {
        var panelOpen = _sensorStripMode != SensorStripMode.Collapsed;
        var hostHeight = ComputeTargetHostHeight(_sensorStripMode);
        var hostLayout = _layoutPolicy.ComputeHostLayout(SystemParameters.WorkArea, panelOpen, hostHeight);

        var sensorWidth = hostLayout.SensorStripWidthDip;
        var sensorLeft = hostLayout.SensorAlignment == HorizontalAlignment.Center
            ? Math.Max(0, (hostLayout.HostWidthDip - sensorWidth) * 0.5)
            : Math.Max(0, hostLayout.HostWidthDip - sensorWidth);

        var sensorHeight = SensorStrip.ActualHeight > 0
            ? SensorStrip.ActualHeight
            : (RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : Height);

        return new Rect(sensorLeft, 0, sensorWidth, sensorHeight);
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            try
            {
                var dataObject = Clipboard.GetDataObject();
                if (dataObject != null)
                {
                    StagePendingDrop(dataObject, DropIngressChannel.Wpf);
                    e.Handled = true;
                }
            }
            catch
            {
            }
            return;
        }

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
        SuppressDropIngress(_dropIngressTimingPolicy.SuppressDuringDragOut);
        _isDragOutInProgress = true;

        try
        {
            if (variant == DragVariant.Processed
                && string.IsNullOrWhiteSpace(resource.ProcessedFilePath)
                && string.IsNullOrWhiteSpace(resource.ProcessedText))
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
            SuppressDropIngress(_dropIngressTimingPolicy.SuppressAfterDragOut);
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
                r.Inflate(AppInteractionDefaults.EdgeBar.CursorInsideInflateDip, AppInteractionDefaults.EdgeBar.CursorInsideInflateDip);
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
                r.Inflate(AppInteractionDefaults.EdgeBar.CursorInsideInflateDip, AppInteractionDefaults.EdgeBar.CursorInsideInflateDip);
                if (r.Contains(screen)) return true;
            }
            catch { }
        }

        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(ActualWidth, ActualHeight));
            rect.Inflate(AppInteractionDefaults.EdgeBar.CursorInsideInflateDip, AppInteractionDefaults.EdgeBar.CursorInsideInflateDip);
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

    private static (HashSet<string> ConditionTags, HashSet<string> PropertyTags) CollectExistingTags(IReadOnlyList<Resource> resources)
    {
        var conditionTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var propertyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            foreach (var tag in resource.ConditionTags)
            {
                var normalized = tag?.Trim().TrimStart('#');
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    conditionTags.Add(normalized);
                }
            }

            foreach (var tag in resource.PropertyTags)
            {
                var normalized = tag?.Trim().TrimStart('#');
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    propertyTags.Add(normalized);
                }
            }
        }

        return (conditionTags, propertyTags);
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
            desiredHeight = AppInteractionDefaults.EdgeBar.CardListPopupContentMinHeightDip;
        }

        var targetHeight = Math.Clamp(
            Math.Ceiling(desiredHeight),
            AppInteractionDefaults.EdgeBar.CardListPopupContentMinHeightDip,
            AppInteractionDefaults.EdgeBar.CardListPopupMaxHeightDip);
        CardListPopupBorder.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation
        {
            To = targetHeight,
            Duration = AppInteractionDefaults.EdgeBar.FadeAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetSensorStripPresentation(SensorStripMode mode)
    {
        _sensorStripMode = mode;
        var panelOpen = mode != SensorStripMode.Collapsed;
        var hostHeight = ComputeTargetHostHeight(mode);
        var hostLayout = _layoutPolicy.ComputeHostLayout(SystemParameters.WorkArea, panelOpen, hostHeight);
        ApplyHostLayout(hostLayout);

        RepositionOpenPopups();
    }

    private double ComputeTargetHostHeight(SensorStripMode mode)
    {
        if (mode == SensorStripMode.Collapsed)
            return AppInteractionDefaults.EdgeBar.WindowHeightDip;

        if (mode == SensorStripMode.MainPanel)
            return AppInteractionDefaults.EdgeBar.CardListPopupVisualMinHeightDip / 3.0;

        DropPopupBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var h = DropPopupBorder.DesiredSize.Height;
        return (h > 0 ? h : 200) / 2.0;
    }

    private void ApplyHostLayout(EdgeBarHostLayout hostLayout)
    {
        Width = hostLayout.HostWidthDip;
        Left = hostLayout.HostLeftDip;
        SensorStrip.HorizontalAlignment = hostLayout.SensorAlignment;
        SensorStrip.Width = hostLayout.SensorStripWidthDip;
        SensorStrip.Opacity = hostLayout.SensorOpacity;

        var anim = new DoubleAnimation
        {
            To = hostLayout.HostHeightDip,
            Duration = AppInteractionDefaults.EdgeBar.PopupOpenAnimationDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(HeightProperty, anim, HandoffBehavior.SnapshotAndReplace);
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