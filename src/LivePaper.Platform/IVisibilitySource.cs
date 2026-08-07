using LivePaper.Protocol;

namespace LivePaper.Platform;

public interface IVisibilitySource
{
    event EventHandler<VisibilityChanged>? Changed;

    VisibilityChanged Current { get; }
}
