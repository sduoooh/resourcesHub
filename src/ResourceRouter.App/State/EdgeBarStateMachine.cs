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
    public EdgeBarVisualState CurrentState { get; private set; } = EdgeBarVisualState.Hidden;

    public event Action<double>? SetOpacityRequested;
    public event Action? ExpandDropPanelRequested;
    public event Action? ShowCardListRequested;
    public event Action? CollapseAllRequested;

    public void MouseProximityChanged(double opacity)
    {
        if (opacity <= 0)
        {
            if (CurrentState is EdgeBarVisualState.Peeking or EdgeBarVisualState.Visible)
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