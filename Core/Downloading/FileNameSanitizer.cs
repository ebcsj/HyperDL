using System.IO;

namespace HyperDL.Core;

public static class FileNameSanitizer
{
    private const int MaximumFileNameLength = 180;
    private const string FallbackFileName = "download";

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FallbackFileName;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeFileName = new string(fileName
            .Select(character =>
                character is '/' or '\\' ||
                char.IsControl(character) ||
                invalidCharacters.Contains(character)
                    ? '_'
                    : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');

        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName is "." or "..")
        {
            return FallbackFileName;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeFileName);
        if (IsReservedDeviceName(nameWithoutExtension))
        {
            safeFileName = $"_{safeFileName}";
        }

        if (safeFileName.Length > MaximumFileNameLength)
        {
            var extension = Path.GetExtension(safeFileName);
            if (extension.Length >= MaximumFileNameLength)
            {
                extension = string.Empty;
            }

            var nameLength = MaximumFileNameLength - extension.Length;
            safeFileName = safeFileName[..nameLength] + extension;
        }

        return safeFileName;
    }

    private static bool IsReservedDeviceName(string name) =>
        name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
        (name.Length == 4 &&
         (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
          name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
         name[3] is >= '1' and <= '9');
}
