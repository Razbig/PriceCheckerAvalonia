using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PriceCheckerAvalonia.Services;

public sealed class MediaContentService
{
    private const string Server = "https://pim.almi.odesa.ua/RetailHelper/hs/selfcheckout/media/";
    private const string Login = "PriceChecker";
    private const string Password = "PassPriceChecker";
    private const string PosId = "1001";
    private const long MinimumFileSize = 100_000;

    private readonly HttpClient _httpClient;
    private readonly string _mediaDirectory;
    private readonly string _logFile;

    public MediaContentService(string mediaDirectory)
    {
        _mediaDirectory = mediaDirectory;
        _logFile = Path.Combine(mediaDirectory, "media-sync.log");

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Login}:{Password}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", auth);
        _httpClient.DefaultRequestHeaders.Add("POSID", PosId);
    }

    public async Task<IReadOnlyList<string>> UpdateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_mediaDirectory);
        Log("Media synchronization started.");

        try
        {
            var catalog = await DownloadCatalogAsync(cancellationToken);
            Log($"Catalog received: {catalog.Count} records.");
            if (catalog.Count == 0)
                return GetLocalVideoFiles();

            var activeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in catalog)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(item.Code))
                    continue;

                var filePath = Path.Combine(_mediaDirectory, item.Code + ".mp4");
                var metadataPath = filePath + ".meta";
                var isActive = !item.Deleted && item.PlayForPriceChecker;
                if (isActive)
                    Log($"Active media: {item.Code}, changed: {item.LastChanged}.");

                if (!isActive)
                {
                    DeleteIfExists(filePath);
                    DeleteIfExists(metadataPath);
                    continue;
                }

                activeCodes.Add(item.Code);
                if (!File.Exists(filePath) || !string.Equals(ReadMetadata(metadataPath), item.LastChanged, StringComparison.Ordinal))
                    await DownloadFileAsync(item.Code, filePath, metadataPath, item.LastChanged, cancellationToken);
            }

            foreach (var file in Directory.EnumerateFiles(_mediaDirectory, "*.mp4"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                if (!activeCodes.Contains(code))
                {
                    DeleteIfExists(file);
                    DeleteIfExists(file + ".meta");
                }
            }

            var files = GetLocalVideoFiles();
            Log($"Media synchronization completed. Local videos: {files.Count}.");
            return files;
        }
        catch (Exception ex)
        {
            Log($"Media synchronization failed: {ex}");
            throw;
        }
    }

    public void WriteDiagnostic(string message) => Log(message);

    private async Task<IReadOnlyList<MediaCatalogItem>> DownloadCatalogAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(Server, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Media catalog response is not a JSON array.");

        var result = new List<MediaCatalogItem>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var code = GetString(element, "Код");
            if (!string.IsNullOrWhiteSpace(code))
            {
                result.Add(new MediaCatalogItem(
                    code,
                    GetBoolean(element, "ПометкаУдаления"),
                    GetBoolean(element, "ВідтворюватиПрайсчекер"),
                    GetString(element, "Змінено")));
            }
        }

        return result;
    }

    private async Task DownloadFileAsync(
        string code,
        string destinationPath,
        string metadataPath,
        string lastChanged,
        CancellationToken cancellationToken)
    {
        var temporaryPath = destinationPath + ".tmp";
        DeleteIfExists(temporaryPath);

        using var response = await _httpClient.GetAsync(Server + Uri.EscapeDataString(code),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Log($"Downloading media {code}. HTTP {(int)response.StatusCode}.");

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (new FileInfo(temporaryPath).Length < MinimumFileSize)
        {
            DeleteIfExists(temporaryPath);
            throw new InvalidDataException($"Downloaded media file is too small: {code}");
        }

        File.Move(temporaryPath, destinationPath, true);
        await File.WriteAllTextAsync(metadataPath, lastChanged ?? string.Empty, cancellationToken);
        Log($"Downloaded media {code}: {new FileInfo(destinationPath).Length} bytes.");
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result));

    private static string? ReadMetadata(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private IReadOnlyList<string> GetLocalVideoFiles() =>
        Directory.EnumerateFiles(_mediaDirectory)
            .Where(IsVideoFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static bool IsVideoFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MediaCatalogItem(
        string Code,
        bool Deleted,
        bool PlayForPriceChecker,
        string LastChanged);
}
