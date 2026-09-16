// Services/BackgroundSyncWorker.cs
using System.Threading;
using System;
using PriceCheckerAvalonia.Services;

namespace PriceCheckerAvalonia.Services
{
    public class BackgroundSyncWorker : IDisposable
    {
        private readonly SyncService _sync;
        private readonly TimeSpan _interval;
        private Timer? _timer;
        private readonly CancellationTokenSource _cts = new();

        public BackgroundSyncWorker(SyncService sync, TimeSpan interval)
        {
            _sync = sync;
            _interval = interval;
        }

        public void Start()
        {
            _timer = new Timer(async _ =>
            {
                await _sync.SyncAsync(_cts.Token);
            }, null, TimeSpan.Zero, _interval);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _timer?.Dispose();
        }
    }
}
