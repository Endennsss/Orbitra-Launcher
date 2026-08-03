using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SS14.Launcher;

/// <summary>
/// Performs format and resource-limit checks before a theme archive is uploaded or applied.
/// Theme archives are never extracted directly to disk.
/// </summary>
public static class ThemeArchiveValidator
{
    public const int MaxArchiveBytes = 20 * 1024 * 1024;
    public const long MaxUncompressedBytes = 40L * 1024 * 1024;
    public const long MaxImageBytes = 25L * 1024 * 1024;
    private const int MaxEntries = 8;
    private const int MaxCompressionRatio = 200;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    public static void Validate(byte[] data)
    {
        if (data.Length == 0) throw Invalid("Архив темы пуст.");
        if (data.Length > MaxArchiveBytes) throw Invalid("Архив темы превышает лимит 20 МБ.");

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is 0 or > MaxEntries)
                throw Invalid($"В архиве допустимо от 1 до {MaxEntries} файлов.");

            long total = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (string.IsNullOrWhiteSpace(name) || name.EndsWith('/') || name.EndsWith('\\'))
                    throw Invalid("Папки внутри темы не поддерживаются.");
                if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) ||
                    name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
                    throw Invalid("Архив содержит небезопасный путь.");
                if (!names.Add(name)) throw Invalid("Архив содержит файлы с одинаковыми именами.");

                var isManifest = string.Equals(name, "theme.json", StringComparison.OrdinalIgnoreCase);
                var extension = Path.GetExtension(name);
                if (!isManifest && !ImageExtensions.Contains(extension))
                    throw Invalid($"Недопустимый файл в теме: {name}.");
                if (isManifest && entry.Length > 64 * 1024)
                    throw Invalid("Описание темы превышает 64 КБ.");
                if (!isManifest && entry.Length > MaxImageBytes)
                    throw Invalid($"Изображение {name} превышает 25 МБ.");

                total = checked(total + entry.Length);
                if (total > MaxUncompressedBytes)
                    throw Invalid("Распакованный размер темы превышает 40 МБ.");
                if (entry.Length > 1024 * 1024 &&
                    (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > MaxCompressionRatio))
                    throw Invalid("Архив похож на ZIP-бомбу и был заблокирован.");

                if (!isManifest) ValidateImageHeader(entry, extension);
            }

            if (!names.Contains("theme.json")) throw Invalid("В архиве отсутствует theme.json.");
        }
        catch (InvalidDataException exception) when (!exception.Message.StartsWith("Небезопасная тема:", StringComparison.Ordinal))
        {
            throw Invalid("ZIP повреждён или использует неподдерживаемый формат.", exception);
        }
        catch (OverflowException exception)
        {
            throw Invalid("Некорректные размеры файлов в архиве.", exception);
        }
    }

    private static void ValidateImageHeader(ZipArchiveEntry entry, string extension)
    {
        Span<byte> header = stackalloc byte[8];
        using var input = entry.Open();
        var read = input.Read(header);
        var valid = extension.ToLowerInvariant() switch
        {
            ".png" => read >= 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".gif" => read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
            ".bmp" => read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M',
            _ => false
        };
        if (!valid) throw Invalid($"Файл {entry.FullName} не является корректным изображением {extension}.");
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new($"Небезопасная тема: {message}", inner);
}
