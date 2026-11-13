using System;
using System.Collections.Generic;
using System.Linq;

namespace TornekoIjiriNext;

public sealed class PineCachedRegion
{
    private readonly byte[] _bytes;

    public uint Address { get; }
    public int Length => _bytes.Length;

    public PineCachedRegion(uint address, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < 1 || bytes.Length > 4096)
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                "PINE region length must be between 1 and 4096 bytes.");

        if ((ulong)address + (ulong)bytes.Length > (ulong)uint.MaxValue + 1UL)
            throw new ArgumentOutOfRangeException(nameof(address));

        Address = address;
        _bytes = (byte[])bytes.Clone();
    }

    public byte[] CopyBytes()
    {
        return (byte[])_bytes.Clone();
    }
}

public sealed class PineSessionSnapshot
{
    private readonly PineCachedRegion[] _regions;

    public long ConnectionGeneration { get; }
    public string GameId { get; }
    public string GameCrc { get; }
    public string GameVersion { get; }
    public int StatusBefore { get; }
    public int StatusAfter { get; }
    public DateTimeOffset CapturedAt { get; }

    public IReadOnlyList<PineCachedRegion> Regions =>
        Array.AsReadOnly(_regions);

    internal PineSessionSnapshot(
        long connectionGeneration,
        string gameId,
        string gameCrc,
        string gameVersion,
        int statusBefore,
        int statusAfter,
        IEnumerable<PineCachedRegion> regions)
    {
        ConnectionGeneration = connectionGeneration;
        GameId = gameId;
        GameCrc = gameCrc;
        GameVersion = gameVersion;
        StatusBefore = statusBefore;
        StatusAfter = statusAfter;
        CapturedAt = DateTimeOffset.UtcNow;

        _regions = regions
            .Select(x => new PineCachedRegion(x.Address, x.CopyBytes()))
            .ToArray();
    }

    public bool TryGetRegion(uint address, out byte[] bytes)
    {
        var region = _regions.FirstOrDefault(x => x.Address == address);

        if (region is null)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = region.CopyBytes();
        return true;
    }
}

public sealed class PineSessionSnapshotCache
{
    private readonly object _sync = new();
    private PineSessionSnapshot? _current;

    public bool HasSnapshot
    {
        get
        {
            lock (_sync)
                return _current is not null;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
            _current = null;
    }

    public bool TryPublish(
        long connectionGeneration,
        string gameId,
        string gameCrc,
        string gameVersion,
        int statusBefore,
        int statusAfter,
        IEnumerable<PineCachedRegion> regions,
        out string reason)
    {
        reason = string.Empty;

        if (connectionGeneration < 0)
        {
            reason = "Invalid connection generation.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(gameId) ||
            string.IsNullOrWhiteSpace(gameCrc) ||
            string.IsNullOrWhiteSpace(gameVersion))
        {
            reason = "Game identity is incomplete.";
            return false;
        }

        if (statusBefore != statusAfter)
        {
            reason = "PINE status changed during capture.";
            return false;
        }

        if (regions is null)
        {
            reason = "Capture regions are missing.";
            return false;
        }

        PineCachedRegion[] copiedRegions;

        try
        {
            copiedRegions = regions
                .Select(x => new PineCachedRegion(x.Address, x.CopyBytes()))
                .ToArray();
        }
        catch (Exception ex)
        {
            reason = "Invalid capture region: " + ex.Message;
            return false;
        }

        if (copiedRegions.Length == 0)
        {
            reason = "Capture contains no regions.";
            return false;
        }

        if (copiedRegions.Select(x => x.Address).Distinct().Count()
            != copiedRegions.Length)
        {
            reason = "Capture contains duplicate region addresses.";
            return false;
        }

        var snapshot = new PineSessionSnapshot(
            connectionGeneration,
            gameId,
            gameCrc,
            gameVersion,
            statusBefore,
            statusAfter,
            copiedRegions);

        lock (_sync)
            _current = snapshot;

        return true;
    }

    public bool TryGetSnapshot(
        long connectionGeneration,
        string gameId,
        string gameCrc,
        string gameVersion,
        out PineSessionSnapshot? snapshot)
    {
        lock (_sync)
        {
            if (_current is null ||
                _current.ConnectionGeneration != connectionGeneration ||
                !string.Equals(_current.GameId, gameId, StringComparison.Ordinal) ||
                !string.Equals(_current.GameCrc, gameCrc, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_current.GameVersion, gameVersion, StringComparison.Ordinal))
            {
                snapshot = null;
                return false;
            }

            snapshot = new PineSessionSnapshot(
                _current.ConnectionGeneration,
                _current.GameId,
                _current.GameCrc,
                _current.GameVersion,
                _current.StatusBefore,
                _current.StatusAfter,
                _current.Regions);

            return true;
        }
    }
}