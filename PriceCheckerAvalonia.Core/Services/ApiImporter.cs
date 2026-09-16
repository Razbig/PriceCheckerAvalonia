using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Core.Services
{
    public class ApiImporter
    {
        private readonly HttpClient _http;
        private readonly LocalDatabase _localDb;
        private readonly ILogger _logger;

        public ApiImporter(LocalDatabase localDb, ILogger logger)
        {
            _http = new HttpClient();
            _localDb = localDb;
            _logger = logger;
        }

        public async Task ImportFromGzipJsonUrlAsync(string url, CancellationToken ct = default)
        {
            _logger.LogInformation("Starting import from {Url}", url);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            // Read entire response into memory (so we can try multiple decompression strategies)
            await using var responseStream = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            RootDto? root = null;

            // 1) Try open as ZIP archive (some providers return .gz which is actually a ZIP)
            try
            {
                ms.Position = 0;
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
                if (zip.Entries.Count > 0)
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                                ?? zip.Entries.First();
                    using var entryStream = entry.Open();
                    root = await JsonSerializer.DeserializeAsync<RootDto>(entryStream, options, ct);
                }
            }
            catch (InvalidDataException)
            {
                // not a zip
                _logger.LogDebug("Response is not a ZIP archive");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read response as ZIP archive");
            }

            // 2) If not ZIP, try GZip decompression
            if (root == null)
            {
                try
                {
                    ms.Position = 0;
                    using var gz = new GZipStream(ms, CompressionMode.Decompress, leaveOpen: true);
                    using var decompressed = new MemoryStream();
                    await gz.CopyToAsync(decompressed, ct);
                    decompressed.Position = 0;
                    root = await JsonSerializer.DeserializeAsync<RootDto>(decompressed, options, ct);
                }
                catch (InvalidDataException)
                {
                    _logger.LogDebug("Response is not a valid GZip stream");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decompress GZip stream");
                }
            }

            // 3) Fallback: try raw JSON
            if (root == null)
            {
                try
                {
                    ms.Position = 0;
                    root = await JsonSerializer.DeserializeAsync<RootDto>(ms, options, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize JSON from {Url}", url);
                    throw;
                }
            }

            if (root?.Products == null || root.Products.Count == 0)
            {
                _logger.LogInformation("No products found in feed {Url}", url);
                return;
            }

            var imagesDir = Path.Combine(Environment.CurrentDirectory, "images");
            Directory.CreateDirectory(imagesDir);

            var products = new List<Product>();

            foreach (var p in root.Products)
            {
                try
                {
                    var articleRaw = p.Article ?? string.Empty;
                    // remove non-digit characters
                    var digits = new string(articleRaw.Where(char.IsDigit).ToArray());
                    if (!int.TryParse(digits, out var articleInt))
                    {
                        _logger.LogWarning("Skipping product with invalid article '{ArticleRaw}'", articleRaw);
                        continue;
                    }

                    var prod = new Product
                    {
                        Article = articleInt,
                        Barcode = digits,
                        Name = p.Name ?? string.Empty,
                        Category = p.Category ?? string.Empty,
                        Price = 0.0,
                        Country = p.GetCharacteristicValue("Країна") ?? string.Empty,
                        Brand = p.GetCharacteristicValue("Торговельна марка") ?? string.Empty,
                        ProductType = p.GetCharacteristicValue("Вид продукції") ?? string.Empty,
                        StockQty = 0,
                        UpdatedAt = DateTime.UtcNow
                    };

                    var imageUrl = p.Media?.Url;
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        // Store image URL in DB; actual download will be performed later when the product is scanned
                        prod.ImagePath = imageUrl;
                    }

                    products.Add(prod);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process product entry");
                }
            }

            if (products.Any())
            {
                _localDb.UpsertProducts(products);
                _logger.LogInformation("Imported {Count} products from {Url}", products.Count, url);
            }
            else
            {
                _logger.LogInformation("No valid products to import from {Url}", url);
            }
        }

        public Task<IEnumerable<Product>> LoadProductsFromJsonAsync(string path)
        {
            using var fs = File.OpenRead(path);
            var items = JsonSerializer.DeserializeAsync<IEnumerable<Product>>(fs).Result;
            return Task.FromResult(items ?? Array.Empty<Product>() as IEnumerable<Product>);
        }

        private class RootDto
        {
            [JsonPropertyName("products")]
            public List<ProductDto> Products { get; set; } = new();
        }

        private class ProductDto
        {
            [JsonPropertyName("article")]
            public string? Article { get; set; }

            [JsonPropertyName("Наименование")]
            public string? Name { get; set; }

            [JsonPropertyName("Category")]
            public string? Category { get; set; }

            [JsonPropertyName("base_unit")]
            public string? BaseUnit { get; set; }

            [JsonPropertyName("characteristics")]
            public List<CharacteristicDto> Characteristics { get; set; } = new();

            [JsonPropertyName("media")]
            public MediaDto? Media { get; set; }

            public string? GetCharacteristicValue(string name)
                => Characteristics?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private class CharacteristicDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("value")]
            public string? Value { get; set; }
        }

        private class MediaDto
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("LastModified")]
            public DateTime LastModified { get; set; }
        }
    }
}
