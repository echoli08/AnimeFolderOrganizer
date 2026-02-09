using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace SmokeQa;

internal static partial class Program
{
    private const string DefaultCustomApiBaseUrl = "https://api.openai.com/v1";
    private const string SubShareRepoPath = "subs_list/animation/1988/(1988.4.16)龙猫";
    private const string DownloadRootFolder = @"Z:\WPF\AnimeFolderOrganizer\.tmp\smokeqa-subshare";

    public static async Task<int> Main()
    {
        try
        {
            var ok1 = await VerifySettingsCustomApiBaseUrlAsync();
            var ok2 = await VerifySubShareDownloadAsync();
            return ok1 && ok2 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SmokeQa 失敗：{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<bool> VerifySettingsCustomApiBaseUrlAsync()
    {
        // 注意：此測試只讀取 settings.json，不呼叫 SaveAsync，避免修改使用者設定。
        var settingsPath = GetUserSettingsJsonPath();
        var expected = TryReadExpectedCustomApiBaseUrl(settingsPath);
        var expectedTrimmed = expected?.Trim();
        var expectedNonDefault = !string.IsNullOrWhiteSpace(expectedTrimmed)
                                 && !string.Equals(expectedTrimmed, DefaultCustomApiBaseUrl, StringComparison.Ordinal);

        var organizerAsm = LoadAnimeFolderOrganizerAssembly();
        var serviceType = organizerAsm.GetType("AnimeFolderOrganizer.Services.FileSettingsService", throwOnError: true)!;

        // 注意：主專案是 net9.0-windows，這個 Smoke harness 必須用反射載入，避免 TFM 不相容。
        var service = Activator.CreateInstance(serviceType)!;
        await InvokeTaskAsync(serviceType, service, "LoadAsync");

        var actual = (string?)serviceType.GetProperty("CustomApiBaseUrl")!.GetValue(service);
        var actualTrimmed = (actual ?? DefaultCustomApiBaseUrl).Trim();

        Console.WriteLine($"CustomApiBaseUrl: {MaskSecrets(actualTrimmed)}");
        Console.WriteLine($"IsDefault({DefaultCustomApiBaseUrl}): {string.Equals(actualTrimmed, DefaultCustomApiBaseUrl, StringComparison.Ordinal)}");

        if (!expectedNonDefault)
        {
            Console.Error.WriteLine("SmokeQa 設定檢查失敗：settings.json 未找到非預設的 CustomApiBaseUrl/DeepseekProxyBaseUrl（無法驗證遷移/載入行為）。");
            Console.Error.WriteLine($"settings.json: {settingsPath}");
            return false;
        }

        if (!string.Equals(actualTrimmed, expectedTrimmed, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("SmokeQa 設定檢查失敗：LoadAsync() 未載入 settings.json 的 CustomApiBaseUrl/DeepseekProxyBaseUrl。" );
            Console.Error.WriteLine($"Expected: {MaskSecrets(expectedTrimmed!)}");
            Console.Error.WriteLine($"Actual:   {MaskSecrets(actualTrimmed)}");
            return false;
        }

        if (string.Equals(actualTrimmed, DefaultCustomApiBaseUrl, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("SmokeQa 設定檢查失敗：載入後的 CustomApiBaseUrl 仍為預設值。" );
            return false;
        }

        return true;
    }

    private static async Task<bool> VerifySubShareDownloadAsync()
    {
        Directory.CreateDirectory(DownloadRootFolder);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        var organizerAsm = LoadAnimeFolderOrganizerAssembly();
        var downloaderType = organizerAsm.GetType("AnimeFolderOrganizer.Services.SubShareSubtitleDownloadService", throwOnError: true)!;
        var animeFolderInfoType = organizerAsm.GetType("AnimeFolderOrganizer.Models.AnimeFolderInfo", throwOnError: true)!;
        var resultType = organizerAsm.GetType("AnimeFolderOrganizer.Models.SubShareSubtitleDownloadResult", throwOnError: true)!;

        var downloader = Activator.CreateInstance(downloaderType, httpClient)!;
        var info = Activator.CreateInstance(animeFolderInfoType, DownloadRootFolder, "SmokeQa")!;
        animeFolderInfoType.GetProperty("SubShareRepoPath")!.SetValue(info, SubShareRepoPath);

        var resultObj = await InvokeTaskWithResultAsync(downloaderType, downloader, "DownloadAsync", new object[]
        {
            info,
            DownloadRootFolder,
            CancellationToken.None
        });

        var errorMessage = (string?)resultType.GetProperty("ErrorMessage")!.GetValue(resultObj);
        if (errorMessage != null)
        {
            Console.Error.WriteLine($"SmokeQa 字幕下載失敗：{errorMessage}");
            return false;
        }

        var downloadedCount = (int)resultType.GetProperty("DownloadedCount")!.GetValue(resultObj)!;
        var skippedCount = (int)resultType.GetProperty("SkippedCount")!.GetValue(resultObj)!;

        Console.WriteLine($"SubShare DownloadedCount: {downloadedCount}");
        Console.WriteLine($"SubShare SkippedCount: {skippedCount}");
        Console.WriteLine($"DownloadRoot: {DownloadRootFolder}");
        Console.WriteLine($"RepoPath: {SubShareRepoPath}");

        // 需求：若 DownloadedCount==0 且 ErrorMessage==null，也視為失敗（必須至少下載一個字幕）。
        if (downloadedCount <= 0)
        {
            Console.Error.WriteLine("SmokeQa 字幕下載失敗：DownloadedCount==0（未下載到任何字幕檔）。");
            return false;
        }

        return true;
    }

    private static string GetUserSettingsJsonPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "AnimeFolderOrganizer", "settings.json");
    }

    private static string? TryReadExpectedCustomApiBaseUrl(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return null;
            var json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (TryGetString(root, "CustomApiBaseUrl", out var customUrl)) return customUrl;
            if (TryGetString(root, "DeepseekProxyBaseUrl", out var deepseekUrl)) return deepseekUrl;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var prop)) return false;
        if (prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString();
        return true;
    }

    private static string MaskSecrets(string text)
    {
        // 注意：避免輸出任何 key/token 之類的敏感資訊（即使使用者把它塞在 URL query 也要遮罩）。
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return QuerySecretRegex().Replace(text, "$1=***");
    }

    [GeneratedRegex("(?i)(api[_-]?key|key|token|access[_-]?token|authorization)=([^&\\s]+)", RegexOptions.Compiled)]
    private static partial Regex QuerySecretRegex();

    private static Assembly LoadAnimeFolderOrganizerAssembly()
    {
        // 注意：此工具專案不直接參考主專案輸出，避免 TFM 相容性限制；改用載入建置後的 DLL。
        var repoRoot = FindRepoRoot();

        var candidates = new[]
        {
            Path.Combine(repoRoot, "bin", "Release", "net9.0-windows7.0", "AnimeFolderOrganizer.dll"),
            Path.Combine(repoRoot, "bin", "Release", "net9.0-windows", "AnimeFolderOrganizer.dll"),
            Path.Combine(repoRoot, "bin", "Debug", "net9.0-windows7.0", "AnimeFolderOrganizer.dll"),
            Path.Combine(repoRoot, "bin", "Debug", "net9.0-windows", "AnimeFolderOrganizer.dll")
        };

        var asmPath = candidates.FirstOrDefault(File.Exists);
        if (asmPath == null)
        {
            // 若找不到，先嘗試建置主專案（Release）。
            TryBuildAnimeFolderOrganizer(repoRoot);
            asmPath = candidates.FirstOrDefault(File.Exists);
        }

        if (asmPath == null)
        {
            // 最後手段：嘗試在 bin 內搜尋（避免掃整個 repo）。
            var binRoot = Path.Combine(repoRoot, "bin");
            if (Directory.Exists(binRoot))
            {
                asmPath = Directory.EnumerateFiles(binRoot, "AnimeFolderOrganizer.dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
        }

        if (asmPath == null)
        {
            throw new FileNotFoundException("找不到 AnimeFolderOrganizer.dll（請先建置主專案或確認建置輸出路徑）。");
        }

        return Assembly.LoadFrom(asmPath);
    }

    private static void TryBuildAnimeFolderOrganizer(string repoRoot)
    {
        var projectPath = Path.Combine(repoRoot, "AnimeFolderOrganizer.csproj");
        if (!File.Exists(projectPath)) return;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -c Release",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"無法啟動 dotnet 進行建置：{ex.Message}");
        }

        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("建置 AnimeFolderOrganizer 逾時。" );
        }

        if (process.ExitCode != 0)
        {
            var stderr = (process.StandardError.ReadToEnd() ?? string.Empty).Trim();
            var tail = string.Join(Environment.NewLine, stderr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(tail)
                ? "建置 AnimeFolderOrganizer 失敗。"
                : $"建置 AnimeFolderOrganizer 失敗：{tail}");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++)
        {
            var csproj = Path.Combine(dir.FullName, "AnimeFolderOrganizer.csproj");
            if (File.Exists(csproj)) return dir.FullName;
            dir = dir.Parent;
        }

        // fallback：多數情況 dotnet run 會從 repo root 啟動
        var cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "AnimeFolderOrganizer.csproj"))) return cwd;
        throw new DirectoryNotFoundException("找不到 repo root（AnimeFolderOrganizer.csproj）。");
    }

    private static async Task InvokeTaskAsync(Type targetType, object target, string methodName)
    {
        var method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
        {
            throw new MissingMethodException(targetType.FullName, methodName);
        }

        try
        {
            var taskObj = method.Invoke(target, Array.Empty<object>());
            if (taskObj is not Task task)
            {
                throw new InvalidOperationException($"{targetType.FullName}.{methodName}() 回傳型別不是 Task。" );
            }
            await task;
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }

    private static async Task<object> InvokeTaskWithResultAsync(Type targetType, object target, string methodName, object[] args)
    {
        var methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToArray();

        var method = methods.FirstOrDefault(m => m.GetParameters().Length == args.Length);
        if (method == null)
        {
            throw new MissingMethodException(targetType.FullName, methodName);
        }

        try
        {
            var taskObj = method.Invoke(target, args);
            if (taskObj is not Task task)
            {
                throw new InvalidOperationException($"{targetType.FullName}.{methodName}(...) 回傳型別不是 Task。" );
            }

            await task;
            var resultProp = taskObj.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            if (resultProp == null)
            {
                throw new InvalidOperationException($"{targetType.FullName}.{methodName}(...) 回傳型別不是 Task<T>。" );
            }

            return resultProp.GetValue(taskObj)!;
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }
}
