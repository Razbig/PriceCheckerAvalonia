using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using Microsoft.Extensions.Logging;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Core.Services
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

        public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
        {
            var result = new SyncResult();
            var lastSync = _localDb.GetLastSyncTime();
            var syncStart = DateTime.UtcNow;

            _logger.LogInformation("Sync from {LastSync}", lastSync);

            try
            {
                await using var conn = new NpgsqlConnection(_pgConnectionString);
                await conn.OpenAsync(ct);

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
                    _logger.LogInformation("Updated {Count} products", products.Count);
                }

                // Fetch t_bar rows (sample limit 100)
                // Try to fetch t_bar from master using master_ip saved in local settings
                try
                {
                    var masterIp = _localDb.GetSetting("master_ip");
                    if (!string.IsNullOrEmpty(masterIp))
                    {
                        var barConnStr = $"Host={masterIp};Port=5432;Database=iris-db;Username=dba;Password=dba;Pooling=true;";
                        await using var barConn = new NpgsqlConnection(barConnStr);
                        await barConn.OpenAsync(ct);

                        var bars = (await barConn.QueryAsync<Bar>("""
                            SELECT id_bar AS IdBar, id_article AS IdArticle, id_measure AS IdMeasure,
                                   bar AS BarValue, dtype AS Dtype, memo AS Memo
                              FROM public.t_bar
                        """ )).ToList();

                        result.BarsFetched = bars.Count;
                        if (bars.Any())
                        {
                            _localDb.UpsertBars(bars);
                            _logger.LogInformation("Updated {Count} bars from master {Master}", bars.Count, masterIp);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("master_ip not configured; skipping t_bar fetch");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to fetch t_bar from master: {Error}", ex.Message);
                }

                _localDb.SetLastSyncTime(syncStart);
                result.Success = true;
            }
            catch (NpgsqlException ex)
            {
                result.Error = $"PostgreSQL: {ex.Message}";
                _logger.LogWarning("Master DB connection failed: {Error}", ex.Message);
            }

            return result;
        }

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
        public int BarsFetched { get; set; }
        public string? Error { get; set; }
    }
}