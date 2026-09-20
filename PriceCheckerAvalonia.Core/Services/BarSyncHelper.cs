using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using PriceCheckerAvalonia.Core.Model;
using System.Collections.Generic;

namespace PriceCheckerAvalonia.Core.Services
{
    public static class BarSyncHelper
    {
        // Fetch all rows from master's t_bar and upsert into local DB
        public static async Task<int> FetchAndUpsertBarsAsync(LocalDatabase localDb)
        {
            if (localDb == null) throw new ArgumentNullException(nameof(localDb));

            var masterIp = localDb.GetSetting("master_ip");
            if (string.IsNullOrEmpty(masterIp))
                return 0;

            var barConnStr = $"Host={masterIp};Port=5432;Database=iris-db;Username=dba;Password=dba;Pooling=true;";

            await using var conn = new NpgsqlConnection(barConnStr);
            await conn.OpenAsync();

            var bars = (await conn.QueryAsync<t_bar>(@"
                SELECT 
                    t_bar.id_bar,
                    t_bar.id_article,
                    t_bar.id_measure,
                    t_bar.bar,
                    t_bar.dtype,
                    t_bar.memo,
                    t_price.price
                FROM public.t_bar
                LEFT JOIN public.t_price 
                    ON t_price.id_bar = t_bar.id_bar
            ")).ToList();

            if (bars.Any())
            {
                localDb.UpsertBars(bars);
            }

            return bars.Count;
        }
    }
}
