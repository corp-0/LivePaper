using LivePaper.Protocol;

namespace LivePaper.Platform;

public interface IPointerPositionSource
{
    event EventHandler<PointerPositionChanged>? Changed;

    PointerPositionChanged? Current { get; }
}
