// Services/LocalDatabase.cs
using System.Collections.Generic;
using System;
using Dapper;
using Microsoft.Data.Sqlite;

public class LocalDatabase
{
    private readonly string _connectionString;

    public LocalDatabase(string dbPath)
    {
        // WAL-режим: устойчив к сбоям питания
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();

        // WAL включается один раз навсегда для файла
        conn.Execute("PRAGMA journal_mode=WAL;");
        conn.Execute("PRAGMA synchronous=NORMAL;"); // баланс скорости и надёжности
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
        """);
    }

    public SqliteConnection OpenConnection()
        => new SqliteConnection(_connectionString);

    // Поиск по штрихкоду (из MainPage/Scanner)
    public Product? FindByBarcode(string barcode)
    {
        using var conn = OpenConnection();
        return conn.QueryFirstOrDefault<Product>(
            "SELECT * FROM products WHERE barcode = @barcode LIMIT 1",
            new { barcode });
    }

    // Время последней синхронизации
    public DateTime GetLastSyncTime()
    {
        using var conn = OpenConnection();
        var val = conn.QueryFirstOrDefault<string>(
            "SELECT value FROM sync_meta WHERE key = 'last_sync'");
        return val is null ? DateTime.MinValue : DateTime.Parse(val);
    }

    public void SetLastSyncTime(DateTime time)
    {
        using var conn = OpenConnection();
        conn.Execute("""
            INSERT INTO sync_meta(key, value) VALUES('last_sync', @val)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
        """, new { val = time.ToString("O") });
    }

    // Массовая вставка/обновление (upsert)
    public void UpsertProducts(IEnumerable<Product> products)
    {
        using var conn = OpenConnection();
        conn.Open();
        using var tx = conn.BeginTransaction(); // транзакция = атомарность
        try
        {
            foreach (var p in products)
            {
                conn.Execute("""
                    INSERT INTO products
                        (id, barcode, name, price, category, country, brand,
                         product_type, stock_qty, image_path, updated_at)
                    VALUES
                        (@Id, @Barcode, @Name, @Price, @Category, @Country, @Brand,
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
                """, p, tx);
            }
            tx.Commit(); // либо всё, либо ничего
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}