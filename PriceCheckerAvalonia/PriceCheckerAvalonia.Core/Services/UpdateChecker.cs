using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Core.Services
{
    public class UpdateChecker
    {
        private readonly HttpClient _http;

        public UpdateChecker(HttpClient httpClient)
        {
            _http = httpClient ?? new HttpClient();
        }

        public async Task<UpdateInfo?> CheckForUpdateAsync(string serverBaseUrl, string channel = "stable", string platform = "win-x64", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(serverBaseUrl)) throw new ArgumentNullException(nameof(serverBaseUrl));
            var uri = $"{serverBaseUrl.TrimEnd('/')}/updates/latest?channel={Uri.EscapeDataString(channel)}&platform={Uri.EscapeDataString(platform)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified) return null;
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var info = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream, cancellationToken: ct).ConfigureAwait(false);
            return info;
        }

        public async Task<string> DownloadUpdateAsync(UpdateInfo update, string destinationFolder, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (string.IsNullOrWhiteSpace(update.Url)) throw new ArgumentException("Update url is empty", nameof(update));
            Directory.CreateDirectory(destinationFolder);
            using var resp = await _http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var fileName = GetFileNameFromResponse(resp) ?? Path.GetFileName(new Uri(update.Url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "update.bin";
            var destPath = Path.Combine(destinationFolder, fileName);
            await using var httpStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var outStream = File.Create(destPath);
            using var sha = SHA256.Create();
            var buffer = new byte[81920];
            long totalRead = 0;
            var contentLength = resp.Content.Headers.ContentLength ?? -1L;
            int read;
            while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await outStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                sha.TransformBlock(buffer, 0, read, null, 0);
                totalRead += read;
                if (contentLength > 0 && progress != null)
                {
                    progress.Report((double)totalRead / contentLength);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            await outStream.FlushAsync(ct).ConfigureAwait(false);
            var actual = BitConverter.ToString(sha.Hash!).Replace("-", "").ToLowerInvariant();
            var expected = (update.Sha256 ?? "").Replace("-", "").ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(destPath); } catch { }
                throw new InvalidDataException($"SHA256 mismatch. Expected {expected}, got {actual}");
            }
            progress?.Report(1.0);
            return destPath;
        }

        private static string? GetFileNameFromResponse(HttpResponseMessage resp)
        {
            if (resp.Content.Headers.ContentDisposition?.FileNameStar is string fnstar && !string.IsNullOrWhiteSpace(fnstar)) return fnstar.Trim('"');
            if (resp.Content.Headers.ContentDisposition?.FileName is string fn && !string.IsNullOrWhiteSpace(fn)) return fn.Trim('"');
            return null;
        }
    }
}
