using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ResourceRouter.App.State;
using ResourceRouter.Core.Models;

namespace ResourceRouter.App.Services;

internal enum DropIngressChannel
{
    Wpf,
    Com
}

internal sealed class DropIngressCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Time, DropIngressChannel Channel)> _recentFingerprints =
        new(StringComparer.Ordinal);

    private readonly TimeSpan _dedupWindow;
    private readonly TimeSpan _wpfDelayWhenComEnabled;

    public DropIngressCoordinator(TimeSpan? dedupWindow = null, TimeSpan? wpfDelayWhenComEnabled = null)
    {
        _dedupWindow = dedupWindow ?? AppInteractionDefaults.DropIngress.DedupWindow;
        _wpfDelayWhenComEnabled = wpfDelayWhenComEnabled ?? AppInteractionDefaults.DropIngress.WpfDelayWhenComEnabled;
    }

    public async Task<bool> ShouldProcessAsync(
        IReadOnlyList<RawDropData> drops,
        DropIngressChannel channel,
        bool comEnabled,
        CancellationToken cancellationToken = default)
    {
        if (drops.Count == 0)
        {
            return false;
        }

        var fingerprint = BuildFingerprint(drops);

        if (comEnabled && channel == DropIngressChannel.Wpf)
        {
            await Task.Delay(_wpfDelayWhenComEnabled, cancellationToken).ConfigureAwait(false);
            if (HasRecentCom(fingerprint))
            {
                return false;
            }
        }

        lock (_gate)
        {
            PruneExpired(DateTimeOffset.UtcNow);

            if (_recentFingerprints.TryGetValue(fingerprint, out var recent))
            {
                if (DateTimeOffset.UtcNow - recent.Time <= _dedupWindow)
                {
                    return false;
                }
            }

            _recentFingerprints[fingerprint] = (DateTimeOffset.UtcNow, channel);
            return true;
        }
    }

    private bool HasRecentCom(string fingerprint)
    {
        lock (_gate)
        {
            PruneExpired(DateTimeOffset.UtcNow);

            if (_recentFingerprints.TryGetValue(fingerprint, out var recent) &&
                recent.Channel == DropIngressChannel.Com &&
                DateTimeOffset.UtcNow - recent.Time <= _dedupWindow)
            {
                return true;
            }

            return false;
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        if (_recentFingerprints.Count == 0)
        {
            return;
        }

        var expired = _recentFingerprints
            .Where(entry => now - entry.Value.Time > _dedupWindow)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in expired)
        {
            _recentFingerprints.Remove(key);
        }
    }

    private static string BuildFingerprint(IReadOnlyList<RawDropData> drops)
    {
        var ordered = drops
            .Select(drop =>
            {
                var path = drop.FilePaths.Count > 0 ? drop.FilePaths[0] : string.Empty;
                var textHint = string.IsNullOrWhiteSpace(drop.Text) ? string.Empty : drop.Text[..Math.Min(48, drop.Text.Length)];
                var urlHint = drop.Url ?? string.Empty;
                return $"{drop.Kind}|{path}|{urlHint}|{textHint}";
            })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var raw = string.Join("||", ordered);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
