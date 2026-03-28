using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace ResourceRouter.App.Views;

public partial class DropPanel : UserControl
{
    private enum ZoneFocus
    {
        None,
        Public,
        Private
    }

    private bool _hasPendingData;
    private int _pendingDropCount;
    private ZoneFocus _currentFocus = ZoneFocus.None;

    public DropPanel()
    {
        InitializeComponent();
        UpdateHint(ZoneFocus.None);
    }

    public event EventHandler<DropDataEventArgs>? PublicDrop;
    public event EventHandler<DropDataEventArgs>? PrivateDrop;
    public event EventHandler? PublicClickRequested;
    public event EventHandler? PrivateClickRequested;
    public event EventHandler? CollapseRequested;

    public void SetPendingState(bool hasPendingData, int pendingDropCount = 0)
    {
        _hasPendingData = hasPendingData;
        _pendingDropCount = Math.Max(0, pendingDropCount);
        if (!hasPendingData)
        {
            SetZoneFocus(ZoneFocus.None, animated: false);
        }
        else
        {
            UpdateHint(_currentFocus);
        }
    }

    private void OnPanelDragOver(object sender, DragEventArgs e)
    {
        var zone = ResolveZoneFromPoint(e.GetPosition(ZoneGrid));
        SetZoneFocus(zone);

        e.Effects = zone == ZoneFocus.None ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPanelDragLeave(object sender, DragEventArgs e)
    {
        SetZoneFocus(ZoneFocus.None);
        if (!_hasPendingData)
        {
            CollapseRequested?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    private void OnPublicDragOver(object sender, DragEventArgs e)
    {
        SetZoneFocus(ZoneFocus.Public);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPrivateDragOver(object sender, DragEventArgs e)
    {
        SetZoneFocus(ZoneFocus.Private);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnPublicDrop(object sender, DragEventArgs e)
    {
        SetPendingState(false);
        SetZoneFocus(ZoneFocus.None, animated: false);
        PublicDrop?.Invoke(this, new DropDataEventArgs { DataObject = e.Data });
        e.Handled = true;
    }

    private void OnPrivateDrop(object sender, DragEventArgs e)
    {
        SetPendingState(false);
        SetZoneFocus(ZoneFocus.None, animated: false);
        PrivateDrop?.Invoke(this, new DropDataEventArgs { DataObject = e.Data });
        e.Handled = true;
    }

    private void OnPublicClick(object sender, RoutedEventArgs e)
    {
        PublicClickRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPrivateClick(object sender, RoutedEventArgs e)
    {
        PrivateClickRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPublicMouseEnter(object sender, MouseEventArgs e)
    {
        SetZoneFocus(ZoneFocus.Public);
    }

    private void OnPrivateMouseEnter(object sender, MouseEventArgs e)
    {
        SetZoneFocus(ZoneFocus.Private);
    }

    private void OnZoneMouseLeave(object sender, MouseEventArgs e)
    {
        SetZoneFocus(ZoneFocus.None);
    }

    private ZoneFocus ResolveZoneFromPoint(Point point)
    {
        if (IsInsideElement(PublicZoneButton, point))
        {
            return ZoneFocus.Public;
        }

        if (IsInsideElement(PrivateZoneButton, point))
        {
            return ZoneFocus.Private;
        }

        return ZoneFocus.None;
    }

    private bool IsInsideElement(FrameworkElement element, Point pointInZoneGrid)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        var topLeft = element.TranslatePoint(new Point(0, 0), ZoneGrid);
        var rect = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
        return rect.Contains(pointInZoneGrid);
    }

    private void SetZoneFocus(ZoneFocus focus, bool animated = true)
    {
        _currentFocus = focus;

        var publicOpacity = focus == ZoneFocus.Public ? 0.92 : 0.16;
        var privateOpacity = focus == ZoneFocus.Private ? 0.92 : 0.16;
        var publicScale = focus == ZoneFocus.Public ? 1.04 : 1.0;
        var privateScale = focus == ZoneFocus.Private ? 1.04 : 1.0;

        AnimateDouble(PublicFocusHalo, UIElement.OpacityProperty, publicOpacity, animated);
        AnimateDouble(PrivateFocusHalo, UIElement.OpacityProperty, privateOpacity, animated);
        AnimateDouble(PublicButtonScale, ScaleTransform.ScaleXProperty, publicScale, animated);
        AnimateDouble(PublicButtonScale, ScaleTransform.ScaleYProperty, publicScale, animated);
        AnimateDouble(PrivateButtonScale, ScaleTransform.ScaleXProperty, privateScale, animated);
        AnimateDouble(PrivateButtonScale, ScaleTransform.ScaleYProperty, privateScale, animated);

        UpdateHint(focus);
    }

    private static void AnimateDouble(DependencyObject target, DependencyProperty property, double to, bool animated)
    {
        var duration = animated ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero;
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = animated ? new QuadraticEase { EasingMode = EasingMode.EaseOut } : null
        };

        if (target is UIElement element)
        {
            element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        if (target is ScaleTransform transform)
        {
            transform.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void UpdateHint(ZoneFocus focus)
    {
        var publicHint = "公开";
        var privateHint = "隐私";

        if (!_hasPendingData)
        {
            PendingHintText.Text = focus switch
            {
                ZoneFocus.Public => $"拖拽到 {publicHint}，将使用云端处理与默认同步",
                ZoneFocus.Private => $"拖拽到 {privateHint}，仅本地处理且不同步",
                _ => "拖入后可点击公开/隐私确认处理"
            };
            return;
        }

        var countHint = _pendingDropCount > 0 ? $"（已暂存 {_pendingDropCount} 项）" : "（已暂存）";

        PendingHintText.Text = focus switch
        {
            ZoneFocus.Public => $"点击公开，使用云端处理与默认同步 {countHint}",
            ZoneFocus.Private => $"点击隐私，仅本地处理且不同步 {countHint}",
            _ => $"已暂存拖入内容，请点击公开或隐私确认处理 {countHint}"
        };
    }
}