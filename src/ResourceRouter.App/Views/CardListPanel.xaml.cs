using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading.Tasks;
using ResourceRouter.App.State;
using ResourceRouter.Core.Abstractions;
using ResourceRouter.Core.Models;
using ResourceRouter.Core.Services;

namespace ResourceRouter.App.Views;

public partial class CardListPanel : UserControl
{
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly HashSet<string> _matchedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceCard> _cards = new();
    private readonly HashSet<string> _conditionTagCatalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _propertyTagCatalog = new(StringComparer.OrdinalIgnoreCase);
    private string _pendingQuery = string.Empty;
    private ResourceCard? _activeCard;
    private bool _isRefreshingConfigMode;
    private bool _hasVerticalScrollBar;
    private bool _isAnimatingCards;
    private bool _suppressSearchInputChanged;
    private Func<Resource, IReadOnlyList<ProcessedRouteOption>>? _processedRouteResolver;
    private IResourceMetadataFacetPolicy _metadataFacetPolicy = new DefaultResourceMetadataFacetPolicy();

    private static readonly Brush ActiveTagBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x6B, 0x5B));
    private static readonly Brush ActiveTagBorderBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0xD3, 0xBE));
    private static readonly Brush ActiveTagForegroundBrush = new SolidColorBrush(Color.FromRgb(0xE9, 0xF7, 0xF3));
    private static readonly Brush MatchedTagBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x26, 0x30));
    private static readonly Brush MatchedTagBorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x5B, 0x6E));
    private static readonly Brush MatchedTagForegroundBrush = new SolidColorBrush(Color.FromRgb(0xA5, 0xB4, 0xC7));
    private static readonly Brush NormalTagBackgroundBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x30, 0x3D));
    private static readonly Brush NormalTagBorderBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x60, 0x73));
    private static readonly Brush NormalTagForegroundBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC6, 0xD7));
    private static readonly Thickness SearchMarginNoScrollBar = new(6, 0, 6, 0);
    private static readonly Thickness SearchMarginWithScrollBar = new(6, 0, 10, 0);
    private static readonly Thickness TagMarginNoScrollBar = new(6, 2, 6, 8);
    private static readonly Thickness TagMarginWithScrollBar = new(6, 2, 10, 8);
    private static readonly Thickness ListMarginNoScrollBar = new(6, 0, 6, 0);
    private static readonly Thickness ListMarginWithScrollBar = new(6, 0, 10, 0);

    public CardListPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateScrollbarAwareLayout(animate: false);

        _searchDebounceTimer = new DispatcherTimer { Interval = AppInteractionDefaults.CardListPanel.SearchDebounce };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            SearchRequested?.Invoke(this, _pendingQuery);
        };
    }

    public event EventHandler<string>? SearchRequested;
    public event EventHandler<ResourceEventArgs>? RawDragRequested;
    public event EventHandler<ResourceEventArgs>? ProcessedDragRequested;
    public event EventHandler<ResourceEventArgs>? CollectionDeleteRequested;
    public event EventHandler<ResourceConfigChangedEventArgs>? ResourceConfigChanged;
    public event EventHandler? InlineInputChanged;
    public event EventHandler<TagToggleEventArgs>? TagToggleRequested;

    public bool HasVisibleTags => TagChipsContainer.Visibility == Visibility.Visible;

    public void SetTagCatalog(IReadOnlyCollection<string> conditionTags, IReadOnlyCollection<string> propertyTags)
    {
        _conditionTagCatalog.Clear();
        foreach (var tag in conditionTags)
        {
            var normalized = NormalizeTag(tag);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _conditionTagCatalog.Add(normalized);
            }
        }

        _propertyTagCatalog.Clear();
        foreach (var tag in propertyTags)
        {
            var normalized = NormalizeTag(tag);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _propertyTagCatalog.Add(normalized);
            }
        }

        foreach (var card in _cards)
        {
            card.SetTagCatalog(_conditionTagCatalog, _propertyTagCatalog);
        }
    }

    public void SetProcessedRouteResolver(Func<Resource, IReadOnlyList<ProcessedRouteOption>> resolver)
    {
        _processedRouteResolver = resolver;
    }

    public void SetMetadataFacetPolicy(IResourceMetadataFacetPolicy metadataFacetPolicy)
    {
        _metadataFacetPolicy = metadataFacetPolicy ?? throw new ArgumentNullException(nameof(metadataFacetPolicy));

        foreach (var card in _cards)
        {
            card.SetMetadataFacetPolicy(_metadataFacetPolicy);
        }
    }

    public void FocusSearchBox(bool selectAll = false)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!SearchBox.IsEnabled || SearchBox.Visibility != Visibility.Visible)
            {
                return;
            }

            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            if (selectAll)
            {
                SearchBox.SelectAll();
            }
            else
            {
                SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
            }
        }), DispatcherPriority.Input);
    }

    public double GetDesiredPopupHeight()
    {
        UpdateLayout();

        var searchHeight = Math.Max(28, SearchContainer.ActualHeight) + 8;
        var tagHeight = TagChipsContainer.Visibility == Visibility.Visible
            ? Math.Max(0, TagChipsContainer.ActualHeight) + 8
            : 0;

        var cardsHeight = 0d;
        foreach (var card in _cards)
        {
            if (card.Visibility != Visibility.Visible)
            {
                continue;
            }

            var height = card.ActualHeight;
            if (height <= 1)
            {
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                height = Math.Max(card.DesiredSize.Height, card.MinHeight);
            }

            cardsHeight += height;
        }

        if (cardsHeight <= 1)
        {
            cardsHeight = AppInteractionDefaults.CardListPanel.EmptyCardsFallbackHeight;
        }

        return AppInteractionDefaults.CardListPanel.PanelOuterMargin
               + searchHeight
               + tagHeight
               + cardsHeight
               + AppInteractionDefaults.CardListPanel.ScrollFrame;
    }

    public void SetResources(IEnumerable<Resource> resources)
    {
        if (!_isAnimatingCards)
        {
            _ = PlayCardsTransitionAsync();
        }

        var existingCards = _cards.ToArray();
        foreach (var existing in existingCards)
        {
            existing.CloseInlineConfigEditor(commitChanges: true);
            existing.DeactivateInteractions();
        }

        CardsHost.Children.Clear();
        _cards.Clear();
        _activeCard = null;

        foreach (var resource in resources)
        {
            var card = new ResourceCard
            {
                Resource = resource,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (_processedRouteResolver is not null)
            {
                card.SetProcessedRouteOptions(_processedRouteResolver(resource));
            }
            card.SetTagCatalog(_conditionTagCatalog, _propertyTagCatalog);
            card.SetMetadataFacetPolicy(_metadataFacetPolicy);

            card.RawDragRequested += (_, args) => RawDragRequested?.Invoke(this, args);
            card.ProcessedDragRequested += (_, args) => ProcessedDragRequested?.Invoke(this, args);
            card.CollectionDeleteRequested += (_, args) => CollectionDeleteRequested?.Invoke(this, args);
            card.ConfigChanged += (_, args) => ResourceConfigChanged?.Invoke(this, args);
            card.TextChanged += (_, _) => InlineInputChanged?.Invoke(this, EventArgs.Empty);
            card.ConfigModeChanged += (_, _) => RefreshConfigModeLock();
            card.InteractionActivated += (_, _) => ActivateCard(card);
            CardsHost.Children.Add(card);
            _cards.Add(card);
        }

        RefreshConfigModeLock();
        Dispatcher.BeginInvoke(
            new Action(() => UpdateScrollbarAwareLayout(animate: true)),
            DispatcherPriority.Background);
    }

    public void CollapseCardInteractions()
    {
        DeactivateAllCards();
    }

    public void ResetPanelState()
    {
        _searchDebounceTimer.Stop();
        _pendingQuery = string.Empty;
        _activeTags.Clear();
        _matchedTags.Clear();

        _suppressSearchInputChanged = true;
        try
        {
            SearchBox.Text = string.Empty;
        }
        finally
        {
            _suppressSearchInputChanged = false;
        }

        TagChipsHost.Children.Clear();
        TagChipsContainer.Visibility = Visibility.Collapsed;

        var existingCards = _cards.ToArray();
        foreach (var existing in existingCards)
        {
            existing.CloseInlineConfigEditor(commitChanges: true);
            existing.DeactivateInteractions();
        }

        CardsHost.Children.Clear();
        _cards.Clear();
        _activeCard = null;
        UpdateScrollbarAwareLayout(animate: false);
    }

    private void OnRootPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var clickedCard = FindAncestor<ResourceCard>(source);
        var openInlineCard = _cards.FirstOrDefault(static card => card.IsInlineConfigOpen);

        if (openInlineCard is not null && openInlineCard.HasInlineDropDownOpen)
        {
            ActivateCard(openInlineCard);
            return;
        }

        if (openInlineCard is not null && IsInlineEditorInteraction(source))
        {
            ActivateCard(openInlineCard);
            return;
        }

        if (openInlineCard is not null && clickedCard is null)
        {
            ActivateCard(openInlineCard);
            return;
        }

        if (openInlineCard is not null && !ReferenceEquals(openInlineCard, clickedCard))
        {
            var committed = openInlineCard.CloseInlineConfigEditor(commitChanges: true);
            if (!committed)
            {
                e.Handled = true;
                return;
            }
        }

        if (clickedCard is null)
        {
            DeactivateAllCards();
            return;
        }

        ActivateCard(clickedCard);
    }

    private void ActivateCard(ResourceCard card)
    {
        if (!ReferenceEquals(_activeCard, card))
        {
            foreach (var existing in _cards)
            {
                if (!ReferenceEquals(existing, card))
                {
                    existing.DeactivateInteractions();
                }
            }
        }

        _activeCard = card;
    }

    private void DeactivateAllCards()
    {
        var cardsSnapshot = _cards.ToArray();
        foreach (var card in cardsSnapshot)
        {
            card.CloseInlineConfigEditor(commitChanges: true);
            card.DeactivateInteractions();
        }

        _activeCard = null;
    }

    private void RefreshConfigModeLock()
    {
        if (_isRefreshingConfigMode)
        {
            return;
        }

        _isRefreshingConfigMode = true;
        try
        {
            var cardsSnapshot = _cards.ToArray();
            var activeConfigCard = cardsSnapshot.FirstOrDefault(static card => card.IsInlineConfigOpen);

            foreach (var card in cardsSnapshot)
            {
                card.SetDragInteractionsLocked(activeConfigCard is not null && !ReferenceEquals(card, activeConfigCard));

                if (activeConfigCard is not null &&
                    !ReferenceEquals(card, activeConfigCard) &&
                    card.IsInlineConfigOpen)
                {
                    card.CloseInlineConfigEditor(commitChanges: true);
                }
            }
        }
        finally
        {
            _isRefreshingConfigMode = false;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static bool IsInlineEditorInteraction(DependencyObject? source)
    {
        return FindAncestor<ComboBox>(source) is not null ||
               FindAncestor<ComboBoxItem>(source) is not null ||
               FindAncestor<ToggleButton>(source) is not null ||
               FindAncestor<TextBox>(source) is not null ||
               FindAncestor<Button>(source) is not null ||
               FindAncestor<ScrollViewer>(source) is not null;
    }

    public void SetTagChips(
        IReadOnlyCollection<string> allTags,
        IReadOnlyCollection<string> activeTags,
        IReadOnlyCollection<string> matchedTags)
    {
        _activeTags.Clear();
        foreach (var tag in activeTags)
        {
            _activeTags.Add(NormalizeTag(tag));
        }

        _matchedTags.Clear();
        foreach (var tag in matchedTags)
        {
            _matchedTags.Add(NormalizeTag(tag));
        }

        TagChipsHost.Children.Clear();
        foreach (var tag in allTags
                     .Select(NormalizeTag)
                     .Where(static t => !string.IsNullOrWhiteSpace(t))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
                     .Take(96))
        {
            var chip = new ToggleButton
            {
                Content = $"#{tag}",
                Tag = tag,
                IsChecked = _activeTags.Contains(tag),
                Style = (Style)FindResource("TagChipToggleStyle")
            };

            ApplyTagVisual(chip, chip.IsChecked == true, _matchedTags.Contains(tag));
            chip.Click += OnTagChipClick;
            TagChipsHost.Children.Add(chip);
        }

        TagChipsContainer.Visibility = TagChipsHost.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        Dispatcher.BeginInvoke(
            new Action(() => UpdateScrollbarAwareLayout(animate: true)),
            DispatcherPriority.Background);
    }

    private void OnCardsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollbarAwareLayout(animate: true);
    }

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearchInputChanged)
        {
            return;
        }

        _pendingQuery = SearchBox.Text;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnTagChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton chip || chip.Tag is not string rawTag)
        {
            return;
        }

        var tag = NormalizeTag(rawTag);
        var isSelected = chip.IsChecked == true;

        if (isSelected)
        {
            _activeTags.Add(tag);
        }
        else
        {
            _activeTags.Remove(tag);
        }

        ApplyTagVisual(chip, isSelected, _matchedTags.Contains(tag));
        TagToggleRequested?.Invoke(this, new TagToggleEventArgs { Tag = tag, IsSelected = isSelected });
    }

    private static string NormalizeTag(string? tag)
    {
        return ResourceTagRules.Normalize(tag) ?? string.Empty;
    }

    private static void ApplyTagVisual(ToggleButton chip, bool isActive, bool isMatched)
    {
        if (isActive)
        {
            chip.Background = ActiveTagBackgroundBrush;
            chip.BorderBrush = ActiveTagBorderBrush;
            chip.Foreground = ActiveTagForegroundBrush;
            return;
        }

        if (isMatched)
        {
            chip.Background = MatchedTagBackgroundBrush;
            chip.BorderBrush = MatchedTagBorderBrush;
            chip.Foreground = MatchedTagForegroundBrush;
            return;
        }

        chip.Background = NormalTagBackgroundBrush;
        chip.BorderBrush = NormalTagBorderBrush;
        chip.Foreground = NormalTagForegroundBrush;
    }

    private void UpdateScrollbarAwareLayout(bool animate)
    {
        var hasScrollBar = CardsScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
        if (hasScrollBar == _hasVerticalScrollBar && animate)
        {
            return;
        }

        _hasVerticalScrollBar = hasScrollBar;

        var searchTarget = hasScrollBar ? SearchMarginWithScrollBar : SearchMarginNoScrollBar;
        var tagTarget = hasScrollBar ? TagMarginWithScrollBar : TagMarginNoScrollBar;
        var listTarget = hasScrollBar ? ListMarginWithScrollBar : ListMarginNoScrollBar;

        if (!animate)
        {
            SearchContainer.Margin = searchTarget;
            TagChipsContainer.Margin = tagTarget;
            CardsScrollViewer.Margin = listTarget;
            return;
        }

        AnimateThickness(SearchContainer, MarginProperty, searchTarget);
        AnimateThickness(TagChipsContainer, MarginProperty, tagTarget);
        AnimateThickness(CardsScrollViewer, MarginProperty, listTarget);
    }

    private static void AnimateThickness(FrameworkElement element, DependencyProperty property, Thickness to)
    {
        element.BeginAnimation(property, new ThicknessAnimation
        {
            To = to,
            Duration = AppInteractionDefaults.CardListPanel.MarginAnimationDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private async Task PlayCardsTransitionAsync()
    {
        _isAnimatingCards = true;
        try
        {
            CardsScrollViewer.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = AppInteractionDefaults.CardListPanel.TransitionFadeDownOpacity,
                Duration = AppInteractionDefaults.CardListPanel.TransitionFadeDownDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);

            await Task.Delay(AppInteractionDefaults.CardListPanel.TransitionFadeDelayMs);

            CardsScrollViewer.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = 1,
                Duration = AppInteractionDefaults.CardListPanel.TransitionFadeUpDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        }
        finally
        {
            _isAnimatingCards = false;
        }
    }
}