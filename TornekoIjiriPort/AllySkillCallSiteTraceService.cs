namespace TornekoIjiriPort;

internal static class AllySkillCallSiteTraceService
{
    internal static string Install() => AppSideTraceService.Start();
    internal static string Read() => AppSideTraceService.Read();
    internal static string Restore() => AppSideTraceService.Stop();
}
