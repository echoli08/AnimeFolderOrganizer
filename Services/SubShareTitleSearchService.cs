using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 標題索引與關鍵字搜尋服務。
/// 使用 XmlReader 串流解析 db.xml，並快取在記憶體中以避免重複解析。
/// </summary>
public sealed class SubShareTitleSearchService : ISubShareTitleSearchService
{
    private const string DbFileName = "db.xml";
    private const string RepoRootPrefix = "subs_list/";

    private readonly ISubShareDbService _dbService;
    private readonly ITextConverter _textConverter;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private volatile string? _loadedFingerprint;
    private string? _lastLoadError;
    private int _lastSubsElementCount;
    private int _lastParsedCount;
    private long _lastFileSize;
    private int _lastRawSubsTagCount;
    private string _lastDbPath = string.Empty;
    private List<SubShareTitleMatch> _matches = [];

    // 針對 LIKE(包含) 行為的粗略倒排索引：使用 2-gram（連續兩個字元）做候選集縮小。
    private Dictionary<uint, List<int>> _bigramIndex = new();

    // 快速 exact 對應：任何語系標題正規化後 -> 最佳索引（以 Time 較新者優先）。
    private Dictionary<string, int> _bestByNormalizedTitle = new(StringComparer.Ordinal);

    // 正規化後的五語系標題，與 _matches 同步索引。
    private string[] _nChs = [];
    private string[] _nCht = [];
    private string[] _nJp = [];
    private string[] _nEn = [];
    private string[] _nRome = [];

    public SubShareTitleSearchService(ISubShareDbService dbService, ITextConverter textConverter)
    {
        _dbService = dbService;
        _textConverter = textConverter;
    }

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        var status = await _dbService.GetStatusAsync().ConfigureAwait(false);
        if (!status.Exists)
        {
            PublishEmpty("missing");
            return;
        }

        var dbPath = GetDefaultDbFilePath();
        _lastDbPath = dbPath;

        FileInfo info;
        try
        {
            info = new FileInfo(dbPath);
            if (!info.Exists)
            {
                PublishEmpty("missing");
                return;
            }
        }
        catch
        {
            // 檔案資訊取得失敗時，不中斷呼叫端，直接視為無資料。
            PublishEmpty("missing");
            return;
        }

        _lastFileSize = info.Length;
        _lastRawSubsTagCount = await CountSubsTagsAsync(dbPath, ct).ConfigureAwait(false);

        var fingerprint = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        if (string.Equals(_loadedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            // 若 raw subs 很多但快取只有 1 筆，強制重載避免卡在錯誤快取
            if (_lastRawSubsTagCount <= 1 || _matches.Count > 1)
            {
                return;
            }
        }

        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.Equals(_loadedFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            var loaded = await TryLoadAsync(dbPath, ct, (subsCount, parsedCount, error) =>
            {
                _lastSubsElementCount = subsCount;
                _lastParsedCount = parsedCount;
                _lastLoadError = error;
            }).ConfigureAwait(false);
            if (loaded is null)
            {
                // 解析失敗：保留舊快取，允許下次重試。
                _loadedFingerprint = null;
                return;
            }

            if (_lastRawSubsTagCount > 1 && loaded.Matches.Count <= 1)
            {
                var fallback = await TryLoadWithXDocument(dbPath, ct).ConfigureAwait(false);
                if (fallback != null && fallback.Matches.Count > loaded.Matches.Count)
                {
                    loaded = fallback;
                    _lastSubsElementCount = loaded.Matches.Count;
                    _lastParsedCount = loaded.Matches.Count;
                    _lastLoadError = string.Empty;
                }
            }

            _matches = loaded.Matches;
            _nChs = loaded.NChs;
            _nCht = loaded.NCht;
            _nJp = loaded.NJp;
            _nEn = loaded.NEn;
            _nRome = loaded.NRome;
            _bigramIndex = loaded.BigramIndex;
            _bestByNormalizedTitle = loaded.BestByNormalizedTitle;
            _loadedFingerprint = fingerprint;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public async Task<SubShareSearchDiagnostics> GetDiagnosticsAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return new SubShareSearchDiagnostics(
            DbPath: _lastDbPath,
            RecordCount: _matches.Count,
            SubsElementCount: _lastSubsElementCount,
            ParsedCount: _lastParsedCount,
            FileSize: _lastFileSize,
            RawSubsTagCount: _lastRawSubsTagCount,
            Fingerprint: _loadedFingerprint ?? string.Empty,
            LastError: _lastLoadError ?? string.Empty);
    }

    public async Task<IReadOnlyList<SubShareTitleMatch>> SearchAsync(string keyword, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return Array.Empty<SubShareTitleMatch>();
        }

        keyword = keyword?.Trim() ?? string.Empty;
        if (keyword.Length == 0)
        {
            return Array.Empty<SubShareTitleMatch>();
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        // 避免在 UI Thread 做大量 CPU 掃描。
        return await Task.Run(() => SearchCore(keyword, limit, ct), ct).ConfigureAwait(false);
    }

    public async Task<SubShareTitleMatch?> FindBestMatchAsync(string title, CancellationToken ct)
    {
        title = title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return null;
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        // 避免在 UI Thread 做大量 CPU 掃描。
        return await Task.Run(() => FindBestMatchCore(title, ct), ct).ConfigureAwait(false);
    }

    private SubShareTitleMatch? FindBestMatchCore(string title, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalized = Normalize(title);
        if (normalized.Length == 0)
        {
            return null;
        }

        if (_bestByNormalizedTitle.TryGetValue(normalized, out var idx))
        {
            return (uint)idx < (uint)_matches.Count ? _matches[idx] : null;
        }

        // 沒有 exact 的話，用搜尋退一步。
        var list = SearchCore(title, 1, ct);
        return list.Count > 0 ? list[0] : null;
    }

    private IReadOnlyList<SubShareTitleMatch> SearchCore(string keyword, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var variants = BuildKeywordVariants(keyword);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SubShareTitleMatch>(Math.Min(limit, 32));

        foreach (var variant in variants)
        {
            ct.ThrowIfCancellationRequested();

            var k = Normalize(variant);
            if (k.Length == 0)
            {
                continue;
            }

            if (k.Length >= 2 && _bigramIndex.Count > 0)
            {
                SearchWithBigramIndex(k, limit, seenKeys, results, ct);
            }
            else
            {
                LinearScan(k, limit, seenKeys, results, ct);
            }

            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private void SearchWithBigramIndex(
        string keywordNormalized,
        int limit,
        HashSet<string> seenKeys,
        List<SubShareTitleMatch> results,
        CancellationToken ct)
    {
        var grams = new HashSet<uint>();
        AddDistinctBigrams(keywordNormalized, grams);
        if (grams.Count == 0)
        {
            LinearScan(keywordNormalized, limit, seenKeys, results, ct);
            return;
        }

        // 交集：透過 count 累計，最後挑出 count == grams.Count 的索引。
        var counts = new Dictionary<int, int>(capacity: 1024);
        foreach (var gram in grams)
        {
            ct.ThrowIfCancellationRequested();

            if (!_bigramIndex.TryGetValue(gram, out var list))
            {
                return;
            }

            foreach (var idx in list)
            {
                counts.TryGetValue(idx, out var c);
                counts[idx] = c + 1;
            }
        }

        var required = grams.Count;
        foreach (var pair in counts)
        {
            ct.ThrowIfCancellationRequested();

            if (pair.Value != required)
            {
                continue;
            }

            var idx = pair.Key;
            if ((uint)idx >= (uint)_matches.Count)
            {
                continue;
            }

            if (!IsMatch(idx, keywordNormalized))
            {
                continue;
            }

            var m = _matches[idx];
            if (m.Key.Length == 0 || !seenKeys.Add(m.Key))
            {
                continue;
            }

            results.Add(m);
            if (results.Count >= limit)
            {
                return;
            }
        }
    }

    private void LinearScan(
        string keywordNormalized,
        int limit,
        HashSet<string> seenKeys,
        List<SubShareTitleMatch> results,
        CancellationToken ct)
    {
        for (var i = 0; i < _matches.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!IsMatch(i, keywordNormalized))
            {
                continue;
            }

            var m = _matches[i];
            if (m.Key.Length == 0 || !seenKeys.Add(m.Key))
            {
                continue;
            }

            results.Add(m);
            if (results.Count >= limit)
            {
                return;
            }
        }
    }

    private bool IsMatch(int idx, string keywordNormalized)
    {
        // 這裡採用正規化後（移除空白 + 小寫）做包含比對：
        // 1) 等同於 LIKE '%keyword%'
        // 2) 避免空白/大小寫造成匹配落差
        return _nChs[idx].Contains(keywordNormalized, StringComparison.Ordinal)
            || _nCht[idx].Contains(keywordNormalized, StringComparison.Ordinal)
            || _nJp[idx].Contains(keywordNormalized, StringComparison.Ordinal)
            || _nEn[idx].Contains(keywordNormalized, StringComparison.Ordinal)
            || _nRome[idx].Contains(keywordNormalized, StringComparison.Ordinal);
    }

    private List<string> BuildKeywordVariants(string keyword)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(keyword);

        if (ContainsChinese(keyword))
        {
            // 若包含中文，依 frm_Search 的行為補上簡/繁變體。
            var trad = _textConverter.ToTraditional(keyword);
            var simp = _textConverter.ToSimplified(keyword);

            if (!string.IsNullOrWhiteSpace(trad))
            {
                set.Add(trad!);
            }

            if (!string.IsNullOrWhiteSpace(simp))
            {
                set.Add(simp!);
            }
        }

        return set.ToList();
    }

    private static bool ContainsChinese(string text)
    {
        foreach (var ch in text)
        {
            // CJK Unified Ideographs + Extensions（足夠用於判斷是否「含中文」）
            if ((ch >= 0x4E00 && ch <= 0x9FFF)
                || (ch >= 0x3400 && ch <= 0x4DBF)
                || (ch >= 0xF900 && ch <= 0xFAFF))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Key / 搜尋的一致正規化：
        // - Trim
        // - 移除所有空白（包含全形空白/換行）
        // - 轉小寫
        var s = text.Trim();
        var sb = new StringBuilder(s.Length);

        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static string NormalizeRepoPath(string? recordPath)
    {
        if (string.IsNullOrWhiteSpace(recordPath))
        {
            return string.Empty;
        }

        // db.xml 的 path 來源可能混用 Windows/Unix 路徑分隔；這裡統一轉成 repo 相對路徑。
        var s = recordPath.Trim().Replace('\\', '/');
        s = s.TrimStart('/');

        if (s.Length == 0)
        {
            return string.Empty;
        }

        if (!s.StartsWith(RepoRootPrefix, StringComparison.Ordinal))
        {
            s = RepoRootPrefix + s;
        }

        return s;
    }

    private static void AddDistinctBigrams(string normalized, HashSet<uint> dest)
    {
        if (normalized.Length < 2)
        {
            return;
        }

        for (var i = 0; i < normalized.Length - 1; i++)
        {
            var a = normalized[i];
            var b = normalized[i + 1];
            var key = ((uint)a << 16) | b;
            dest.Add(key);
        }
    }

    private static string GetDefaultDbFilePath()
    {
        // 注意：此路徑需與 SubShareDbService 的存放位置一致。
        var baseDir = AppContext.BaseDirectory;
        var basePath = Path.Combine(baseDir, "subshare", DbFileName);
        if (File.Exists(basePath))
        {
            return basePath;
        }

        // 相容舊版路徑（AppData）
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyPath = Path.Combine(appData, "AnimeFolderOrganizer", "sub_share", DbFileName);
        return legacyPath;
    }

    private void PublishEmpty(string fingerprint)
    {
        _matches = [];
        _nChs = [];
        _nCht = [];
        _nJp = [];
        _nEn = [];
        _nRome = [];
        _bigramIndex = new Dictionary<uint, List<int>>();
        _bestByNormalizedTitle = new Dictionary<string, int>(StringComparer.Ordinal);
        _loadedFingerprint = fingerprint;
    }

    private sealed record LoadedData(
        List<SubShareTitleMatch> Matches,
        string[] NChs,
        string[] NCht,
        string[] NJp,
        string[] NEn,
        string[] NRome,
        Dictionary<uint, List<int>> BigramIndex,
        Dictionary<string, int> BestByNormalizedTitle);

    private static async Task<LoadedData?> TryLoadAsync(string dbPath, CancellationToken ct, Action<int, int, string?>? onLoaded)
    {
        try
        {
            await using var stream = new FileStream(
                dbPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var settings = new XmlReaderSettings
            {
                Async = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore
            };

            using var reader = XmlReader.Create(stream, settings);

            var matches = new List<SubShareTitleMatch>(capacity: 4096);
            var nChs = new List<string>(capacity: 4096);
            var nCht = new List<string>(capacity: 4096);
            var nJp = new List<string>(capacity: 4096);
            var nEn = new List<string>(capacity: 4096);
            var nRome = new List<string>(capacity: 4096);

            var bigramIndex = new Dictionary<uint, List<int>>();
            var bestByNormalizedTitle = new Dictionary<string, int>(StringComparer.Ordinal);

            var recordBigrams = new HashSet<uint>();

            var subsElementCount = 0;
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (!string.Equals(reader.LocalName, "subs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                subsElementCount++;

                var record = await ReadSubsAsync(reader, ct).ConfigureAwait(false);
                if (record is null)
                {
                    continue;
                }

                var type = string.IsNullOrWhiteSpace(record.Type) ? null : record.Type.Trim();
                var titleJp = record.NameJp ?? string.Empty;
                var key = $"{Normalize(titleJp)}_{type ?? string.Empty}";

                var time = record.Time == default ? (DateTimeOffset?)null : record.Time;

                var m = new SubShareTitleMatch
                {
                    Key = key,
                    TitleChs = record.NameChs ?? string.Empty,
                    TitleCht = record.NameCht ?? string.Empty,
                    TitleJp = titleJp,
                    TitleEn = record.NameEn ?? string.Empty,
                    TitleRome = record.NameRome ?? string.Empty,
                    Type = type,
                    Time = time,
                    RepoPath = NormalizeRepoPath(record.Path)
                };

                var idx = matches.Count;
                matches.Add(m);

                var a = Normalize(m.TitleChs);
                var b = Normalize(m.TitleCht);
                var c = Normalize(m.TitleJp);
                var d = Normalize(m.TitleEn);
                var e = Normalize(m.TitleRome);
                nChs.Add(a);
                nCht.Add(b);
                nJp.Add(c);
                nEn.Add(d);
                nRome.Add(e);

                // 建立 2-gram 索引（用 record-level 去重，降低記憶體成長）。
                recordBigrams.Clear();
                AddDistinctBigrams(a, recordBigrams);
                AddDistinctBigrams(b, recordBigrams);
                AddDistinctBigrams(c, recordBigrams);
                AddDistinctBigrams(d, recordBigrams);
                AddDistinctBigrams(e, recordBigrams);

                foreach (var gram in recordBigrams)
                {
                    if (!bigramIndex.TryGetValue(gram, out var list))
                    {
                        list = [];
                        bigramIndex.Add(gram, list);
                    }

                    list.Add(idx);
                }

                // 建立 exact 對應：任何語系標題正規化後 -> 時間較新者優先。
                UpdateBest(bestByNormalizedTitle, a, idx, matches);
                UpdateBest(bestByNormalizedTitle, b, idx, matches);
                UpdateBest(bestByNormalizedTitle, c, idx, matches);
                UpdateBest(bestByNormalizedTitle, d, idx, matches);
                UpdateBest(bestByNormalizedTitle, e, idx, matches);
            }

            onLoaded?.Invoke(subsElementCount, matches.Count, null);
            return new LoadedData(
                matches,
                nChs.ToArray(),
                nCht.ToArray(),
                nJp.ToArray(),
                nEn.ToArray(),
                nRome.ToArray(),
                bigramIndex,
                bestByNormalizedTitle);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            onLoaded?.Invoke(0, 0, ex.Message);
            return null;
        }
    }

    private static async Task<LoadedData?> TryLoadWithXDocument(string dbPath, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                dbPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);
            var root = doc.Root;
            if (root == null)
            {
                return null;
            }

            var matches = new List<SubShareTitleMatch>(capacity: 4096);
            var nChs = new List<string>(capacity: 4096);
            var nCht = new List<string>(capacity: 4096);
            var nJp = new List<string>(capacity: 4096);
            var nEn = new List<string>(capacity: 4096);
            var nRome = new List<string>(capacity: 4096);
            var bigramIndex = new Dictionary<uint, List<int>>();
            var bestByNormalizedTitle = new Dictionary<string, int>(StringComparer.Ordinal);
            var recordBigrams = new HashSet<uint>();

            foreach (var node in root.Elements("subs"))
            {
                ct.ThrowIfCancellationRequested();

                var record = new SubShareRecord
                {
                    Time = ParseTime(node.Element("time")?.Value ?? node.Attribute("time")?.Value),
                    NameChs = node.Element("name_chs")?.Value ?? node.Attribute("name_chs")?.Value ?? string.Empty,
                    NameCht = node.Element("name_cht")?.Value ?? node.Attribute("name_cht")?.Value ?? string.Empty,
                    NameJp = node.Element("name_jp")?.Value ?? node.Attribute("name_jp")?.Value ?? string.Empty,
                    NameEn = node.Element("name_en")?.Value ?? node.Attribute("name_en")?.Value ?? string.Empty,
                    NameRome = node.Element("name_rome")?.Value ?? node.Attribute("name_rome")?.Value ?? string.Empty,
                    Type = node.Element("type")?.Value ?? node.Attribute("type")?.Value ?? string.Empty,
                    Source = node.Element("source")?.Value ?? node.Attribute("source")?.Value ?? string.Empty,
                    SubName = node.Element("sub_name")?.Value ?? node.Attribute("sub_name")?.Value ?? string.Empty,
                    Extension = node.Element("extension")?.Value ?? node.Attribute("extension")?.Value ?? string.Empty,
                    Providers = node.Element("providers")?.Value ?? node.Attribute("providers")?.Value ?? string.Empty,
                    Desc = node.Element("desc")?.Value ?? node.Attribute("desc")?.Value ?? string.Empty,
                    Path = node.Element("path")?.Value ?? node.Attribute("path")?.Value ?? string.Empty
                };

                var type = string.IsNullOrWhiteSpace(record.Type) ? null : record.Type.Trim();
                var titleJp = record.NameJp ?? string.Empty;
                var key = $"{Normalize(titleJp)}_{type ?? string.Empty}";
                var time = record.Time == default ? (DateTimeOffset?)null : record.Time;

                var m = new SubShareTitleMatch
                {
                    Key = key,
                    TitleChs = record.NameChs ?? string.Empty,
                    TitleCht = record.NameCht ?? string.Empty,
                    TitleJp = titleJp,
                    TitleEn = record.NameEn ?? string.Empty,
                    TitleRome = record.NameRome ?? string.Empty,
                    Type = type,
                    Time = time,
                    RepoPath = NormalizeRepoPath(record.Path)
                };

                var idx = matches.Count;
                matches.Add(m);

                var a = Normalize(m.TitleChs);
                var b = Normalize(m.TitleCht);
                var c = Normalize(m.TitleJp);
                var d = Normalize(m.TitleEn);
                var e = Normalize(m.TitleRome);
                nChs.Add(a);
                nCht.Add(b);
                nJp.Add(c);
                nEn.Add(d);
                nRome.Add(e);

                recordBigrams.Clear();
                AddDistinctBigrams(a, recordBigrams);
                AddDistinctBigrams(b, recordBigrams);
                AddDistinctBigrams(c, recordBigrams);
                AddDistinctBigrams(d, recordBigrams);
                AddDistinctBigrams(e, recordBigrams);

                foreach (var gram in recordBigrams)
                {
                    if (!bigramIndex.TryGetValue(gram, out var list))
                    {
                        list = [];
                        bigramIndex.Add(gram, list);
                    }

                    list.Add(idx);
                }

                UpdateBest(bestByNormalizedTitle, a, idx, matches);
                UpdateBest(bestByNormalizedTitle, b, idx, matches);
                UpdateBest(bestByNormalizedTitle, c, idx, matches);
                UpdateBest(bestByNormalizedTitle, d, idx, matches);
                UpdateBest(bestByNormalizedTitle, e, idx, matches);
            }

            return new LoadedData(
                matches,
                nChs.ToArray(),
                nCht.ToArray(),
                nJp.ToArray(),
                nEn.ToArray(),
                nRome.ToArray(),
                bigramIndex,
                bestByNormalizedTitle);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> CountSubsTagsAsync(string dbPath, CancellationToken ct)
    {
        const int bufferSize = 64 * 1024;
        var count = 0;
        var needle = "<subs";

        await using var stream = new FileStream(
            dbPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: bufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: bufferSize, leaveOpen: true);

        char[] buffer = new char[bufferSize];
        var carry = string.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            var chunk = carry + new string(buffer, 0, read);
            var index = 0;
            while (true)
            {
                var found = chunk.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                count++;
                index = found + needle.Length;
            }

            carry = chunk.Length > needle.Length
                ? chunk[^needle.Length..]
                : chunk;
        }

        return count;
    }

    private static void UpdateBest(
        Dictionary<string, int> map,
        string normalizedTitle,
        int idx,
        List<SubShareTitleMatch> matches)
    {
        if (normalizedTitle.Length == 0)
        {
            return;
        }

        if (!map.TryGetValue(normalizedTitle, out var existing))
        {
            map[normalizedTitle] = idx;
            return;
        }

        var a = matches[existing].Time ?? DateTimeOffset.MinValue;
        var b = matches[idx].Time ?? DateTimeOffset.MinValue;
        if (b > a)
        {
            map[normalizedTitle] = idx;
        }
    }

    private static async Task<SubShareRecord?> ReadSubsAsync(XmlReader reader, CancellationToken ct)
    {
        // reader 位於 <subs> 開始標籤；需讀取其子節點。
        var timeText = reader.GetAttribute("time");
        var nameChs = reader.GetAttribute("name_chs");
        var nameCht = reader.GetAttribute("name_cht");
        var nameJp = reader.GetAttribute("name_jp");
        var nameEn = reader.GetAttribute("name_en");
        var nameRome = reader.GetAttribute("name_rome");
        var type = reader.GetAttribute("type");
        var source = reader.GetAttribute("source");
        var subName = reader.GetAttribute("sub_name");
        var extension = reader.GetAttribute("extension");
        var providers = reader.GetAttribute("providers");
        var desc = reader.GetAttribute("desc");
        var path = reader.GetAttribute("path");

        if (reader.IsEmptyElement)
        {
            return new SubShareRecord
            {
                Time = ParseTime(timeText),
                NameChs = nameChs ?? string.Empty,
                NameCht = nameCht ?? string.Empty,
                NameJp = nameJp ?? string.Empty,
                NameEn = nameEn ?? string.Empty,
                NameRome = nameRome ?? string.Empty,
                Type = type ?? string.Empty,
                Source = source ?? string.Empty,
                SubName = subName ?? string.Empty,
                Extension = extension ?? string.Empty,
                Providers = providers ?? string.Empty,
                Desc = desc ?? string.Empty,
                Path = path ?? string.Empty
            };
        }

        var depth = reader.Depth;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth
                && string.Equals(reader.LocalName, "subs", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            // 注意：db.xml 來源可能有不同欄位大小寫/命名；這裡用 LocalName 並接受常見欄位。
            switch (reader.LocalName)
            {
                case "time":
                    timeText = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "name_chs":
                    nameChs = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "name_cht":
                    nameCht = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "name_jp":
                    nameJp = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "name_en":
                    nameEn = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "name_rome":
                    nameRome = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "type":
                    type = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "source":
                    source = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "sub_name":
                    subName = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "extension":
                    extension = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "providers":
                    providers = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "desc":
                    desc = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
                case "path":
                    path = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                    break;
            }
        }

        return new SubShareRecord
        {
            Time = ParseTime(timeText),
            NameChs = nameChs ?? string.Empty,
            NameCht = nameCht ?? string.Empty,
            NameJp = nameJp ?? string.Empty,
            NameEn = nameEn ?? string.Empty,
            NameRome = nameRome ?? string.Empty,
            Type = type ?? string.Empty,
            Source = source ?? string.Empty,
            SubName = subName ?? string.Empty,
            Extension = extension ?? string.Empty,
            Providers = providers ?? string.Empty,
            Desc = desc ?? string.Empty,
            Path = path ?? string.Empty
        };
    }

    private static DateTimeOffset ParseTime(string? timeText)
    {
        if (string.IsNullOrWhiteSpace(timeText))
        {
            return default;
        }

        var s = timeText.Trim();

        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            }
            catch
            {
                return default;
            }
        }

        if (DateTimeOffset.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out var dto))
        {
            return dto;
        }

        return default;
    }
}
