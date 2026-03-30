using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ResourceRouter.App.Interop;
using ResourceRouter.App.State;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;

namespace ResourceRouter.App.Views;

public partial class ResourceCard : UserControl, INotifyPropertyChanged
{
    private const double DragExportTriggerPadding = AppInteractionDefaults.ResourceCard.DragExportTriggerPadding;
    public static readonly DependencyProperty ResourceProperty = DependencyProperty.Register(
        nameof(Resource),
        typeof(Resource),
        typeof(ResourceCard),
        new PropertyMetadata(null, OnResourceChanged));

    private const double DeleteRevealMaxWidth = AppInteractionDefaults.ResourceCard.DeleteRevealMaxWidth;
    private const double DeleteRevealCommitWidth = AppInteractionDefaults.ResourceCard.DeleteRevealCommitWidth;
    private const double DeleteSwipeHandleWidth = AppInteractionDefaults.ResourceCard.DeleteSwipeHandleWidth;
    private const double DeleteSwipeMoveThreshold = AppInteractionDefaults.ResourceCard.DeleteSwipeMoveThreshold;
    private static readonly CornerRadius CardCornerCollapsed = new(8);
    private static readonly CornerRadius CardCornerWithInlineConfig = new(8, 8, 0, 0);
    private static readonly CornerRadius ConfigCornerCollapsed = new(10);
    private static readonly CornerRadius ConfigCornerExpanded = new(0, 0, 10, 10);
    private static readonly IReadOnlyList<OptionItem<PrivacyLevel>> PrivacyOptions = new[]
    {
        new OptionItem<PrivacyLevel>(PrivacyLevel.Private, "私有（本地可见）"),
        new OptionItem<PrivacyLevel>(PrivacyLevel.Public, "公开（可共享）")
    };
    private static readonly IReadOnlyList<OptionItem<SyncPolicy>> SyncOptions = new[]
    {
        new OptionItem<SyncPolicy>(SyncPolicy.LocalOnly, "仅本地"),
        new OptionItem<SyncPolicy>(SyncPolicy.CloudDefault, "云端默认同步")
    };
    private static readonly IReadOnlyList<OptionItem<ModelType>> ModelOptions = new[]
    {
        new OptionItem<ModelType>(ModelType.None, "不分析（仅保存）"),
        new OptionItem<ModelType>(ModelType.LocalSmall, "本地小模型"),
        new OptionItem<ModelType>(ModelType.CloudAI, "云端 AI")
    };
    private static readonly IReadOnlyList<OptionItem<PersistencePolicy>> PersistenceOptions = new[]
    {
        new OptionItem<PersistencePolicy>(PersistencePolicy.InPlace, "原地存储（仅引用）"),
        new OptionItem<PersistencePolicy>(PersistencePolicy.Unified, "统一存储（拷贝转存）"),
        new OptionItem<PersistencePolicy>(PersistencePolicy.Backup, "备份存储（冗余同步）")
    };

    private readonly DispatcherTimer _countdownTimer;
    private readonly IReadOnlyList<PermissionPreset> _presets;
    private IReadOnlyList<ProcessedRouteOption> _processedRouteOptions = Array.Empty<ProcessedRouteOption>();
    private Point _dragStartPoint;
    private bool _isRawDragArmed;
    private bool _isProcessedDragArmed;
    private bool _hasDragThresholdReached;
    private bool _isPointerOutsideMainPanel;
    private DateTimeOffset? _pointerOutsideSince;
    private UIElement? _activeDragSurface;
    private Border? _activeDragSurfaceBorder;
    private Border? _activeDragPlaceholder;
    private TranslateTransform? _activeDragTransform;
    private int _cardBaseZIndex;
    private bool _isCardZIndexElevated;
    private bool _isDragVisualFloating;

    private bool _isActionPanelVisible;
    private bool _isDragInteractionsLocked;
    private bool _isInlineConfigOpen;
    private bool _isInlineConfigInitializing;
    private bool _isDeleteSwipeArmed;
    private bool _isDeleteSwipeMoved;
    private bool _isCardTapCandidate;
    private bool _newTagAsCondition;
    private Point _deleteSwipeStartPoint;
    private double _deleteRevealStartWidth;
    private double _deleteRevealWidth;
    private readonly HashSet<string> _conditionTagCatalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _propertyTagCatalog = new(StringComparer.OrdinalIgnoreCase);
    private IResourceMetadataFacetPolicy _metadataFacetPolicy = new DefaultResourceMetadataFacetPolicy();

    public ResourceCard()
    {
        InitializeComponent();

        _presets = PermissionPreset.BuiltIn.Values
            .OrderBy(static preset => preset.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        InlinePresetCombo.ItemsSource = _presets;
        InlinePresetCombo.DisplayMemberPath = nameof(PermissionPreset.DisplayName);

        InlinePrivacyCombo.ItemsSource = PrivacyOptions;
        InlinePrivacyCombo.DisplayMemberPath = nameof(OptionItem<PrivacyLevel>.Label);

        InlineSyncCombo.ItemsSource = SyncOptions;
        InlineSyncCombo.DisplayMemberPath = nameof(OptionItem<SyncPolicy>.Label);

        InlineModelCombo.ItemsSource = ModelOptions;
        InlineModelCombo.DisplayMemberPath = nameof(OptionItem<ModelType>.Label);

        InlinePersistenceCombo.ItemsSource = PersistenceOptions;
        InlinePersistenceCombo.DisplayMemberPath = nameof(OptionItem<PersistencePolicy>.Label);

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        };

        UpdateVisualState();
        SetActionPanelVisible(false, animate: false);
        SetDeleteRevealWidth(0, animate: false);
        ApplyInlineCornerPresentation(visible: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ResourceEventArgs>? RawDragRequested;
    public event EventHandler<ResourceEventArgs>? ProcessedDragRequested;
    public event EventHandler<ResourceEventArgs>? CollectionDeleteRequested;
    public event EventHandler<ResourceConfigChangedEventArgs>? ConfigChanged;
    public event EventHandler? TextChanged;
    public event EventHandler<ResourceConfigModeChangedEventArgs>? ConfigModeChanged;
    public event EventHandler? InteractionActivated;

    public Resource? Resource
    {
        get => (Resource?)GetValue(ResourceProperty);
        set => SetValue(ResourceProperty, value);
    }

    public bool IsInlineConfigOpen => _isInlineConfigOpen;

    public bool HasInlineDropDownOpen =>
        InlinePresetCombo.IsDropDownOpen ||
        InlinePrivacyCombo.IsDropDownOpen ||
        InlineSyncCombo.IsDropDownOpen ||
        InlineModelCombo.IsDropDownOpen ||
        InlineRouteCombo.IsDropDownOpen ||
        InlinePersistenceCombo.IsDropDownOpen;

    public Visibility DetailVisibility => _isInlineConfigOpen ? Visibility.Collapsed : Visibility.Visible;

    public bool IsProcessedRouteLocked => _processedRouteOptions.Count == 0;

    public string ProcessedRouteSummary => IsProcessedRouteLocked
        ? "无可用路由（已锁定）"
        : "拖拽导出";

    public string StatusText
    {
        get
        {
            if (Resource is null)
            {
                return string.Empty;
            }

            return Resource.State switch
            {
                ResourceState.Waiting when Resource.WaitingExpiresAt.HasValue =>
                    BuildWaitingText(Resource.WaitingExpiresAt.Value),
                ResourceState.Waiting => "等待处理",
                ResourceState.Processing => "处理中",
                ResourceState.Ready => string.IsNullOrWhiteSpace(Resource.Summary)
                    ? "就绪"
                    : Resource.Summary,
                ResourceState.Error => $"错误: {Resource.LastError}",
                _ => Resource.State.ToString()
            };
        }
    }

    public IReadOnlyList<string> VisibleTags
    {
        get
        {
            if (Resource is null)
            {
                return Array.Empty<string>();
            }

            var metadataFacet = _metadataFacetPolicy.Read(Resource);

            return metadataFacet.ConditionTags
                .Concat(metadataFacet.PropertyTags)
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => $"#{tag.Trim().TrimStart('#')}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
        }
    }

    public void SetTagCatalog(IReadOnlyCollection<string> conditionTags, IReadOnlyCollection<string> propertyTags)
    {
        _conditionTagCatalog.Clear();
        foreach (var tag in conditionTags)
        {
            var normalized = NormalizeTagText(tag);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _conditionTagCatalog.Add(normalized);
            }
        }

        _propertyTagCatalog.Clear();
        foreach (var tag in propertyTags)
        {
            var normalized = NormalizeTagText(tag);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _propertyTagCatalog.Add(normalized);
            }
        }

        RefreshInlineTagViews();
    }

    public void SetMetadataFacetPolicy(IResourceMetadataFacetPolicy metadataFacetPolicy)
    {
        _metadataFacetPolicy = metadataFacetPolicy ?? throw new ArgumentNullException(nameof(metadataFacetPolicy));
    }

    public void DeactivateInteractions()
    {
        ClearArmedDrag(animateBack: false);
        _isCardTapCandidate = false;
        EndDeleteSwipe(keepOpen: false);
        SetActionPanelVisible(false);
        SetDeleteRevealWidth(0);
    }

    public void SetDragInteractionsLocked(bool locked)
    {
        _isDragInteractionsLocked = locked;
        if (locked)
        {
            ClearArmedDrag(animateBack: false);
            SetActionPanelVisible(false);
            EndDeleteSwipe(keepOpen: false);
            SetDeleteRevealWidth(0);
        }
    }

    public void SetProcessedRouteOptions(IReadOnlyList<ProcessedRouteOption> routes)
    {
        _processedRouteOptions = routes ?? Array.Empty<ProcessedRouteOption>();

        if (Resource is not null)
        {
            if (_processedRouteOptions.Count == 0)
            {
                Resource.ProcessedRouteId = null;
            }
            else if (string.IsNullOrWhiteSpace(Resource.ProcessedRouteId) ||
                     !_processedRouteOptions.Any(route =>
                         string.Equals(route.RouteId, Resource.ProcessedRouteId, StringComparison.OrdinalIgnoreCase)))
            {
                Resource.ProcessedRouteId = _processedRouteOptions[0].RouteId;
            }
        }

        InlineRouteCombo.ItemsSource = _processedRouteOptions;
        InlineRouteCombo.DisplayMemberPath = nameof(ProcessedRouteOption.DisplayName);

        SyncInlineConfigFromResource();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsProcessedRouteLocked)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProcessedRouteSummary)));
    }

    public bool CloseInlineConfigEditor(bool commitChanges)
    {
        if (!_isInlineConfigOpen)
        {
            return true;
        }

        if (commitChanges && !TryCommitInlineConfig())
        {
            return false;
        }

        SetInlineConfigVisible(false);
        return true;
    }

    private static void OnResourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ResourceCard card)
        {
            card.UpdateVisualState();
            card.SyncInlineConfigFromResource();
            card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(StatusText)));
            card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(VisibleTags)));
            card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(DetailVisibility)));
            card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(IsProcessedRouteLocked)));
            card.PropertyChanged?.Invoke(card, new PropertyChangedEventArgs(nameof(ProcessedRouteSummary)));
        }
    }

    private void UpdateVisualState()
    {
        if (Resource?.State == ResourceState.Waiting)
        {
            _countdownTimer.Start();
        }
        else
        {
            _countdownTimer.Stop();
        }
    }

    private void OnCardPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Resource is null)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (IsDescendantOf(source, HoverActions) || IsDescendantOf(source, DeleteCollectionButton))
        {
            InteractionActivated?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_isInlineConfigOpen)
        {
            if (IsDescendantOf(source, InlineConfigHost) ||
                IsInlineConfigEditorInteraction(source) ||
                HasInlineDropDownOpen)
            {
                InteractionActivated?.Invoke(this, EventArgs.Empty);
                return;
            }

            e.Handled = true;
            if (TryCommitInlineConfig())
            {
                SetInlineConfigVisible(false);
            }

            return;
        }

        InteractionActivated?.Invoke(this, EventArgs.Empty);

        if (_isDragInteractionsLocked)
        {
            e.Handled = true;
            return;
        }

        var point = e.GetPosition(CardChrome);
        var inDeleteHandle = true;

        _isCardTapCandidate = true;
        if (inDeleteHandle)
        {
            BeginDeleteSwipe(point);
        }

        e.Handled = true;
    }

    private void OnCardPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Resource is null)
        {
            return;
        }

        InteractionActivated?.Invoke(this, EventArgs.Empty);
        if (_isInlineConfigOpen)
        {
            CloseInlineConfigEditor(commitChanges: true);
        }
        else
        {
            if (_isDragInteractionsLocked)
            {
                e.Handled = true;
                return;
            }

            OpenInlineConfigEditor();
        }

        e.Handled = true;
    }

    private void OnCardPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDeleteSwipeArmed)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDeleteSwipe(keepOpen: _deleteRevealWidth >= DeleteRevealCommitWidth);
            _isCardTapCandidate = false;
            return;
        }

        var current = e.GetPosition(CardChrome);
        var delta = _deleteSwipeStartPoint.X - current.X;

        if (!_isDeleteSwipeMoved)
        {
            if (Math.Abs(delta) < DeleteSwipeMoveThreshold)
            {
                return;
            }

            // Left swipe opens delete reveal; right swipe closes when already opened.
            if (delta <= 0 && _deleteRevealStartWidth <= 0.1)
            {
                return;
            }

            _isDeleteSwipeMoved = true;
            _isCardTapCandidate = false;
            SetActionPanelVisible(false);
        }

        var revealWidth = Math.Clamp(_deleteRevealStartWidth + delta, 0, DeleteRevealMaxWidth);
        SetDeleteRevealWidth(revealWidth, animate: false);
        e.Handled = true;
    }

    private void OnCardPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDeleteSwipeArmed)
        {
            var keepDeleteReveal = _isDeleteSwipeMoved && _deleteRevealWidth >= DeleteRevealCommitWidth;
            EndDeleteSwipe(keepOpen: keepDeleteReveal);
        }

        if (_isCardTapCandidate && !_isDragInteractionsLocked && !_isInlineConfigOpen)
        {
            SetDeleteRevealWidth(0);
            SetActionPanelVisible(true);
        }

        _isCardTapCandidate = false;
        e.Handled = true;
    }

    private void OnDeleteCollectionClick(object sender, RoutedEventArgs e)
    {
        if (Resource is null)
        {
            return;
        }

        CollectionDeleteRequested?.Invoke(this, new ResourceEventArgs { Resource = Resource });
        DeactivateInteractions();
        e.Handled = true;
    }

    private void OnRawActionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isInlineConfigOpen || _isDragInteractionsLocked)
        {
            return;
        }

        ArmDrag(sender as Border, RawActionPlaceholder, isRaw: true, e);
    }

    private void OnProcessedActionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isInlineConfigOpen || _isDragInteractionsLocked || IsProcessedRouteLocked)
        {
            e.Handled = true;
            return;
        }

        ArmDrag(sender as Border, ProcessedActionPlaceholder, isRaw: false, e);
    }

    private void OnActionSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        HandleArmedDragMouseMove(e);
    }

    private void OnActionSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteArmedDrag();
        e.Handled = true;
    }

    private void OnRootPreviewMouseMove(object sender, MouseEventArgs e)
    {
        HandleArmedDragMouseMove(e);
    }

    private void OnRootPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRawDragArmed && !_isProcessedDragArmed)
        {
            return;
        }

        CompleteArmedDrag();
        e.Handled = true;
    }

    private void HandleArmedDragMouseMove(MouseEventArgs e)
    {
        if (Resource is null || _isInlineConfigOpen || _isDragInteractionsLocked)
        {
            ClearArmedDrag(animateBack: false);
            return;
        }

        if (!_isRawDragArmed && !_isProcessedDragArmed)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearArmedDrag(animateBack: true);
            return;
        }

        var currentPoint = e.GetPosition(this);
        var deltaX = Math.Abs(currentPoint.X - _dragStartPoint.X);
        var deltaY = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

        if (deltaX < SystemParameters.MinimumHorizontalDragDistance &&
            deltaY < SystemParameters.MinimumVerticalDragDistance)
        {
            UpdateDragVisual(currentPoint);
            return;
        }

        _hasDragThresholdReached = true;

        UpdateDragVisual(currentPoint);

        _isPointerOutsideMainPanel = !IsCursorInsideMainPanel();
        if (_isPointerOutsideMainPanel)
        {
            var now = DateTimeOffset.UtcNow;
            _pointerOutsideSince ??= now;
            var outsideDuration = now - _pointerOutsideSince.Value;

            if (outsideDuration >= AppInteractionDefaults.ResourceCard.OutsideMainPanelTrigger)
            {
                StartExportDragOut();
            }
        }
        else
        {
            _pointerOutsideSince = null;
        }

        e.Handled = true;
    }

    private void ArmDrag(Border? surface, Border? placeholder, bool isRaw, MouseButtonEventArgs e)
    {
        if (Resource is null || surface is null)
        {
            return;
        }

        _isRawDragArmed = isRaw;
        _isProcessedDragArmed = !isRaw;
        _hasDragThresholdReached = false;
        _isPointerOutsideMainPanel = false;
        _pointerOutsideSince = null;
        _dragStartPoint = e.GetPosition(this);
        _activeDragSurface = surface;
        _activeDragSurfaceBorder = surface;
        _activeDragPlaceholder = placeholder;
        _activeDragTransform = surface.RenderTransform as TranslateTransform;
        _isDragVisualFloating = false;

        if (_activeDragPlaceholder is not null)
        {
            _activeDragPlaceholder.Visibility = Visibility.Visible;
        }

        UpdateDragGhostContent(isRaw);
        UpdateDragVisual(_dragStartPoint);
        Mouse.Capture(this, CaptureMode.SubTree);
        e.Handled = true;
    }

    private void ClearArmedDrag(bool animateBack)
    {
        _isRawDragArmed = false;
        _isProcessedDragArmed = false;
        _hasDragThresholdReached = false;
        _isPointerOutsideMainPanel = false;
        _pointerOutsideSince = null;

        ResetDragVisual(animateBack);

        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }

        if (_activeDragSurface is not null)
        {
            _activeDragSurface.ReleaseMouseCapture();
            _activeDragSurface = null;
        }

        _activeDragSurfaceBorder = null;
        _activeDragPlaceholder = null;
        _activeDragTransform = null;
    }

    private void UpdateDragVisual(Point currentPoint)
    {
        if (_activeDragSurfaceBorder is null)
        {
            return;
        }

        if (!_isDragVisualFloating)
        {
            _isDragVisualFloating = true;
            Panel.SetZIndex(_activeDragSurfaceBorder, 9);
            _activeDragSurfaceBorder.Opacity = 0;
            ElevateCardZIndexForDrag();
        }

        var screen = Win32Helpers.GetCursorScreenPosition();
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var dip = source.CompositionTarget.TransformFromDevice.Transform(screen);
        var offsetX = dip.X + 2;
        var offsetY = dip.Y + 2;

        DragGhostPopup.HorizontalOffset = offsetX;
        DragGhostPopup.VerticalOffset = offsetY;
        if (!DragGhostPopup.IsOpen)
        {
            DragGhostPopup.IsOpen = true;
        }
    }

    private void ResetDragVisual(bool animateBack)
    {
        if (_activeDragSurfaceBorder is null)
        {
            DragGhostPopup.IsOpen = false;
            RestoreCardZIndexAfterDrag();
            if (_activeDragPlaceholder is not null)
            {
                _activeDragPlaceholder.Visibility = Visibility.Collapsed;
            }

            return;
        }

        Panel.SetZIndex(_activeDragSurfaceBorder, 0);
        _activeDragSurfaceBorder.Opacity = 1;
        _isDragVisualFloating = false;
        DragGhostPopup.IsOpen = false;
        RestoreCardZIndexAfterDrag();

        if (!animateBack || _activeDragTransform is null)
        {
            _activeDragSurfaceBorder.BeginAnimation(OpacityProperty, null);
            if (_activeDragTransform is not null)
            {
                _activeDragTransform.BeginAnimation(TranslateTransform.XProperty, null);
                _activeDragTransform.BeginAnimation(TranslateTransform.YProperty, null);
                _activeDragTransform.X = 0;
                _activeDragTransform.Y = 0;
            }

            if (_activeDragPlaceholder is not null)
            {
                _activeDragPlaceholder.Visibility = Visibility.Collapsed;
            }

            return;
        }

        try
        {
            var screenPoint = Win32Helpers.GetCursorScreenPosition();
            var localPoint = this.PointFromScreen(screenPoint);
            _activeDragTransform.X = localPoint.X - _dragStartPoint.X;
            _activeDragTransform.Y = localPoint.Y - _dragStartPoint.Y;
        }
        catch { }

        var animationX = new DoubleAnimation
        {
            To = 0,
            Duration = AppInteractionDefaults.ResourceCard.DragResetAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var animationY = new DoubleAnimation
        {
            To = 0,
            Duration = AppInteractionDefaults.ResourceCard.DragResetAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        animationY.Completed += (_, _) =>
        {
            if (_activeDragPlaceholder is not null)
            {
                _activeDragPlaceholder.Visibility = Visibility.Collapsed;
            }
        };

        _activeDragTransform.BeginAnimation(TranslateTransform.XProperty, animationX, HandoffBehavior.SnapshotAndReplace);
        _activeDragTransform.BeginAnimation(TranslateTransform.YProperty, animationY, HandoffBehavior.SnapshotAndReplace);
    }

    private bool IsCursorInsideMainPanel()
    {
        var screen = Win32Helpers.GetCursorScreenPosition();

        if (!TryGetPrimaryPanelBounds(out var rect))
        {
            return true;
        }

        rect.Inflate(DragExportTriggerPadding, DragExportTriggerPadding);
        return rect.Contains(screen);
    }

    private bool TryGetPrimaryPanelBounds(out Rect rect)
    {
        rect = Rect.Empty;

        var cardListPanel = FindAncestor<CardListPanel>(this);
        if (TryBuildScreenRect(cardListPanel, out rect))
        {
            return true;
        }

        if (TryBuildScreenRect(this, out rect))
        {
            return true;
        }

        var hostWindow = Window.GetWindow(this);
        return TryBuildScreenRect(hostWindow, out rect);
    }

    private static bool TryBuildScreenRect(FrameworkElement? element, out Rect rect)
    {
        rect = Rect.Empty;
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = element.PointToScreen(new Point(0, 0));
        rect = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        return rect.Width > 0 && rect.Height > 0;
    }

    private void OnInlineOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        _ = TryCommitInlineConfig();
    }

    private void OnInlineOptionDropDownClosed(object sender, EventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        _ = TryCommitInlineConfig();
    }

    private void BeginDeleteSwipe(Point startPoint)
    {
        _isDeleteSwipeArmed = true;
        _isDeleteSwipeMoved = false;
        _deleteSwipeStartPoint = startPoint;
        _deleteRevealStartWidth = _deleteRevealWidth;
        CardChrome.CaptureMouse();
    }

    private void EndDeleteSwipe(bool keepOpen)
    {
        _isDeleteSwipeArmed = false;
        _isDeleteSwipeMoved = false;
        CardChrome.ReleaseMouseCapture();

        var targetWidth = keepOpen ? DeleteRevealMaxWidth : 0;
        SetDeleteRevealWidth(targetWidth);
    }

    private void SetActionPanelVisible(bool visible, bool animate = true)
    {
        if (visible && (_isInlineConfigOpen || _isDragInteractionsLocked))
        {
            visible = false;
        }

        _isActionPanelVisible = visible;

        if (!animate)
        {
            HoverMask.Opacity = visible ? 1 : 0;
            HoverActions.Opacity = visible ? 1 : 0;
            HoverActions.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            HoverActions.IsHitTestVisible = visible;
            return;
        }

        if (visible)
        {
            HoverActions.Visibility = Visibility.Visible;
            HoverActions.IsHitTestVisible = true;
            AnimateOpacity(HoverMask, 1);
            AnimateOpacity(HoverActions, 1);
            return;
        }

        HoverActions.IsHitTestVisible = false;
        var actionFade = BuildOpacityAnimation(0);
        actionFade.Completed += (_, _) =>
        {
            if (!_isActionPanelVisible)
            {
                HoverActions.Visibility = Visibility.Collapsed;
            }
        };

        HoverActions.BeginAnimation(OpacityProperty, actionFade, HandoffBehavior.SnapshotAndReplace);
        AnimateOpacity(HoverMask, 0);
    }

    private void SetDeleteRevealWidth(double width, bool animate = true)
    {
        width = Math.Clamp(width, 0, DeleteRevealMaxWidth);
        _deleteRevealWidth = width;

        var targetOffset = -width;
        DeleteRevealPanel.IsHitTestVisible = width >= DeleteRevealCommitWidth;

        if (!animate)
        {
            CardTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            CardTranslate.X = targetOffset;
            return;
        }

        CardTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = targetOffset,
            Duration = AppInteractionDefaults.ResourceCard.DeleteRevealAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void OpenInlineConfigEditor()
    {
        SyncInlineConfigFromResource();
        SetDeleteRevealWidth(0);
        SetActionPanelVisible(false);
        ClearArmedDrag(animateBack: false);
        SetInlineConfigVisible(true);
    }

    private void SetInlineConfigVisible(bool visible)
    {
        if (_isInlineConfigOpen == visible)
        {
            return;
        }

        _isInlineConfigOpen = visible;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailVisibility)));
        ApplyInlineCornerPresentation(visible);
        var resource = Resource;
        if (resource is not null)
        {
            ConfigModeChanged?.Invoke(this, new ResourceConfigModeChangedEventArgs
            {
                Resource = resource,
                IsOpen = visible
            });
        }

        if (visible)
        {
            InlineConfigHost.Visibility = Visibility.Visible;
            InlineConfigHost.BeginAnimation(OpacityProperty, BuildOpacityAnimation(1), HandoffBehavior.SnapshotAndReplace);
            return;
        }

        var fadeOut = BuildOpacityAnimation(0);
        fadeOut.Completed += (_, _) =>
        {
            if (!_isInlineConfigOpen)
            {
                InlineConfigHost.Visibility = Visibility.Collapsed;
            }
        };

        InlineConfigHost.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void SyncInlineConfigFromResource()
    {
        if (Resource is null)
        {
            return;
        }

        _isInlineConfigInitializing = true;
        try
        {
            var presetId = string.IsNullOrWhiteSpace(Resource.PermissionPresetId)
                ? PermissionPreset.PrivatePresetId
                : Resource.PermissionPresetId;

            InlinePresetCombo.SelectedItem = _presets.FirstOrDefault(preset =>
                string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase));
            InlinePrivacyCombo.SelectedItem = PrivacyOptions.FirstOrDefault(option => option.Value == Resource.Privacy);
            InlineSyncCombo.SelectedItem = SyncOptions.FirstOrDefault(option => option.Value == Resource.SyncPolicy);
            InlineModelCombo.SelectedItem = ModelOptions.FirstOrDefault(option => option.Value == Resource.ProcessingModel);
            InlinePersistenceCombo.SelectedItem = PersistenceOptions.FirstOrDefault(option => option.Value == Resource.PersistencePolicy);
            InlinePersistenceCombo.IsEnabled = PersistencePolicyRules.CanConfigurePolicy(Resource);

            if (InlinePrivacyCombo.SelectedIndex < 0)
            {
                InlinePrivacyCombo.SelectedIndex = 0;
            }

            if (InlineSyncCombo.SelectedIndex < 0)
            {
                InlineSyncCombo.SelectedIndex = 0;
            }

            if (InlineModelCombo.SelectedIndex < 0)
            {
                var defaultModelIndex = ModelOptions
                    .Select((option, index) => new { option.Value, index })
                    .FirstOrDefault(pair => pair.Value == ModelType.LocalSmall)?.index ?? 0;
                InlineModelCombo.SelectedIndex = defaultModelIndex;
            }

            var metadataFacet = _metadataFacetPolicy.Read(Resource);
            InlineTitleBox.Text = metadataFacet.TitleOverride ?? string.Empty;
            InlineAnnotationsBox.Text = metadataFacet.Annotations ?? string.Empty;

            InlineRouteCombo.IsEnabled = _processedRouteOptions.Count > 0;
            if (_processedRouteOptions.Count == 0)
            {
                InlineRouteCombo.SelectedIndex = -1;
            }
            else
            {
                InlineRouteCombo.SelectedItem = _processedRouteOptions.FirstOrDefault(route =>
                    string.Equals(route.RouteId, Resource.ProcessedRouteId, StringComparison.OrdinalIgnoreCase));
                if (InlineRouteCombo.SelectedIndex < 0)
                {
                    InlineRouteCombo.SelectedIndex = 0;
                }
            }
        }
        finally
        {
            _isInlineConfigInitializing = false;
        }

        _newTagAsCondition = false;
        InlineTagSearchBox.Text = string.Empty;
        InlineTagInputBox.Text = string.Empty;
        UpdateTagTypeToggleText();
        RefreshInlineTagViews();
    }

    private void OnInlineTagSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        RefreshInlineTagViews();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnInlineTagInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnInlineMetadataTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnInlineTagTypeToggleClick(object sender, RoutedEventArgs e)
    {
        _newTagAsCondition = !_newTagAsCondition;
        UpdateTagTypeToggleText();
    }

    private void OnInlineTagInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        AddNewInlineTag();
        e.Handled = true;
    }

    private void OnInlineTagAddClick(object sender, RoutedEventArgs e)
    {
        AddNewInlineTag();
    }

    private void AddNewInlineTag()
    {
        if (Resource is null)
        {
            return;
        }

        var normalized = NormalizeTagText(InlineTagInputBox.Text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var conditionTags = GetConditionTags();
        var propertyTags = GetPropertyTags();
        if (_newTagAsCondition)
        {
            propertyTags.Remove(normalized);
            conditionTags.Add(normalized);
            _conditionTagCatalog.Add(normalized);
        }
        else
        {
            conditionTags.Remove(normalized);
            propertyTags.Add(normalized);
            _propertyTagCatalog.Add(normalized);
        }

        ApplyTagChanges(conditionTags, propertyTags);
        InlineTagInputBox.Text = string.Empty;
        RefreshInlineTagViews();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnInlineTagChipToggle(object sender, RoutedEventArgs e)
    {
        if (Resource is null || sender is not ToggleButton chip || chip.Tag is not string tag)
        {
            return;
        }

        var normalized = NormalizeTagText(tag);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var conditionTags = GetConditionTags();
        var propertyTags = GetPropertyTags();
        var isSelected = chip.IsChecked == true;

        if (isSelected)
        {
            var addAsCondition = _conditionTagCatalog.Contains(normalized) && !_propertyTagCatalog.Contains(normalized);
            if (addAsCondition)
            {
                conditionTags.Add(normalized);
                propertyTags.Remove(normalized);
            }
            else
            {
                propertyTags.Add(normalized);
                conditionTags.Remove(normalized);
            }
        }
        else
        {
            conditionTags.Remove(normalized);
            propertyTags.Remove(normalized);
        }

        ApplyTagChanges(conditionTags, propertyTags);
        RefreshInlineTagViews();
    }

    private void ApplyTagChanges(HashSet<string> conditionTags, HashSet<string> propertyTags)
    {
        if (Resource is null)
        {
            return;
        }

        var currentFacet = _metadataFacetPolicy.Read(Resource);
        var changed = _metadataFacetPolicy.Apply(Resource, new ResourceMetadataFacet
        {
            TitleOverride = currentFacet.TitleOverride,
            Annotations = currentFacet.Annotations,
            Summary = currentFacet.Summary,
            ConditionTags = conditionTags.OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase).ToArray(),
            PropertyTags = propertyTags.OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase).ToArray(),
            OriginalFileName = currentFacet.OriginalFileName,
            MimeType = currentFacet.MimeType,
            FileSize = currentFacet.FileSize,
            Source = currentFacet.Source,
            CreatedAt = currentFacet.CreatedAt,
            ExtensionMetadata = currentFacet.ExtensionMetadata
        });

        if (!changed)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleTags)));

        ConfigChanged?.Invoke(this, new ResourceConfigChangedEventArgs
        {
            Resource = Resource,
            PreviousPrivacy = Resource.Privacy,
            PreviousSyncPolicy = Resource.SyncPolicy,
            PreviousProcessingModel = Resource.ProcessingModel,
            PreviousPermissionPresetId = Resource.PermissionPresetId,
            PreviousProcessedRouteId = Resource.ProcessedRouteId,
            PreviousPersistencePolicy = Resource.PersistencePolicy
        });
    }

    private HashSet<string> GetConditionTags()
    {
        if (Resource is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var metadataFacet = _metadataFacetPolicy.Read(Resource);

        return metadataFacet.ConditionTags
            .Select(NormalizeTagText)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private HashSet<string> GetPropertyTags()
    {
        if (Resource is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var metadataFacet = _metadataFacetPolicy.Read(Resource);

        return metadataFacet.PropertyTags
            .Select(NormalizeTagText)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshInlineTagViews()
    {
        if (InlineTagChipsHost is null)
        {
            return;
        }

        if (InlineTagChipsContainer is not null)
        {
            InlineTagChipsContainer.Visibility = Visibility.Collapsed;
        }

        var conditionTags = GetConditionTags();
        var propertyTags = GetPropertyTags();

        var selectedTags = new HashSet<string>(conditionTags, StringComparer.OrdinalIgnoreCase);
        selectedTags.UnionWith(propertyTags);

        var keyword = NormalizeTagText(InlineTagSearchBox.Text);

        var displayTags = new HashSet<string>(selectedTags, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            foreach (var matched in _conditionTagCatalog
                         .Concat(_propertyTagCatalog)
                         .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                         .Where(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                displayTags.Add(matched);
            }
        }

        InlineTagChipsHost.Children.Clear();
        foreach (var tag in displayTags
                     .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
                     .Take(96))
        {
            InlineTagChipsHost.Children.Add(CreateTagChipToggle(tag, selectedTags.Contains(tag)));
        }

        if (InlineTagChipsContainer is not null && InlineTagChipsHost.Children.Count > 0)
        {
            InlineTagChipsContainer.Visibility = Visibility.Visible;
        }
    }

    private ToggleButton CreateTagChipToggle(string tag, bool isSelected)
    {
        var chip = new ToggleButton
        {
            Tag = tag,
            Content = $"#{tag}",
            IsChecked = isSelected,
            Style = (Style)FindResource("InlineTagChipToggleStyle")
        };

        chip.Click += OnInlineTagChipToggle;
        return chip;
    }

    private void UpdateTagTypeToggleText()
    {
        if (InlineTagTypeToggleButton is null)
        {
            return;
        }

        InlineTagTypeToggleButton.Content = _newTagAsCondition ? "条件" : "属性";
    }

    private static string NormalizeTagText(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return string.Empty;
        }

        return rawTag.Trim().TrimStart('#');
    }

    private bool TryCommitInlineConfig()
    {
        if (Resource is null)
        {
            return false;
        }

        var privacy = InlinePrivacyCombo.SelectedItem is OptionItem<PrivacyLevel> privacyOption
            ? privacyOption.Value
            : Resource.Privacy;
        var syncPolicy = InlineSyncCombo.SelectedItem is OptionItem<SyncPolicy> syncOption
            ? syncOption.Value
            : Resource.SyncPolicy;
        var modelType = InlineModelCombo.SelectedItem is OptionItem<ModelType> modelOption
            ? modelOption.Value
            : Resource.ProcessingModel;
        var persistencePolicy = InlinePersistenceCombo.SelectedItem is OptionItem<PersistencePolicy> persistenceOption
            ? persistenceOption.Value
            : Resource.PersistencePolicy;

        var previousPrivacy = Resource.Privacy;
        var previousSyncPolicy = Resource.SyncPolicy;
        var previousProcessingModel = Resource.ProcessingModel;
        var previousPresetId = Resource.PermissionPresetId;
        var previousProcessedRouteId = Resource.ProcessedRouteId;
        var previousPersistencePolicy = Resource.PersistencePolicy;
        var currentFacet = _metadataFacetPolicy.Read(Resource);
        var metadataChanged = _metadataFacetPolicy.Apply(Resource, new ResourceMetadataFacet
        {
            Summary = currentFacet.Summary,
            ConditionTags = currentFacet.ConditionTags,
            PropertyTags = currentFacet.PropertyTags,
            TitleOverride = InlineTitleBox.Text,
            Annotations = InlineAnnotationsBox.Text,
            OriginalFileName = currentFacet.OriginalFileName,
            MimeType = currentFacet.MimeType,
            FileSize = currentFacet.FileSize,
            Source = currentFacet.Source,
            CreatedAt = currentFacet.CreatedAt,
            ExtensionMetadata = currentFacet.ExtensionMetadata
        });
        var nextPresetId = previousPresetId;
        var nextProcessedRouteId = previousProcessedRouteId;

        if (InlinePresetCombo.SelectedItem is PermissionPreset preset)
        {
            nextPresetId = preset.Id;
        }

        if (_processedRouteOptions.Count == 0)
        {
            nextProcessedRouteId = null;
        }
        else
        {
            nextProcessedRouteId = (InlineRouteCombo.SelectedItem as ProcessedRouteOption)?.RouteId;
            if (string.IsNullOrWhiteSpace(nextProcessedRouteId))
            {
                nextProcessedRouteId = _processedRouteOptions[0].RouteId;
            }
        }

        var unchanged =
            string.Equals(previousPresetId, nextPresetId, StringComparison.OrdinalIgnoreCase) &&
            previousPrivacy == privacy &&
            previousSyncPolicy == syncPolicy &&
            previousProcessingModel == modelType &&
            previousPersistencePolicy == persistencePolicy &&
            string.Equals(previousProcessedRouteId, nextProcessedRouteId, StringComparison.OrdinalIgnoreCase) &&
            !metadataChanged;

        if (unchanged)
        {
            return true;
        }

        Resource.PermissionPresetId = nextPresetId;
        Resource.Privacy = privacy;
        Resource.SyncPolicy = syncPolicy;
        Resource.ProcessingModel = modelType;
        Resource.PersistencePolicy = persistencePolicy;
        Resource.ProcessedRouteId = nextProcessedRouteId;

        ConfigChanged?.Invoke(this, new ResourceConfigChangedEventArgs
        {
            Resource = Resource,
            PreviousPrivacy = previousPrivacy,
            PreviousSyncPolicy = previousSyncPolicy,
            PreviousProcessingModel = previousProcessingModel,
            PreviousPermissionPresetId = previousPresetId,
            PreviousProcessedRouteId = previousProcessedRouteId,
            PreviousPersistencePolicy = previousPersistencePolicy
        });

        return true;
    }

    private void OnInlinePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInlineConfigInitializing)
        {
            return;
        }

        if (InlinePresetCombo.SelectedItem is not PermissionPreset preset)
        {
            return;
        }

        _isInlineConfigInitializing = true;
        try
        {
            InlinePrivacyCombo.SelectedItem = PrivacyOptions.FirstOrDefault(option => option.Value == preset.Privacy);
            InlineSyncCombo.SelectedItem = SyncOptions.FirstOrDefault(option => option.Value == preset.SyncPolicy);
            InlineModelCombo.SelectedItem = ModelOptions.FirstOrDefault(option => option.Value == preset.ProcessingModel);
        }
        finally
        {
            _isInlineConfigInitializing = false;
        }

        _ = TryCommitInlineConfig();
    }

    private void OnInlineConfigHostPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = false;
    }

    private void OnInlineConfigHostPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CloseInlineConfigEditor(commitChanges: true);
        e.Handled = true;
    }

    private void ApplyInlineCornerPresentation(bool visible)
    {
        CardChrome.CornerRadius = visible ? CardCornerWithInlineConfig : CardCornerCollapsed;
        InlineConfigHost.CornerRadius = visible ? ConfigCornerExpanded : ConfigCornerCollapsed;
    }

    private void UpdateDragGhostContent(bool isRaw)
    {
        DragGhostTitleText.Text = isRaw ? "原始文件" : "处理结果";
        DragGhostSubtitleText.Text = isRaw ? "拖拽导出" : ProcessedRouteSummary;

        if (_activeDragSurfaceBorder is not null)
        {
            var width = Math.Max(64, _activeDragSurfaceBorder.ActualWidth);
            DragGhostBorder.Width = width;
        }
    }

    private void ElevateCardZIndexForDrag()
    {
        if (_isCardZIndexElevated)
        {
            return;
        }

        _cardBaseZIndex = Panel.GetZIndex(this);
        Panel.SetZIndex(this, Math.Max(_cardBaseZIndex, 500));
        _isCardZIndexElevated = true;
    }

    private void RestoreCardZIndexAfterDrag()
    {
        if (!_isCardZIndexElevated)
        {
            return;
        }

        Panel.SetZIndex(this, _cardBaseZIndex);
        _isCardZIndexElevated = false;
    }

    private void CompleteArmedDrag()
    {
        if (!_isRawDragArmed && !_isProcessedDragArmed)
        {
            return;
        }

        ClearArmedDrag(animateBack: true);
    }

    private void UnlockMouseBeforeDragDrop()
    {
        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }

        if (_activeDragSurface is not null)
        {
            _activeDragSurface.ReleaseMouseCapture();
        }
    }

    private void FinalizeExternalExportDrag()
    {
        _activeDragSurface = null;
        _activeDragSurfaceBorder = null;
        _activeDragPlaceholder = null;
        _activeDragTransform = null;
    }

    private void StartExportDragOut()
    {
        if (!_hasDragThresholdReached || !_isPointerOutsideMainPanel || Resource is null)
        {
            return;
        }

        var exportVariant = _isRawDragArmed ? DragVariant.Raw : DragVariant.Processed;
        var resource = Resource;

        UnlockMouseBeforeDragDrop();

        var hostWindow = Window.GetWindow(this);
        GiveFeedbackEventHandler feedbackHandler = (sender, args) =>
        {
            try
            {
                var screen = Win32Helpers.GetCursorScreenPosition();
                var local = this.PointFromScreen(screen);
                _isPointerOutsideMainPanel = !IsCursorInsideMainPanel();
                UpdateDragVisual(local);
            }
            catch { }
        };

        if (hostWindow is not null)
        {
            hostWindow.GiveFeedback += feedbackHandler;
        }

        try
        {
            if (exportVariant == DragVariant.Raw)
            {
                RawDragRequested?.Invoke(this, new ResourceEventArgs { Resource = resource });
            }
            else
            {
                ProcessedDragRequested?.Invoke(this, new ResourceEventArgs { Resource = resource });
            }
        }
        finally
        {
            if (hostWindow is not null)
            {
                hostWindow.GiveFeedback -= feedbackHandler;
            }

            var droppedInside = false;
            try
            {
                droppedInside = IsCursorInsideMainPanel();
            }
            catch { }

            ResetDragVisual(animateBack: droppedInside);

            _isRawDragArmed = false;
            _isProcessedDragArmed = false;
            _hasDragThresholdReached = false;
            _isPointerOutsideMainPanel = false;
            _pointerOutsideSince = null;
            FinalizeExternalExportDrag();
        }
    }

    private sealed class OptionItem<T>
    {
        public OptionItem(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }

        public string Label { get; }
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject target)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private static bool IsInlineConfigEditorInteraction(DependencyObject? source)
    {
        return FindAncestor<ComboBox>(source) is not null ||
               FindAncestor<ComboBoxItem>(source) is not null ||
               FindAncestor<ToggleButton>(source) is not null ||
               FindAncestor<TextBox>(source) is not null ||
               FindAncestor<Button>(source) is not null ||
               FindAncestor<ScrollViewer>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static DoubleAnimation BuildOpacityAnimation(double to)
    {
        return new DoubleAnimation
        {
            To = to,
            Duration = AppInteractionDefaults.ResourceCard.OpacityAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
    }

    private static void AnimateOpacity(UIElement element, double to)
    {
        element.BeginAnimation(OpacityProperty, BuildOpacityAnimation(to), HandoffBehavior.SnapshotAndReplace);
    }

    private static string BuildWaitingText(DateTimeOffset expiresAt)
    {
        var remain = expiresAt - DateTimeOffset.UtcNow;
        if (remain <= TimeSpan.Zero)
        {
            return "等待处理 - 即将开始";
        }

        var minutes = (int)remain.TotalMinutes;
        var seconds = remain.Seconds;
        return $"等待处理 - 倒计时 {minutes:D2}:{seconds:D2}";
    }
}
