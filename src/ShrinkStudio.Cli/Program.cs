using System.Globalization;
using ShrinkVideo;

// ============================================================================
//  ShrinkStudio CLI — mismo motor que la app de escritorio, pero multiplataforma.
//  La interfaz gráfica usa WPF (solo Windows); esto corre en Linux y macOS.
// ============================================================================

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Help();
    return 0;
}

string cmd = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return cmd switch
    {
        "comprimir" or "compress" => await CompressAsync(rest),
        "analizar" or "probe" => await ProbeAsync(rest),
        "medir" or "measure" => await MeasureAsync(rest),
        "version" or "--version" => Version(),
        _ => Unknown(cmd),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelado.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 1;
}

// ---------------------------------------------------------------------------

static int Version()
{
    Console.WriteLine("ShrinkStudio CLI " + (typeof(Engine).Assembly.GetName().Version?.ToString(3) ?? "?"));
    return 0;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Orden desconocida: «{cmd}». Prueba con --help.");
    return 2;
}

static void Help() => Console.WriteLine("""
ShrinkStudio CLI — compresor de vídeo con foco en el ahorro de almacenamiento.

USO
  shrinkstudio <orden> [rutas...] [opciones]

ÓRDENES
  comprimir <archivos|carpetas>   Comprime los vídeos indicados
  analizar  <archivos|carpetas>   Muestra pistas, duración y tamaño
  medir     <archivo>             Mide el tamaño real codificando muestras cortas
  version                         Muestra la versión

OPCIONES DE SALIDA
  -o, --salida <carpeta>     Carpeta de destino (por defecto: «comprimido» junto al origen)
      --formato <mkv|mp4|webm|mp3|m4a|flac|opus>   Contenedor o solo audio (por defecto mkv)
      --codec <hevc|h264|av1>                      Códec de vídeo (por defecto hevc)
  -q, --calidad <18-35>      Calidad CRF: menos = mejor imagen y más peso (por defecto automática)
      --alto <px>            Reescala si supera esa altura (p. ej. 1080, 720)
      --audio <kbps>         Recodifica el audio a esa tasa (0 = copiar el original)
      --idioma <cod>         Idioma de audio preferido (por defecto spa)
      --idiomas <a,b,c>      Idiomas de audio a conservar (por defecto: el preferido + eng)
      --sin-subs             No incluir subtítulos

OPCIONES GENERALES
  -r, --recursivo            Entrar en subcarpetas al buscar vídeos
  -f, --forzar               Reprocesar aunque ya exista la salida
  -n, --simular              Solo mostrar lo que haría, sin escribir nada
      --sin-hardware         No usar la GPU (codificar por CPU)
      --margen-disco <MB>    Margen mínimo de disco antes de pausar (por defecto 200)

RENOMBRADO DE LA SALIDA (estilo PowerRename)
      --buscar <texto>       Texto o expresión regular a buscar en el nombre
      --reemplazar <texto>   Sustitución; admite $1..$9, ${}, ${padding=2;start=1}, $YYYY, $MM…
      --regex                Interpretar «--buscar» como expresión regular
      --enumerar             Habilitar los contadores ${...}

EJEMPLOS
  shrinkstudio comprimir serie/ -r --formato mp4 --alto 720 --audio 128 -o comprimidos/
  shrinkstudio medir capitulo.mkv --alto 720
  shrinkstudio comprimir *.mkv --regex --buscar "^" --reemplazar "T01E${padding=2;start=1} - " --enumerar

Necesita ffmpeg y ffprobe en el PATH.
""");

static async Task<int> CompressAsync(string[] args)
{
    var (paths, o) = Parse(args);
    if (paths.Count == 0) { Console.Error.WriteLine("Indica al menos un archivo o carpeta."); return 2; }
    var files = Collect(paths, o.recursive);
    if (files.Count == 0) { Console.Error.WriteLine("No se han encontrado vídeos."); return 1; }

    if (!await Engine.ToolsAvailableAsync())
    {
        Console.Error.WriteLine("No se encuentra ffmpeg en el PATH. Instálalo y vuelve a intentarlo.");
        return 3;
    }

    Engine.AllowHardware = o.hardware;
    Engine.MinFreeBytes = o.minFreeMb * 1024L * 1024;

    Console.WriteLine($"{files.Count} vídeo(s) a procesar.");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("\nDeteniendo…"); };

    var engine = new Engine();
    var results = await engine.CompressAsync(files, o.opt, new ConsoleReporter(), cts.Token);

    var ok = results.Where(r => r.Ok).ToList();
    if (ok.Count > 0)
    {
        long inB = ok.Sum(r => r.InBytes), outB = ok.Sum(r => r.OutBytes!.Value);
        int pct = (int)Math.Round(100 - outB / (double)Math.Max(inB, 1) * 100);
        Console.WriteLine($"\nTerminado: {ok.Count} archivo(s) · {Human(inB)} → {Human(outB)} (-{pct}%)");
    }
    else Console.WriteLine("\nTerminado: no se generó ningún archivo.");
    return 0;
}

static async Task<int> ProbeAsync(string[] args)
{
    var (paths, o) = Parse(args);
    var files = Collect(paths, o.recursive);
    if (files.Count == 0) { Console.Error.WriteLine("No se han encontrado vídeos."); return 1; }

    var engine = new Engine();
    Console.WriteLine($"{"ARCHIVO",-46} {"TAMAÑO",10} {"DURACIÓN",9} {"CÓDEC",7} {"RESOL.",10}  AUDIO");
    foreach (var f in files)
    {
        var i = await engine.ProbeAsync(f);
        var name = Path.GetFileName(f);
        if (name.Length > 45) name = name[..42] + "…";
        Console.WriteLine($"{name,-46} {Human(new FileInfo(f).Length),10} " +
                          $"{TimeSpan.FromSeconds(i.DurationSec):hh\\:mm\\:ss} {i.Codec,7} " +
                          $"{i.Width + "x" + i.Height,10}  {string.Join("+", i.AudioLangs)}");
    }
    return 0;
}

static async Task<int> MeasureAsync(string[] args)
{
    var (paths, o) = Parse(args);
    if (paths.Count == 0) { Console.Error.WriteLine("Indica un archivo."); return 2; }
    var file = Collect(paths, o.recursive).FirstOrDefault();
    if (file == null) { Console.Error.WriteLine("No se ha encontrado el vídeo."); return 1; }

    Engine.AllowHardware = o.hardware;
    var engine = new Engine();
    Console.WriteLine($"Midiendo «{Path.GetFileName(file)}» con muestras reales…");
    int kbps = await engine.MeasureVideoBitrateAsync(file, o.opt, new ConsoleReporter(quiet: true), CancellationToken.None);
    if (kbps <= 0) { Console.Error.WriteLine("No se pudo medir."); return 1; }

    var info = await engine.ProbeAsync(file);
    long est = (long)((kbps + 192.0) * 1000 / 8 * info.DurationSec * 1.02);
    long orig = new FileInfo(file).Length;
    Console.WriteLine($"Vídeo medido : {kbps} kbps");
    Console.WriteLine($"Tamaño previsto: ≈ {Human(est)}  (original {Human(orig)}, ahorro ≈ {100 - est * 100 / Math.Max(orig, 1)}%)");
    Console.WriteLine("Incluye una estimación de audio de 192 kbps; el valor exacto depende de las pistas que conserves.");
    return 0;
}

// ---------------------------------------------------------------------------

static List<string> Collect(List<string> paths, bool recursive)
{
    var files = new List<string>();
    foreach (var p in paths)
    {
        if (File.Exists(p)) { files.Add(Path.GetFullPath(p)); continue; }
        if (!Directory.Exists(p)) { Console.Error.WriteLine($"No existe: {p}"); continue; }
        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        files.AddRange(Directory.EnumerateFiles(p, "*.*", opt)
            .Where(f => Engine.VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(Path.GetFullPath));
    }
    return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
}

static (List<string> paths, Opts o) Parse(string[] args)
{
    var paths = new List<string>();
    var opt = new EncodeOptions();
    var rule = new RenameRule();
    bool recursive = false, hardware = true;
    int minFreeMb = 200;

    string Next(ref int i) => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Falta el valor de «{args[i]}»");
    int NextInt(ref int i) => int.TryParse(Next(ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        ? v : throw new ArgumentException("Se esperaba un número");

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--salida": opt.Output = Path.GetFullPath(Next(ref i)); break;
            case "--formato":
                var f = Next(ref i).ToLowerInvariant();
                if (f is "mp3" or "m4a" or "flac" or "opus") { opt.AudioOnly = true; opt.AudioFormat = f; }
                else opt.Container = f;
                break;
            case "--codec": opt.VideoCodec = Next(ref i).ToLowerInvariant(); break;
            case "-q" or "--calidad": opt.Quality = NextInt(ref i); break;
            case "--alto": opt.MaxHeight = NextInt(ref i); break;
            case "--audio": opt.AudioBitrate = NextInt(ref i); break;
            case "--idioma": opt.Lang = Next(ref i); break;
            case "--idiomas": opt.KeepLangs = Next(ref i).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList(); break;
            case "--sin-subs": opt.NoSubs = true; break;
            case "-r" or "--recursivo": recursive = true; break;
            case "-f" or "--forzar": opt.Force = true; break;
            case "-n" or "--simular": opt.DryRun = true; break;
            case "--sin-hardware": hardware = false; break;
            case "--margen-disco": minFreeMb = NextInt(ref i); break;
            case "--buscar": rule.Search = Next(ref i); rule.Enabled = true; break;
            case "--reemplazar": rule.Replace = Next(ref i); rule.Enabled = true; break;
            case "--regex": rule.UseRegex = true; break;
            case "--enumerar": rule.Enumerate = true; break;
            default:
                if (args[i].StartsWith('-')) throw new ArgumentException($"Opción desconocida: {args[i]}");
                paths.Add(args[i]);
                break;
        }
    }
    if (rule.HasEffect) opt.NameRule = rule;
    return (paths, new Opts(opt, recursive, hardware, minFreeMb));
}

static string Human(long b) => b switch
{
    >= (1L << 30) => $"{b / (double)(1L << 30):n2} GB",
    >= (1L << 20) => $"{b / (double)(1L << 20):n0} MB",
    _ => $"{b / 1024.0:n0} KB",
};

readonly record struct Opts(EncodeOptions opt, bool recursive, bool hardware, int minFreeMb);

/// <summary>Vuelca el progreso del motor por consola, con una barra en la misma línea.</summary>
sealed class ConsoleReporter(bool quiet = false) : IEngineReporter
{
    private string _current = "";

    public void Log(string line) { if (!quiet) Console.WriteLine(line); }

    // El motor ya narra el archivo por Log(): aquí solo llevamos la barra de progreso.
    public void FileStart(int index, int total, string name, double durationSec) => _current = name;

    public void FileProgress(double fraction, string rawLine)
    {
        if (quiet || Console.IsOutputRedirected) return;
        int width = 28;
        int done = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width);
        Console.Write($"\r    [{new string('#', done)}{new string('.', width - done)}] {fraction * 100,5:n1}%   ");
    }

    public void FileDone(FileResult r)
    {
        // borra la barra de progreso; el resultado lo narra el propio motor
        if (!Console.IsOutputRedirected && !quiet) Console.Write("\r" + new string(' ', 52) + "\r");
    }

    public void DiskFull(bool paused) => Console.WriteLine(paused
        ? "    Disco lleno — en pausa. Libera espacio y continuará solo."
        : "    Espacio disponible — continuando…");
}
