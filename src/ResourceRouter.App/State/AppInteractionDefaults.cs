using System;

namespace ResourceRouter.App.State;

public static class AppInteractionDefaults
{
    public static class EdgeBar
    {
        public const double SensorStripWidthDip = 10;
        public const double CollapsedHostWidthDip = SensorStripWidthDip;
        public const double ExpandedHostWidthDip = 24;
        public const double CollapsedSensorOpacity = 0.86;
        public const double ExpandedSensorOpacity = 1.0;

        public const int CardListLimit = 200;
        public const double DragStartThresholdDip = 6;
        public const double CursorInsideInflateDip = 56;

        public const double WindowHeightDip = 200;

        public const double CardListPopupWidthDip = 430;
        public const double CardListPopupHeightDip = 320;
        public const double CardListPopupVisualMinHeightDip = 220;
        public const double CardListPopupContentMinHeightDip = 150;
        public const double CardListPopupMaxHeightDip = 640;

        public const int DragLeaveCollapseDelayMs = 800;
        public const int DropIngressRecoveryDelayMs = 300;
        public const int FadeAnimationDurationMs = 140;
        public const int RevealAnimationDurationMs = 120;
        public const int PopupOpenAnimationDurationMs = 150;

        public static readonly TimeSpan DragLeaveCollapseDelay = TimeSpan.FromMilliseconds(DragLeaveCollapseDelayMs);
        public static readonly TimeSpan DropIngressRecoveryDelay = TimeSpan.FromMilliseconds(DropIngressRecoveryDelayMs);
        public static readonly TimeSpan FadeAnimationDuration = TimeSpan.FromMilliseconds(FadeAnimationDurationMs);
        public static readonly TimeSpan RevealAnimationDuration = TimeSpan.FromMilliseconds(RevealAnimationDurationMs);
        public static readonly TimeSpan PopupOpenAnimationDuration = TimeSpan.FromMilliseconds(PopupOpenAnimationDurationMs);

        public const double PopupOpenScaleFrom = 0.96;
    }

    public static class ProximityFade
    {
        public const int TickIntervalMs = 50;
        public const double ActivationDistanceDip = 100;
        public const double MaxOpacity = 0.6;
        public const double PinnedMinimumOpacity = 0.35;

        public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(TickIntervalMs);
    }

    public static class DampedDrag
    {
        public const double Stiffness = 0.2;
        public const double Damping = 0.75;
    }

    public static class DropIngress
    {
        public const int SuppressDuringDragOutMs = 1800;
        public const int SuppressAfterDragOutMs = 700;
        public const int DedupWindowMs = 1200;
        public const int WpfDelayWhenComEnabledMs = 140;

        public static readonly TimeSpan SuppressDuringDragOut = TimeSpan.FromMilliseconds(SuppressDuringDragOutMs);
        public static readonly TimeSpan SuppressAfterDragOut = TimeSpan.FromMilliseconds(SuppressAfterDragOutMs);
        public static readonly TimeSpan DedupWindow = TimeSpan.FromMilliseconds(DedupWindowMs);
        public static readonly TimeSpan WpfDelayWhenComEnabled = TimeSpan.FromMilliseconds(WpfDelayWhenComEnabledMs);
    }

    public static class DropPanel
    {
        public const double FocusOpacity = 0.92;
        public const double IdleOpacity = 0.16;
        public const double FocusScale = 1.04;
        public const double IdleScale = 1.0;
        public const int FocusAnimationDurationMs = 120;

        public static readonly TimeSpan FocusAnimationDuration = TimeSpan.FromMilliseconds(FocusAnimationDurationMs);
    }

    public static class CardListPanel
    {
        public const int SearchDebounceMs = 200;
        public const double EmptyCardsFallbackHeight = 36;
        public const double PanelOuterMargin = 16;
        public const double ScrollFrame = 2;

        public const int MarginAnimationDurationMs = 140;
        public const int TransitionFadeDownDurationMs = 90;
        public const int TransitionFadeUpDurationMs = 140;
        public const int TransitionFadeDelayMs = 40;

        public const double TransitionFadeDownOpacity = 0.86;

        public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(SearchDebounceMs);
        public static readonly TimeSpan MarginAnimationDuration = TimeSpan.FromMilliseconds(MarginAnimationDurationMs);
        public static readonly TimeSpan TransitionFadeDownDuration = TimeSpan.FromMilliseconds(TransitionFadeDownDurationMs);
        public static readonly TimeSpan TransitionFadeUpDuration = TimeSpan.FromMilliseconds(TransitionFadeUpDurationMs);
    }

    public static class ResourceCard
    {
        public const double DragExportTriggerPadding = 56;
        public const double DeleteRevealMaxWidth = 106;
        public const double DeleteRevealCommitWidth = 56;
        public const double DeleteSwipeHandleWidth = 92;
        public const double DeleteSwipeMoveThreshold = 6;

        public const int OutsideMainPanelTriggerMs = 90;
        public const int DragResetAnimationDurationMs = 140;
        public const int DeleteRevealAnimationDurationMs = 130;
        public const int OpacityAnimationDurationMs = 120;

        public static readonly TimeSpan OutsideMainPanelTrigger = TimeSpan.FromMilliseconds(OutsideMainPanelTriggerMs);
        public static readonly TimeSpan DragResetAnimationDuration = TimeSpan.FromMilliseconds(DragResetAnimationDurationMs);
        public static readonly TimeSpan DeleteRevealAnimationDuration = TimeSpan.FromMilliseconds(DeleteRevealAnimationDurationMs);
        public static readonly TimeSpan OpacityAnimationDuration = TimeSpan.FromMilliseconds(OpacityAnimationDurationMs);
    }
}