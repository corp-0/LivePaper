using LivePaper.Renderer;

try
{
    return RendererApplication.Run(args);
}
catch (DllNotFoundException exception)
{
    Console.Error.WriteLine($"Missing native dependency: {exception.Message}");
    return 1;
}
catch (EntryPointNotFoundException exception)
{
    Console.Error.WriteLine($"Unsupported native library version: {exception.Message}");
    return 1;
}
catch (Exception exception) when (
    exception is ArgumentException or
        IOException or
        InvalidOperationException or
        UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Cannot load wallpaper: {exception.Message}");
    return 1;
}
