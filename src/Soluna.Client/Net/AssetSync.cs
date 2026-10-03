using Soluna.Shared;

namespace Soluna.Client.Net;

/// <summary>
/// Keeps the game's files in step with the server: compares the manifest sent at login with what is
/// on disk, downloads what is missing or different a few at a time, and reports every file that
/// changed so the caches holding it can let go.
/// </summary>
internal sealed class AssetSync(Action<string> request)
{
    // How much is asked for at once: enough to keep the link busy, little enough not to queue
    // megabytes ahead of everything else the server sends.
    private const int MaxFilesInFlight = 64;
    private const long MaxBytesInFlight = 1024 * 1024;

    private readonly Dictionary<string, (long size, string hash)> _expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _queue = [];
    private readonly Dictionary<string, long> _asked = new(StringComparer.OrdinalIgnoreCase);
    private long _bytesAsked;
    private readonly List<string> _changed = [];

    /// <summary>Bytes still to download and how many there were in this round, for the progress bar.</summary>
    public long BytesLeft { get; private set; }

    public long BytesTotal { get; private set; }

    public bool Busy => _queue.Count > 0 || _asked.Count > 0;

    public void OnManifest(IEnumerable<(string path, long size, string hash)> entries)
    {
        _expected.Clear();
        _queue.Clear();
        _asked.Clear();
        _bytesAsked = 0;
        BytesLeft = BytesTotal = 0;
        foreach (var (path, size, hash) in entries) Want(path, size, hash);
        Pump();
    }

    /// <summary>A file added or replaced while playing. Even one already on disk (an upload from this machine) counts as changed.</summary>
    public void OnAdded(string path, long size, string hash)
    {
        if (!Want(path, size, hash)) _changed.Add(path);
        Pump();
    }

    public void OnData(string path, byte[] data)
    {
        if (!_asked.Remove(path, out var askedSize)) return;
        _bytesAsked -= askedSize;
        BytesLeft = Math.Max(0, BytesLeft - data.Length);
        if (_expected.TryGetValue(path, out var entry) && AssetFiles.Hash(data) == entry.hash && AssetFiles.Resolve(path) is { } full)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full + ".tmp", data);
            File.Move(full + ".tmp", full, overwrite: true);
            _changed.Add(path);
        }
        else
        {
            Console.WriteLine($"Asset '{path}' arrived damaged or unexpected; ignored.");
        }
        Pump();
    }

    /// <summary>Files written or replaced since the last call; empty while a round is still downloading.</summary>
    public List<string> TakeChanged()
    {
        if (Busy || _changed.Count == 0) return [];
        var changed = _changed.ToList();
        _changed.Clear();
        return changed;
    }

    private bool Want(string path, long size, string hash)
    {
        if (!AssetFiles.IsDistributed(path) || AssetFiles.Resolve(path) is not { } full) return false;
        _expected[path] = (size, hash);
        if (File.Exists(full) && new FileInfo(full).Length == size && AssetFiles.Hash(full) == hash) return false;
        _queue.Enqueue(path);
        BytesLeft += size;
        BytesTotal += size;
        return true;
    }

    private void Pump()
    {
        // Always at least one, so a file bigger than the byte budget still comes.
        while (_queue.Count > 0 && (_asked.Count == 0 || _asked.Count < MaxFilesInFlight && _bytesAsked < MaxBytesInFlight))
        {
            var path = _queue.Dequeue();
            var size = _expected[path].size;
            _asked[path] = size;
            _bytesAsked += size;
            request(path);
        }
        if (!Busy) BytesTotal = BytesLeft = 0;
    }
}
