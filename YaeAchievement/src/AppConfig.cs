using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using YaeAchievement.Utilities;

namespace YaeAchievement;

public static partial class AppConfig {

    public static string GamePath { get; private set; } = null!;

    private static readonly string[] ProductNames = [ "原神", "Genshin Impact" ];

    internal static void Load(string argumentPath) {
        if (argumentPath != "auto" && File.Exists(argumentPath)) {
            GamePath = argumentPath;
        } else if (TryReadGamePathFromCache(out var cachedPath)) {
            GamePath = cachedPath;
        } else if (TryReadGamePathFromUnityLog(out var loggedPath)) {
            GamePath = loggedPath;
        } else {
            GamePath = ReadGamePathFromProcess();
        }
        CacheFile.Write("genshin_impact_game_path_v2", Encoding.UTF8.GetBytes($"{GamePath}\u1145{Utils.GetGameHash(GamePath)}"));
        SentrySdk.AddBreadcrumb(GamePath.EndsWith("YuanShen.exe") ? "CN" : "OS", "GamePath");
        return;
        static bool TryReadGamePathFromCache([NotNullWhen(true)] out string? path) {
            path = null;
            try {
                if (!CacheFile.TryRead("genshin_impact_game_path_v2", out var cacheFile)) {
                    return false;
                }
                var cacheData = cacheFile.Content.ToStringUtf8().Split("\u1145");
                if (Utils.GetGameHash(cacheData[0]) != uint.Parse(cacheData[1])) {
                    return false;
                }
                path = cacheData[0];
                return true;
            } catch (Exception) {
                return false;
            }
        }
        static bool TryReadGamePathFromUnityLog([NotNullWhen(true)] out string? path) {
            path = null;
            try {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logPath = ProductNames
                    .Select(name => $"{appDataPath}/../LocalLow/miHoYo/{name}/output_log.txt")
                    .Where(File.Exists)
                    .MaxBy(File.GetLastWriteTime);
                if (logPath == null) {
                    return false;
                }
                return (path = GetGamePathFromLogFile(logPath) ?? GetGamePathFromLogFile($"{logPath}.last")) != null;
            } catch (Exception) {
                return false;
            }
        }
        static string ReadGamePathFromProcess() {
            return AnsiConsole.Status().Spinner(Spinner.Known.SimpleDotsScrolling).Start(App.ConfigNeedStartGenshin, _ => {
                Process? proc;
                while ((proc = Utils.GetGameProcess()) == null) {
                    Thread.Sleep(250);
                }
                var fileName = proc.GetFileName()!;
                proc.Kill();
                return fileName;
            });
        }
    }

    private static string? GetGamePathFromLogFile(string path) {
        if (!File.Exists(path)) {
            return null;
        }
        var content = File.ReadAllText(path);
        var matchResult = GamePathRegex().Match(content);
        if (!matchResult.Success) {
            return null;
        }
        var entryName = matchResult.Groups["1"].Value.Replace("_Data", ".exe");
        var fullPath = Path.GetFullPath(Path.Combine(matchResult.Value, "..", entryName));
        return File.Exists(fullPath) ? fullPath : null;
    }

    [GeneratedRegex(@"(?m).:(?:\\|/).+(GenshinImpact_Data|YuanShen_Data)", RegexOptions.IgnoreCase)]
    private static partial Regex GamePathRegex();

}
