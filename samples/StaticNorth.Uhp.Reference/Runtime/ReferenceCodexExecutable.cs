namespace StaticNorth.Uhp.Reference;

internal static class ReferenceCodexExecutable
{
    public static string ExecutablePath { get; } = FindExecutable();

    private static string FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var file = new FileInfo(Path.Combine(directory, OperatingSystem.IsWindows() ? "codex.exe" : "codex"));

            if (file.Exists)
            {
                return file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
            }
        }

        throw new FileNotFoundException("Codex is not installed on PATH.");
    }
}
