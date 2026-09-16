using System;
using System.Collections.Generic;
using Dapper;
using Microsoft.Data.Sqlite;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Core.Services
{
    public class LocalDatabase
    {
        private readonly string _connectionString;

        public LocalDatabase(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
            InitializeSchema();
        }

        public void UpsertStores(IEnumerable<Store> stores)
        {
            using var conn = OpenConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var s in stores)
                {
                    conn.Execute("""
                        INSERT INTO stores (shop_id, shop_name)
                        VALUES (@ShopId, @ShopName)
                        ON CONFLICT(shop_id) DO UPDATE SET
                            shop_name = excluded.shop_name
                    """, s, tx);
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public List<Store> GetStores()
        {
            using var conn = OpenConnection();
            conn.Open();
            var rows = conn.Query<Store>(
                "SELECT shop_id AS ShopId, shop_name AS ShopName FROM stores ORDER BY shop_name");
            return rows.AsList();
        }

        private void InitializeSchema()
        {
            using var conn = OpenConnection();
            conn.Execute("PRAGMA journal_mode=WAL;");
            conn.Execute("PRAGMA synchronous=NORMAL;");
            conn.Execute("PRAGMA foreign_keys=ON;");

            conn.Execute("""
                CREATE TABLE IF NOT EXISTS products (
                    id          INTEGER PRIMARY KEY,
                    barcode     TEXT NOT NULL UNIQUE,
                    name        TEXT NOT NULL,
                    price       REAL NOT NULL,
                    category    TEXT,
                    country     TEXT,
                    brand       TEXT,
                    product_type TEXT,
                    stock_qty   INTEGER DEFAULT 0,
                    image_path  TEXT,
                    updated_at  TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_products_barcode ON products(barcode);

                CREATE TABLE IF NOT EXISTS sync_meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS t_bar (
                    id_bar     INTEGER PRIMARY KEY,
                    id_article INTEGER NOT NULL,
                    id_measure INTEGER NOT NULL,
                    bar        TEXT,
                    dtype      INTEGER,
                    memo       TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_t_bar_id_article ON t_bar(id_article);

                -- Добавлено: таблица stores
                CREATE TABLE IF NOT EXISTS stores (
                    shop_id   INTEGER PRIMARY KEY,
                    shop_name TEXT NOT NULL
                );
            """);
        }

        public SqliteConnection OpenConnection()
            => new SqliteConnection(_connectionString);

        public Product? FindByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            barcode = barcode.Trim();

            using var conn = OpenConnection();
            // Try to find product by direct barcode match in products, or by t_bar.bar (joined by article)
            // Return a single product row and, if available, the article from t_bar as Article property.
            return conn.QueryFirstOrDefault<Product>(
                @"SELECT
                    p.id        AS Id,
                    b.id_article AS Article,
                    p.barcode   AS Barcode,
                    p.name      AS Name,
                    p.price     AS Price,
                    p.category  AS Category,
                    p.country   AS Country,
                    p.brand     AS Brand,
                    p.product_type AS ProductType,
                    p.stock_qty AS StockQty,
                    p.image_path AS ImagePath,
                    p.updated_at AS UpdatedAt
                  FROM  t_bar b
                  LEFT JOIN products p ON b.id_article = p.barcode
                  WHERE b.bar = @barcode
                  LIMIT 1",
                new { barcode });
        }
        //WHERE p.barcode = @barcode OR b.bar = @barcode


        public DateTime GetLastSyncTime()
        {
            using var conn = OpenConnection();
            var val = conn.QueryFirstOrDefault<string>(
                "SELECT value FROM sync_meta WHERE key = 'last_sync'");
            return val is null ? DateTime.MinValue : DateTime.Parse(val);
        }

        // Generic settings access (stored in sync_meta)
        public string? GetSetting(string key)
        {
            using var conn = OpenConnection();
            return conn.QueryFirstOrDefault<string>(
                "SELECT value FROM sync_meta WHERE key = @key", new { key });
        }

        public void SetSetting(string key, string value)
        {
            using var conn = OpenConnection();
            conn.Execute("""
                INSERT INTO sync_meta(key, value) VALUES(@key, @val)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """, new { key, val = value });
        }

        // Convenience helpers
        public string GetMasterPgConnectionString()
            => GetSetting("master_pg_conn") ?? string.Empty;

        public void SetMasterPgConnectionString(string connStr)
            => SetSetting("master_pg_conn", connStr);

        public string GetPrintTypeDefault()
            => GetSetting("print_type") ?? "32"; // default to "32"

        public void SetPrintTypeDefault(string printType)
            => SetSetting("print_type", printType);

        public string GetShopName()
            => GetSetting("shop_name") ?? string.Empty;

        public int GetShopId()
        {
            var v = GetSetting("shop_id");
            return int.TryParse(v, out var id) ? id : 0;
        }

        public void SetShop(int id, string name)
        {
            SetSetting("shop_id", id.ToString());
            SetSetting("shop_name", name);
        }

        public void SetLastSyncTime(DateTime time)
        {
            using var conn = OpenConnection();
            conn.Execute("""
                INSERT INTO sync_meta(key, value) VALUES('last_sync', @val)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """, new { val = time.ToString("O") });
        }

        public void UpsertProducts(IEnumerable<Product> products)
        {
            using var conn = OpenConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var p in products)
                {
                    var param = new
                    {
                        p.Barcode,
                        p.Name,
                        p.Price,
                        p.Category,
                        p.Country,
                        p.Brand,
                        ProductType = p.ProductType,
                        StockQty = p.StockQty,
                        ImagePath = p.ImagePath,
                        UpdatedAt = p.UpdatedAt
                    };

                    try
                    {
                        conn.Execute("""
                            INSERT INTO products
                                (barcode, name, price, category, country, brand,
                                 product_type, stock_qty, image_path, updated_at)
                            VALUES
                                (@Barcode, @Name, @Price, @Category, @Country, @Brand,
                                 @ProductType, @StockQty, @ImagePath, @UpdatedAt)
                            ON CONFLICT(barcode) DO UPDATE SET
                                name         = excluded.name,
                                price        = excluded.price,
                                category     = excluded.category,
                                country      = excluded.country,
                                brand        = excluded.brand,
                                product_type = excluded.product_type,
                                stock_qty    = excluded.stock_qty,
                                image_path   = excluded.image_path,
                                updated_at   = excluded.updated_at
                        """, param, tx);
                    }
                    catch (SqliteException ex)
                    {
                        tx.Rollback();
                        throw new Exception($"SQLite error {ex.SqliteErrorCode} while upserting product (barcode={p.Barcode}): {ex.Message}", ex);
                    }
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void UpsertBars(IEnumerable<Bar> bars)
        {
            using var conn = OpenConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var b in bars)
                {
                    conn.Execute("""
                        INSERT INTO t_bar
                            (id_bar, id_article, id_measure, bar, dtype, memo)
                        VALUES
                            (@IdBar, @IdArticle, @IdMeasure, @BarValue, @Dtype, @Memo)
                        ON CONFLICT(id_bar) DO UPDATE SET
                            id_article = excluded.id_article,
                            id_measure = excluded.id_measure,
                            bar        = excluded.bar,
                            dtype      = excluded.dtype,
                            memo       = excluded.memo
                    """, b, tx);
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
