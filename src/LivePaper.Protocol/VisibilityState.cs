namespace LivePaper.Protocol;

public enum VisibilityState
{
    Visible,
    PartiallyCovered,
    FullyCovered,
    OutputDisabled,
    SessionLocked
}

public record VisibilityChanged(VisibilityState State, bool ShouldRender, bool ShouldMute);
