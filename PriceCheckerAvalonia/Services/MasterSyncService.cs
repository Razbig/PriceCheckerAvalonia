// Services/SyncService.cs
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Services
{
    public class SyncService
    {
        private readonly string _pgConnectionString;
        private readonly LocalDatabase _localDb;
        private readonly ILogger _logger;

        public SyncService(string pgConnStr, LocalDatabase localDb, ILogger logger)
        {
            _pgConnectionString = pgConnStr;
            _localDb = localDb;
            _logger = logger;
        }

        // Дельта-синхронизация: только изменения с последней синхронизации
        public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
        {
            var result = new SyncResult();
            var lastSync = _localDb.GetLastSyncTime();
            var syncStart = DateTime.UtcNow;

            _logger.LogInformation("Синхронизация с {LastSync}", lastSync);

            try
            {
                await using var conn = new NpgsqlConnection(_pgConnectionString);
                await conn.OpenAsync(ct);

                // Получаем только изменённые записи
                var products = (await conn.QueryAsync<Product>("""
                    SELECT
                        id, barcode, name, price,
                        category, country, brand, product_type,
                        stock_qty, image_path, updated_at
                    FROM products
                    WHERE updated_at > @lastSync
                    ORDER BY updated_at
                    LIMIT 5000
                """, new { lastSync })).ToList();

                result.FetchedCount = products.Count;

                if (products.Any())
                {
                    _localDb.UpsertProducts(products);
                    _logger.LogInformation("Обновлено {Count} товаров", products.Count);
                }

                // Сохраняем время только при успехе
                _localDb.SetLastSyncTime(syncStart);
                result.Success = true;
            }
            catch (NpgsqlException ex)
            {
                result.Error = $"PostgreSQL: {ex.Message}";
                _logger.LogWarning("Нет соединения с мастер-БД: {Error}", ex.Message);
                // Работаем дальше с локальными данными — это нормально
            }

            return result;
        }

        // Полная пересинхронизация (например, первый запуск)
        public async Task FullSyncAsync(CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConnectionString);
            await conn.OpenAsync(ct);

            const int batchSize = 500;
            long offset = 0;

            while (true)
            {
                var batch = (await conn.QueryAsync<Product>(
                    $"SELECT * FROM products ORDER BY id LIMIT {batchSize} OFFSET {offset}")).ToList();

                if (!batch.Any()) break;

                _localDb.UpsertProducts(batch);
                offset += batchSize;
            }

            _localDb.SetLastSyncTime(DateTime.UtcNow);
        }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public int FetchedCount { get; set; }
        public string? Error { get; set; }
    }
}