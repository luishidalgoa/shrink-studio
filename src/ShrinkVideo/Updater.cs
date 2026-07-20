using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace ShrinkVideo;

public sealed class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string Tag { get; init; } = "";
    public string Notes { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string AssetName { get; init; } = "";
}

/// <summary>Comprueba GitHub Releases, descarga el instalador nuevo y relanza para actualizar.</summary>
public static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ShrinkVideo-Updater");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string Repo
    {
        get
        {
            var meta = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateRepo");
            return string.IsNullOrWhiteSpace(meta?.Value) ? "luishidalgoa/shrink-video" : meta!.Value!;
        }
    }

    /// <summary>Devuelve la actualización disponible, o null si ya estás al día / sin red.</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{Repo}/releases/latest";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            string clean = tag.TrimStart('v', 'V');
            if (!Version.TryParse(clean, out var latest)) return null;
            latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));
            if (latest <= Current) return null;

            // buscar el asset del instalador (.exe)
            string dl = "", asset = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var nm = a.GetProperty("name").GetString() ?? "";
                    if (nm.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        asset = nm;
                        dl = a.GetProperty("browser_download_url").GetString() ?? "";
                        break;
                    }
                }
            }
            if (dl == "") return null;   // release sin instalador adjunto

            return new UpdateInfo
            {
                Version = latest,
                Tag = tag,
                Notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "",
                DownloadUrl = dl,
                AssetName = asset,
            };
        }
        catch { return null; }   // sin conexión o API caída: no molestar
    }

    /// <summary>Descarga el instalador a una carpeta temporal y devuelve su ruta.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ShrinkVideoUpdate");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, info.AssetName);

        using var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long? totalLen = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0; int r;
        while ((r = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, r));
            read += r;
            if (totalLen is > 0) progress?.Report((double)read / totalLen.Value);
        }
        return dest;
    }

    /// <summary>Lanza el instalador descargado y cierra la app para que pueda actualizar.</summary>
    public static void LaunchInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
