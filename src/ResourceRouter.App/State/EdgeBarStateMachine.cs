using System;

namespace ResourceRouter.App.State;

public enum EdgeBarVisualState
{
    Hidden,
    Peeking,
    Visible,
    Dragging,
    DropPanel,
    CardList
}

public sealed class EdgeBarStateMachine
{
    private bool _isVisibilityPinned;

    public EdgeBarVisualState CurrentState { get; private set; } = EdgeBarVisualState.Hidden;

    public double PinnedMinimumOpacity { get; set; } = AppInteractionDefaults.ProximityFade.PinnedMinimumOpacity;

    public event Action<double>? SetOpacityRequested;
    public event Action? ExpandDropPanelRequested;
    public event Action? ShowCardListRequested;
    public event Action? CollapseAllRequested;

    public void PinVisibility()
    {
        _isVisibilityPinned = true;
    }

    public void UnpinVisibility()
    {
        _isVisibilityPinned = false;
    }

    public void MouseProximityChanged(double opacity)
    {
        if (_isVisibilityPinned)
        {
            opacity = Math.Max(opacity, PinnedMinimumOpacity);
        }

        if (opacity <= 0)
        {
            if (!_isVisibilityPinned && (CurrentState is EdgeBarVisualState.Peeking or EdgeBarVisualState.Visible))
            {
                CurrentState = EdgeBarVisualState.Hidden;
                SetOpacityRequested?.Invoke(0);
            }

            return;
        }

        if (CurrentState is EdgeBarVisualState.Hidden or EdgeBarVisualState.Peeking)
        {
            CurrentState = EdgeBarVisualState.Peeking;
            SetOpacityRequested?.Invoke(opacity);
        }
        else if (_isVisibilityPinned)
        {
            SetOpacityRequested?.Invoke(opacity);
        }
    }

    public void Click()
    {
        CurrentState = EdgeBarVisualState.CardList;
        ShowCardListRequested?.Invoke();
    }

    public void BeginDragMove()
    {
        CurrentState = EdgeBarVisualState.Dragging;
    }

    public void EndDragMove()
    {
        CurrentState = EdgeBarVisualState.Visible;
    }

    public void DragEnter()
    {
        CurrentState = EdgeBarVisualState.DropPanel;
        ExpandDropPanelRequested?.Invoke();
    }

    public void DragLeave()
    {
        if (CurrentState == EdgeBarVisualState.DropPanel)
        {
            CurrentState = EdgeBarVisualState.Peeking;
            CollapseAllRequested?.Invoke();
        }
    }

    public void DropCompleted()
    {
        CurrentState = EdgeBarVisualState.Hidden;
        CollapseAllRequested?.Invoke();
        SetOpacityRequested?.Invoke(0);
    }

    public void Collapse()
    {
        CurrentState = EdgeBarVisualState.Hidden;
        CollapseAllRequested?.Invoke();
        SetOpacityRequested?.Invoke(0);
    }
}