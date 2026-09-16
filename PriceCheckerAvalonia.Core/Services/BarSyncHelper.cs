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

            var bars = (await conn.QueryAsync<Bar>(@"
                SELECT id_bar AS IdBar, id_article AS IdArticle, id_measure AS IdMeasure,
                       bar AS BarValue, dtype AS Dtype, memo AS Memo
                  FROM public.t_bar
            ")).ToList();

            if (bars.Any())
            {
                localDb.UpsertBars(bars);
            }

            return bars.Count;
        }
    }
}
