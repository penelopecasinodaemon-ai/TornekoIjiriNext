using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TornekoIjiriNext;

internal sealed record PineAssetMetadata(
    string GameId,
    string GameCrc,
    string GameVersion,
    uint Address,
    int Length,
    DateTimeOffset CapturedAt,
    long ConnectionGeneration);

internal sealed record ProjectBundleEntry(
    string FileName,
    string Category,
    long Length,
    string Sha256,
    DateTimeOffset AddedUtc,
    string? ParentFileName = null,
    string? ParentSha256 = null,
    PineAssetMetadata? PineSource = null);
internal sealed record ProjectAssetByteChange(int Offset, byte? Before, byte? After);
internal sealed record ProjectAssetDiff(int BeforeLength, int AfterLength, int TotalChangedBytes, IReadOnlyList<ProjectAssetByteChange> Changes, bool IsTruncated);

/// <summary>Offline project container for legacy source files; never connects to PINE or game memory.</summary>
internal sealed class ProjectBundleModel
{
    private const int CurrentVersion = 1;
    private const long MaximumAssetLength = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly List<ProjectBundleEntry> _entries;

    private ProjectBundleModel(string manifestPath, string name, string targetGameId, List<ProjectBundleEntry> entries, DateTimeOffset createdUtc)
    {
        ManifestPath = Path.GetFullPath(manifestPath);
        Name = name;
        TargetGameId = targetGameId;
        _entries = entries;
        CreatedUtc = createdUtc;
    }

    public string ManifestPath { get; }
    public string RootDirectory => Path.GetDirectoryName(ManifestPath)!;
    public string Name { get; private set; }
    public string TargetGameId { get; private set; }
    public DateTimeOffset CreatedUtc { get; }
    public IReadOnlyList<ProjectBundleEntry> Entries => _entries.AsReadOnly();

    public static ProjectBundleModel Create(string manifestPath, string name, string targetGameId = "SLPM-62199")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Project name is required.", nameof(name));
        var fullPath = Path.GetFullPath(manifestPath);
        if (File.Exists(fullPath)) throw new IOException("A project manifest already exists at that path.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(fullPath)!, "assets"));
        var model = new ProjectBundleModel(fullPath, name.Trim(), targetGameId.Trim(), new List<ProjectBundleEntry>(), DateTimeOffset.UtcNow);
        model.Save();
        return model;
    }

    public static ProjectBundleModel Open(string manifestPath)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Project manifest is missing or too large.");
        var dto = JsonSerializer.Deserialize<ProjectBundleDto>(File.ReadAllBytes(fullPath)) ?? throw new InvalidDataException("Project manifest is empty.");
        if (dto.FormatVersion != CurrentVersion) throw new InvalidDataException($"Unsupported project format version: {dto.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.TargetGameId) || dto.CreatedUtc == default || dto.Entries is null)
            throw new InvalidDataException("Project manifest is incomplete.");
        foreach (var entry in dto.Entries)
        {
            if (entry is null) throw new InvalidDataException("Project manifest contains an empty asset entry.");
            ValidateFileName(entry.FileName);
            if (string.IsNullOrWhiteSpace(entry.Category) || entry.Length < 0 || entry.Length > MaximumAssetLength || string.IsNullOrWhiteSpace(entry.Sha256) || entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"Invalid manifest entry: {entry.FileName}.");
            if ((entry.ParentFileName is null) != (entry.ParentSha256 is null)) throw new InvalidDataException($"Incomplete parent reference: {entry.FileName}.");
            if (entry.ParentFileName is not null)
            {
                ValidateFileName(entry.ParentFileName);
                if (entry.ParentSha256!.Length != 64 || !entry.ParentSha256.All(Uri.IsHexDigit)) throw new InvalidDataException($"Invalid parent hash: {entry.FileName}.");
            }
        }
        if (dto.Entries.Select(e => e.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dto.Entries.Count)
            throw new InvalidDataException("Project manifest contains duplicate filenames.");
        foreach (var entry in dto.Entries.Where(e => e.ParentFileName is not null))
        {
            var parent = dto.Entries.FirstOrDefault(e => string.Equals(e.FileName, entry.ParentFileName, StringComparison.OrdinalIgnoreCase));
            if (parent is null || !string.Equals(parent.Sha256, entry.ParentSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Missing or mismatched parent reference: {entry.FileName}.");
        }
        var entriesByName = dto.Entries.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        var visitState = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        bool HasParentCycle(ProjectBundleEntry entry)
        {
            if (visitState.TryGetValue(entry.FileName, out var state)) return state == 1;
            visitState[entry.FileName] = 1;
            if (entry.ParentFileName is not null && HasParentCycle(entriesByName[entry.ParentFileName])) return true;
            visitState[entry.FileName] = 2;
            return false;
        }
        if (dto.Entries.Any(HasParentCycle)) throw new InvalidDataException("Project manifest contains a cyclic asset history.");
        return new ProjectBundleModel(fullPath, dto.Name, dto.TargetGameId, dto.Entries, dto.CreatedUtc);
    }

    public void AddFiles(IEnumerable<(string SourcePath, string Category)> files)
    {
        var added = new List<ProjectBundleEntry>();
        var destinations = new List<string>();
        var manifestUpdated = false;
        try
        {
            foreach (var (sourcePath, category) in files)
            {
                if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Asset category is required.", nameof(files));
                var source = Path.GetFullPath(sourcePath);
                var sourceInfo = new FileInfo(source);
                if (!sourceInfo.Exists) throw new FileNotFoundException("Source asset was not found.", source);
                if (sourceInfo.Length > MaximumAssetLength) throw new InvalidDataException($"Asset exceeds 64 MiB: {sourceInfo.Name}");
                var fileName = MakeUniqueName(Path.GetFileName(source));
                var assetDirectory = Path.Combine(RootDirectory, "assets");
                Directory.CreateDirectory(assetDirectory);
                var finalPath = Path.Combine(assetDirectory, fileName);
                var tempPath = finalPath + ".importing";
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                var digest = HashFile(tempPath);
                File.Move(tempPath, finalPath);
                destinations.Add(finalPath);
                added.Add(new ProjectBundleEntry(fileName, category.Trim(), sourceInfo.Length, digest, DateTimeOffset.UtcNow));
            }
            _entries.AddRange(added);
            manifestUpdated = true;
            Save();
        }
        catch
        {
            if (manifestUpdated) _entries.RemoveRange(_entries.Count - added.Count, added.Count);
            foreach (var path in destinations) if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public string AddAssetBytes(string fileName, string category, ReadOnlySpan<byte> content, string? parentFileName = null, string? parentSha256 = null, PineAssetMetadata? pineSource = null)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Asset category is required.", nameof(category));
        if (content.Length > MaximumAssetLength) throw new InvalidDataException("Asset exceeds 64 MiB.");
        if ((parentFileName is null) != (parentSha256 is null)) throw new ArgumentException("Parent file and parent hash must be supplied together.");
        if (parentFileName is not null)
        {
            ValidateFileName(parentFileName);
            var parent = _entries.FirstOrDefault(e => string.Equals(e.FileName, parentFileName, StringComparison.OrdinalIgnoreCase));
            if (parent is null || !string.Equals(parent.Sha256, parentSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Parent asset is missing or its hash has changed.");
        }
        Directory.CreateDirectory(Path.Combine(RootDirectory, "assets"));
        var uniqueName = MakeUniqueName(fileName);
        var finalPath = Path.Combine(RootDirectory, "assets", uniqueName);
        var tempPath = finalPath + ".importing";
        var moved = false;
        File.WriteAllBytes(tempPath, content.ToArray());
        try
        {
            var entry = new ProjectBundleEntry(uniqueName, category.Trim(), content.Length, HashFile(tempPath), DateTimeOffset.UtcNow, parentFileName, parentSha256, pineSource);
            File.Move(tempPath, finalPath);
            moved = true;
            _entries.Add(entry);
            try { Save(); }
            catch { _entries.RemoveAt(_entries.Count - 1); throw; }
            return uniqueName;
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (moved && File.Exists(finalPath) && !_entries.Any(e => e.FileName == uniqueName)) File.Delete(finalPath);
            throw;
        }
    }

    public IReadOnlyList<string> VerifyAssets()
    {
        var failures = new List<string>();
        foreach (var entry in _entries)
        {
            var path = Path.Combine(RootDirectory, "assets", entry.FileName);
            if (!File.Exists(path)) { failures.Add($"Missing: {entry.FileName}"); continue; }
            var info = new FileInfo(path);
            if (info.Length != entry.Length || !string.Equals(HashFile(path), entry.Sha256, StringComparison.OrdinalIgnoreCase)) failures.Add($"Changed: {entry.FileName}");
        }
        return failures;
    }

    public IReadOnlyList<ProjectBundleEntry> GetAssetHistory(string fileName)
    {
        var selected = FindEntry(fileName);
        var byName = _entries.ToDictionary(entry => entry.FileName, StringComparer.OrdinalIgnoreCase);
        var root = selected;
        while (root.ParentFileName is not null) root = byName[root.ParentFileName];
        var history = new List<ProjectBundleEntry>();
        var queue = new Queue<ProjectBundleEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.FileName)) continue;
            history.Add(current);
            foreach (var child in _entries.Where(entry => string.Equals(entry.ParentFileName, current.FileName, StringComparison.OrdinalIgnoreCase) && string.Equals(entry.ParentSha256, current.Sha256, StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.AddedUtc))
                queue.Enqueue(child);
        }
        return history;
    }

    public IReadOnlyList<ProjectBundleEntry> GetPineCaptureHistory(PineAssetMetadata source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return _entries
            .Where(entry =>
            {
                var pine = entry.PineSource;
                return pine is not null
                    && string.Equals(pine.GameId, source.GameId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pine.GameCrc, source.GameCrc, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pine.GameVersion, source.GameVersion, StringComparison.OrdinalIgnoreCase)
                    && pine.Address == source.Address
                    && pine.Length == source.Length;
            })
            .OrderBy(entry => entry.PineSource!.CapturedAt)
            .ThenBy(entry => entry.AddedUtc)
            .ToList()
            .AsReadOnly();
    }
    public ProjectAssetDiff CompareAssets(string beforeFileName, string afterFileName, int maximumDisplayedChanges = 2000)
    {
        if (maximumDisplayedChanges is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maximumDisplayedChanges));
        var before = ReadVerifiedAsset(FindEntry(beforeFileName));
        var after = ReadVerifiedAsset(FindEntry(afterFileName));
        var totalLength = Math.Max(before.Length, after.Length);
        var changes = new List<ProjectAssetByteChange>(Math.Min(totalLength, maximumDisplayedChanges));
        var totalChanges = 0;
        for (var offset = 0; offset < totalLength; offset++)
        {
            byte? left = offset < before.Length ? before[offset] : null;
            byte? right = offset < after.Length ? after[offset] : null;
            if (left == right) continue;
            totalChanges++;
            if (changes.Count < maximumDisplayedChanges) changes.Add(new ProjectAssetByteChange(offset, left, right));
        }
        return new ProjectAssetDiff(before.Length, after.Length, totalChanges, changes, totalChanges > changes.Count);
    }

    private ProjectBundleEntry FindEntry(string fileName) => _entries.FirstOrDefault(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Project asset not found: {fileName}");

    private byte[] ReadVerifiedAsset(ProjectBundleEntry entry)
    {
        var path = Path.Combine(RootDirectory, "assets", entry.FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"Project asset is missing: {entry.FileName}");
        var info = new FileInfo(path);
        if (info.Length != entry.Length || !string.Equals(HashFile(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Project asset failed integrity verification: {entry.FileName}");
        return File.ReadAllBytes(path);
    }

    private string MakeUniqueName(string original)
    {
        ValidateFileName(original);
        var used = _entries.Select(e => e.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = original;
        var extension = Path.GetExtension(original);
        var stem = Path.GetFileNameWithoutExtension(original);
        for (var index = 2; used.Contains(candidate) || File.Exists(Path.Combine(RootDirectory, "assets", candidate)); index++)
            candidate = $"{stem}_{index}{extension}";
        return candidate;
    }

    private static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Asset file name must be a plain file name without path components.");
    }

    private void Save()
    {
        var dto = new ProjectBundleDto(CurrentVersion, Name, TargetGameId, CreatedUtc, _entries.ToList());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        var temp = ManifestPath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, ManifestPath, true);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ProjectBundleDto(int FormatVersion, string Name, string TargetGameId, DateTimeOffset CreatedUtc, List<ProjectBundleEntry>? Entries);
}
