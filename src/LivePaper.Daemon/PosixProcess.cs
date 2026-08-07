using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LivePaper.Daemon;

public static partial class PosixProcess
{
    private const int Sigterm = 15;

    public static void SendTerminate(int processId)
    {
        if (Kill(processId, Sigterm) != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int processId, int signal);
}
