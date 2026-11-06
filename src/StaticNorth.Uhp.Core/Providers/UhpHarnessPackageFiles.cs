using System.Text;
using StaticNorth.Uhp.Core.Contracts;

namespace StaticNorth.Uhp.Core.Providers;

public static class UhpHarnessPackageFiles
{
    public static void ValidateFiles(SkillFile[]? files, string code)
    {
        if (files is null)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];

            if (string.IsNullOrWhiteSpace(file.Path) || Path.IsPathRooted(file.Path) || file.Path.Contains('\\') || file.Path
                .Split('/')
                .Any(part => part is "" or "." or "..") || !names.Add(file.Path) || (file.Content is null) == (file.ContentB64 is null))
            {
                throw UhpRequestException.InvalidRequest(422, code, "Invalid or duplicate package member.");
            }

            try
            {
                _ = Bytes(file);
            }
            catch (FormatException)
            {
                throw UhpRequestException.InvalidRequest(422, code, "Invalid base64 package member.");
            }
        }
    }

    public static byte[] Bytes(SkillFile file) => file.Content is { } text ? Encoding.UTF8.GetBytes(text) : Convert.FromBase64String(file.ContentB64!);
}
