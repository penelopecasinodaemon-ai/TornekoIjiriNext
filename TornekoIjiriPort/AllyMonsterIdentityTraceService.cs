namespace TornekoIjiriPort;

internal static class AllyMonsterIdentityTraceService
{
    internal static string Install() => AppSideTraceService.Start();
    internal static string Read() => AppSideTraceService.Read();
    internal static string Restore() => AppSideTraceService.Stop();
    internal static string DumpKnownStructures() => AppSideTraceService.CaptureNow();
}
