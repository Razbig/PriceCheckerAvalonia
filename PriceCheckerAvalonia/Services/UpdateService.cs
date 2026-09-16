using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace PriceCheckerAvalonia.Services
{
    public class UpdateService
    {
        private const string GitHubRepositoryUrl =
            "https://github.com/Razbig/PriceCheckerAvalonia";

        private readonly UpdateManager _updateManager;

        public UpdateService()
        {
            var source = new GithubSource(
                GitHubRepositoryUrl,
                null,
                false);

            _updateManager = new UpdateManager(source);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                return await _updateManager
                    .CheckForUpdatesAsync()
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[UpdateService] CheckForUpdatesAsync error: {ex}");

                return null;
            }
        }

        public async Task<bool> DownloadUpdateAsync(
            UpdateInfo updateInfo,
            Action<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _updateManager.DownloadUpdatesAsync(
                    updateInfo,
                    progress,
                    cancellationToken)
                    .ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[UpdateService] DownloadUpdateAsync error: {ex}");

                return false;
            }
        }

        public void ApplyUpdateAndRestart(UpdateInfo updateInfo)
        {
            try
            {
                _updateManager.ApplyUpdatesAndRestart(updateInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[UpdateService] ApplyUpdateAndRestart error: {ex}");
            }
        }

        public string CurrentVersion =>
            _updateManager.CurrentVersion?.ToString() ?? "unknown";
    }
}