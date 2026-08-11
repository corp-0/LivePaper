using Xunit;

namespace LivePaper.Daemon.Tests;

public class LivePaperConfigTests
{
    [Fact]
    public void DirectWpeIsOptIn()
    {
        Assert.False(new LivePaperConfig().ForceDirectWpe);
        Assert.True(LivePaperConfig.Load("force_direct_wpe = true").ForceDirectWpe);
    }
}
