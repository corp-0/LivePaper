using LivePaper.Protocol;
using Tmds.DBus.Protocol;

namespace LivePaper.Platform.KWin;

public class VisibilityMethodHandler(TimeSpan pollInterval) : IPathMethodHandler
{
    private const string Interface = "io.github.livepaper.Visibility";

    public event EventHandler<string>? Changed;

    public event Action<PointerPositionChanged>? PointerPositionChanged;

    public string Path => "/Visibility";

    public bool HandlesChildPaths => false;

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([InterfaceXml]);
            return;
        }

        var request = context.Request;
        if (request.InterfaceAsString == Interface &&
            request is { MemberAsString: "SetPointerPosition", SignatureAsString: "ii" })
        {
            var reader = request.GetBodyReader();
            PointerPositionChanged?.Invoke(new(reader.ReadInt32(), reader.ReadInt32()));
            Reply(context);
            return;
        }

        if (request.InterfaceAsString == Interface &&
            request.MemberAsString == "SetVisibilityState" &&
            request.SignatureAsString == "s")
        {
            Changed?.Invoke(this, request.GetBodyReader().ReadString());
            Reply(context);
            return;
        }

        if (request.InterfaceAsString == Interface &&
            request.MemberAsString == "NextPoll" &&
            request.SignatureAsString == "")
        {
            await Task.Delay(pollInterval);
            Reply(context);
            return;
        }

        context.ReplyUnknownMethodError();
    }

    private static void Reply(MethodContext context)
    {
        using var writer = context.CreateReplyWriter("");
        context.Reply(writer.CreateMessage());
    }

    private static ReadOnlyMemory<byte> InterfaceXml { get; } =
        """
        <interface name="io.github.livepaper.Visibility">
          <method name="SetVisibilityState">
            <arg direction="in" type="s"/>
          </method>
          <method name="NextPoll"/>
          <method name="SetPointerPosition">
            <arg direction="in" type="i"/>
            <arg direction="in" type="i"/>
          </method>
        </interface>
        """u8.ToArray();
}
