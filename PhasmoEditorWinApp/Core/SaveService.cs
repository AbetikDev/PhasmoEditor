using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PhasmoEditor.Core;

public sealed record SaveSnapshot(string Path, byte[] Original, JsonObject Document,
    int Money, int Level, int Prestige);

public sealed class SaveService
{
    private static readonly string[] Fields = ["PlayersMoney", "NewLevel", "Prestige"];
    private const string Password = "t36gref9u84y7f43g";

    public SaveSnapshot Load(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(path);
        var doc = Decode(bytes);
        return new(path, bytes, doc, ReadValue(doc, Fields[0]), ReadValue(doc, Fields[1]), ReadValue(doc, Fields[2]));
    }

    public string Save(SaveSnapshot snapshot, int money, int level, int prestige)
    {
        if (money < 0 || level < 0 || prestige < 0)
            throw SaveErrors.Tag(new ArgumentException("Values cannot be negative."), "NegativeValues");
        var updated = (JsonObject)snapshot.Document.DeepClone();
        var values = new[] { money, level, prestige };
        for (int i = 0; i < Fields.Length; i++)
            updated[Fields[i]]!["value"] = values[i];
        // Preserve Easy Save's nonstandard dictionary keys, spacing and all unrelated data.
        var text = DecryptText(snapshot.Original);
        var replacements = Fields.Zip(values).ToDictionary(p => p.First, p => p.Second);
        var encrypted = EncryptText(PatchValues(text, replacements));
        if (!JsonNode.DeepEquals(updated, Decode(encrypted)))
            throw SaveErrors.Tag(new InvalidDataException("Verification failed."), "VerificationFailed");
        return Replace(snapshot, encrypted);
    }

    public string Restore(SaveSnapshot current, string backupPath)
    {
        var backup = Load(backupPath);
        return Replace(current, backup.Original);
    }

    public string ExportOriginal(string path, string destinationDirectory)
    {
        // Read the current file on disk, never the pending editor values.
        var snapshot = Load(path);
        Directory.CreateDirectory(destinationDirectory);
        string destination = System.IO.Path.Combine(destinationDirectory,
            System.IO.Path.GetFileNameWithoutExtension(path) + "-old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
            + "-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(path));
        bool created = false;
        try
        {
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            stream.Write(snapshot.Original);
            stream.Flush(true);
            return destination;
        }
        catch
        {
            if (created) File.Delete(destination);
            throw;
        }
    }

    public string Restore(string currentPath, string backupPath)
    {
        var backup = Load(backupPath);
        var path = System.IO.Path.GetFullPath(currentPath);
        var current = new SaveSnapshot(path, File.ReadAllBytes(path), backup.Document,
            backup.Money, backup.Level, backup.Prestige);
        return Replace(current, backup.Original);
    }

    public static string BackupDirectory(string path) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "PhasmoEditorBackups");

    public static string[] Backups(string path)
    {
        var directory = BackupDirectory(path);
        var prefix = System.IO.Path.GetFileName(path) + ".";
        var backups = Directory.Exists(directory)
            ? Directory.GetFiles(directory).Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)).OrderDescending().ToArray()
            : [];
        var legacy = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,
            System.IO.Path.GetFileNameWithoutExtension(path) + "Backup" + System.IO.Path.GetExtension(path));
        return File.Exists(legacy) ? [.. backups, legacy] : backups;
    }

    public static string[] Discover(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(directory, "SaveFile.txt", options)
            .Where(p => !p.Split(System.IO.Path.DirectorySeparatorChar).Contains("PhasmoEditorBackups", StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int ReadValue(JsonObject document, string field)
    {
        if (document[field] is not JsonObject entry || entry["value"] is not JsonValue value
            || !value.TryGetValue<int>(out var number))
            throw SaveErrors.Tag(new InvalidDataException($"Unsupported field: {field}."), "InvalidField", field);
        return number;
    }

    public static JsonObject Decode(byte[] bytes)
    {
        if (bytes.Length < 32 || (bytes.Length - 16) % 16 != 0)
            throw SaveErrors.Tag(new InvalidDataException("Unsupported or damaged save."), "InvalidFormat");
        try
        {
            var text = DecryptText(bytes).TrimStart('\uFEFF').TrimEnd('\0');
            // Match quoted strings first, so digits inside strings are never rewritten.
            var normalized = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"|-?[0-9]+(?=\\s*:)",
                m => m.Value.StartsWith('"') ? m.Value : "\"" + m.Value + "\"");
            var document = JsonNode.Parse(normalized) as JsonObject
                ?? throw SaveErrors.Tag(new InvalidDataException("Expected a JSON object."), "ExpectedObject");
            foreach (var field in Fields) ReadValue(document, field);
            return document;
        }
        catch (Exception ex) when (ex is CryptographicException or System.Text.Json.JsonException or DecoderFallbackException)
        {
            throw SaveErrors.Tag(new InvalidDataException("Could not decode the save.", ex), "DecodeFailed");
        }
    }

    public static byte[] Encode(JsonObject document)
        => EncryptText(document.ToJsonString());

    public static string DecryptText(byte[] bytes)
    {
        using var aes = Aes.Create();
        aes.IV = bytes[..16];
        aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), aes.IV, 100, HashAlgorithmName.SHA1, 16);
        return new UTF8Encoding(false, true).GetString(aes.DecryptCbc(bytes[16..], aes.IV, PaddingMode.PKCS7));
    }

    private static string PatchValues(string text, Dictionary<string, int> values)
    {
        int depth = 0;
        string? field = null, property = null;
        var changed = new HashSet<string>();
        var result = Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"|[{}\\[\\]]|-?[0-9]+", m =>
        {
            string token = m.Value;
            if (token is "{" or "[") { depth++; return token; }
            if (token is "}" or "]") { depth--; return token; }
            if (token.StartsWith('"'))
            {
                var tail = text.AsSpan(m.Index + m.Length).TrimStart();
                if (!tail.IsEmpty && tail[0] == ':')
                {
                    if (depth == 1) field = JsonSerializer.Deserialize<string>(token);
                    if (depth == 2) property = JsonSerializer.Deserialize<string>(token);
                }
                return token;
            }
            if (depth == 2 && property == "value" && field is not null && values.TryGetValue(field, out int value))
            {
                if (!changed.Add(field)) throw new InvalidDataException("Duplicate editable field.");
                return value.ToString(CultureInfo.InvariantCulture);
            }
            return token;
        });
        if (changed.Count != values.Count) throw new InvalidDataException("Missing editable field.");
        return result;
    }

    private static byte[] EncryptText(string text)
    {
        using var aes = Aes.Create();
        aes.GenerateIV();
        aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), aes.IV, 100, HashAlgorithmName.SHA1, 16);
        var encrypted = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), aes.IV, PaddingMode.PKCS7);
        return [.. aes.IV, .. encrypted];
    }

    private static string Replace(SaveSnapshot snapshot, byte[] replacement)
    {
        var directory = System.IO.Path.GetDirectoryName(snapshot.Path)!;
        var temporary = System.IO.Path.Combine(directory, ".phasmoeditor-" + Guid.NewGuid().ToString("N") + ".tmp");
        var backupDir = BackupDirectory(snapshot.Path);
        Directory.CreateDirectory(backupDir);
        var backup = System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(snapshot.Path) + "."
            + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "." + Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(replacement);
                stream.Flush(true);
            }
            if (!File.ReadAllBytes(snapshot.Path).AsSpan().SequenceEqual(snapshot.Original))
                throw SaveErrors.Tag(new IOException("The save changed after loading."), "FileChanged");
            // Atomic replacement also preserves the previous destination as a backup.
            File.Replace(temporary, snapshot.Path, backup);
            return backup;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal static class SaveErrors
{
    public static T Tag<T>(T exception, string key, params object[] arguments) where T : Exception
    {
        exception.Data["MessageKey"] = key;
        exception.Data["MessageArguments"] = arguments;
        return exception;
    }
}
