using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _1RM.Model;
using _1RM.Utils.Reachability;
using _1RM.View;
using Shawn.Utils;

namespace _1RM.Service
{
    /// <summary>
    /// Keeps the reachability dot on each server in the list roughly honest.
    ///
    /// Off unless asked for: this opens a connection to every server on a timer, which on a corporate
    /// network looks a lot like a port scan and is not something to start doing behind the user's back.
    /// </summary>
    public class ServerReachabilityService : IDisposable
    {
        private const int PROBE_TIMEOUT_MS = 2500;

        /// <summary>
        /// Enough to get through a long list quickly, few enough that a sweep does not look like a burst of
        /// scanning traffic or exhaust the connection table on a laptop.
        /// </summary>
        private const int MAX_CONCURRENT_PROBES = 8;

        public const int MIN_INTERVAL_SECONDS = 15;
        public const int MAX_INTERVAL_SECONDS = 60 * 60;

        private readonly ConfigurationService _configurationService;
        private readonly ProxyService _proxyService;

        private readonly object _lock = new object();
        private System.Timers.Timer? _timer;
        private CancellationTokenSource? _cts;

        /// <summary>Guards against a slow sweep overlapping the next tick.</summary>
        private int _sweeping;

        public ServerReachabilityService(ConfigurationService configurationService, ProxyService proxyService)
        {
            _configurationService = configurationService;
            _proxyService = proxyService;
        }

        /// <summary>
        /// Starts, stops or re-times the sweep to match the current settings. Called at launch and whenever
        /// the toggle on the general page changes.
        /// </summary>
        public void ApplyConfiguration()
        {
            lock (_lock)
            {
                StopLocked();

                if (!_configurationService.General.CheckServerReachability)
                {
                    ClearStates();
                    return;
                }

                var interval = Math.Clamp(_configurationService.General.ServerReachabilityIntervalSeconds,
                    MIN_INTERVAL_SECONDS, MAX_INTERVAL_SECONDS);

                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                _timer = new System.Timers.Timer(interval * 1000d) { AutoReset = true };
                _timer.Elapsed += (_, _) => _ = SweepAsync(token);
                _timer.Start();

                // The first sweep runs now rather than one interval from now, or the dots stay blank for a
                // minute after the feature is switched on and it looks broken.
                _ = SweepAsync(token);
            }
        }

        private void StopLocked()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void ClearStates()
        {
            foreach (var vm in Targets(includeInvisible: true))
                vm.SetReachability(EReachState.Unknown, 0, "");
        }

        private static IEnumerable<ProtocolBaseViewModel> Targets(bool includeInvisible)
        {
            var data = IoC.TryGet<GlobalData>();
            if (data == null) return Enumerable.Empty<ProtocolBaseViewModel>();
            return includeInvisible
                ? data.VmItemList.ToList()
                // Hidden rows are hidden: probing what the user filtered out is traffic nobody will look at,
                // and it is what keeps a sweep proportional to the screen rather than to the database.
                : data.VmItemList.Where(x => x.IsVisible).ToList();
        }

        private async Task SweepAsync(CancellationToken ct)
        {
            if (Interlocked.Exchange(ref _sweeping, 1) != 0) return;
            try
            {
                var targets = Targets(includeInvisible: false);
                using var gate = new SemaphoreSlim(MAX_CONCURRENT_PROBES);

                var probes = targets.Select(async vm =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var proxy = _proxyService.Find(vm.Server.ProxyName);
                        var result = await ServerProbe.ProbeAsync(vm.Server, proxy, PROBE_TIMEOUT_MS, ct).ConfigureAwait(false);
                        vm.SetReachability(result.State, result.LatencyMs, result.Reason);
                    }
                    finally
                    {
                        gate.Release();
                    }
                });

                await Task.WhenAll(probes).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // switched off or re-timed mid sweep
            }
            catch (Exception e)
            {
                SimpleLogHelper.Warning($"ServerReachabilityService: sweep failed, {e.Message}");
            }
            finally
            {
                Volatile.Write(ref _sweeping, 0);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                StopLocked();
            }
        }
    }
}
