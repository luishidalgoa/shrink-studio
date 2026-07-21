using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShrinkVideo;

public enum EstadoPaso { Pendiente, EnCurso, Hecho, Fallo, Saltado }

/// <summary>Un paso de la caja: qué es, cómo va y qué se sabe de él.</summary>
public sealed class Paso : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public required string Clave { get; init; }
    public required string Texto { get; init; }

    private EstadoPaso _estado = EstadoPaso.Pendiente;
    public EstadoPaso Estado
    {
        get => _estado;
        set { _estado = value; N(); N(nameof(Glifo)); N(nameof(EnCurso)); N(nameof(Apagado)); }
    }

    /// <summary>Lo que se ha averiguado al hacerlo: «h264 · 23:26 · 3 audios». Vacío mientras no se sabe.</summary>
    private string _detalle = "";
    public string Detalle
    {
        get => _detalle;
        set { _detalle = value; N(); }
    }

    /// <summary>
    /// Glifo además del color: en blanco y negro, o para quien no distingue verde de ámbar,
    /// el color solo no dice nada. Misma regla que el semáforo de «Organizar».
    /// </summary>
    public string Glifo => Estado switch
    {
        EstadoPaso.Hecho => "✓",
        EstadoPaso.Fallo => "✕",
        EstadoPaso.Saltado => "–",
        EstadoPaso.EnCurso => "◐",
        _ => "○",
    };

    public bool EnCurso => Estado == EstadoPaso.EnCurso;
    /// <summary>Lo que aún no ha pasado se atenúa: así el que está en curso salta a la vista.</summary>
    public bool Apagado => Estado == EstadoPaso.Pendiente;
}

/// <summary>
/// La caja de pasos de un fichero: en qué punto del trabajo está.
///
/// Complementa al registro, no lo sustituye. El registro es la transcripción completa y
/// sirve para saber qué pasó DESPUÉS; esto contesta «¿por dónde va?» de un vistazo, que es
/// lo que se mira MIENTRAS corre. Son dos preguntas distintas.
///
/// Sin WPF a propósito: así el orden de los pasos —que es donde está la lógica— se puede
/// testear sin levantar una ventana.
/// </summary>
public sealed class ListaDePasos : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public const string Leer = "leer";
    public const string Plan = "plan";
    public const string Codificar = "codificar";
    public const string Guardar = "guardar";

    public ObservableCollection<Paso> Pasos { get; } = new()
    {
        new Paso { Clave = Leer,      Texto = "Leer el vídeo" },
        new Paso { Clave = Plan,      Texto = "Elegir pistas y calidad" },
        new Paso { Clave = Codificar, Texto = "Codificar" },
        new Paso { Clave = Guardar,   Texto = "Guardar en el destino" },
    };

    private string _titulo = "";
    /// <summary>«[1/2] Bob_Esponja_8x01…». Vacío mientras no hay nada en marcha.</summary>
    public string Titulo
    {
        get => _titulo;
        private set { _titulo = value; N(); }
    }

    private bool _activa;
    /// <summary>Hay un fichero en curso. Con esto la caja se enseña o se esconde.</summary>
    public bool Activa
    {
        get => _activa;
        private set { _activa = value; N(); }
    }

    public Paso? this[string clave] => Pasos.FirstOrDefault(p => p.Clave == clave);

    /// <summary>Empieza un fichero: todo a cero y el primer paso en marcha.</summary>
    public void Empezar(string titulo)
    {
        Titulo = titulo;
        Activa = true;
        foreach (var p in Pasos) { p.Estado = EstadoPaso.Pendiente; p.Detalle = ""; }
    }

    /// <summary>
    /// Marca un paso como hecho y pone en marcha el siguiente.
    ///
    /// Da por hechos también los anteriores que se quedaran pendientes. No es tolerancia
    /// gratuita: el motor no avisa de cada paso por separado, y una caja con el tercero en
    /// verde y el primero aún en blanco se lee como «algo ha fallado» cuando no ha fallado
    /// nada.
    /// </summary>
    public void Completar(string clave, string? detalle = null)
    {
        int i = IndiceDe(clave);
        if (i < 0) return;

        for (int j = 0; j <= i; j++)
            if (Pasos[j].Estado is EstadoPaso.Pendiente or EstadoPaso.EnCurso)
                Pasos[j].Estado = EstadoPaso.Hecho;

        if (detalle != null) Pasos[i].Detalle = detalle;
        if (i + 1 < Pasos.Count && Pasos[i + 1].Estado == EstadoPaso.Pendiente)
            Pasos[i + 1].Estado = EstadoPaso.EnCurso;
    }

    /// <summary>Un paso que ya está corriendo, con lo que se sepa de él (el «42 %»).</summary>
    public void EnMarcha(string clave, string? detalle = null)
    {
        int i = IndiceDe(clave);
        if (i < 0) return;

        DarPorHechosLosAnteriores(i);

        if (Pasos[i].Estado != EstadoPaso.Fallo) Pasos[i].Estado = EstadoPaso.EnCurso;
        if (detalle != null) Pasos[i].Detalle = detalle;
    }

    /// <summary>
    /// Para estar en el paso N hubo que pasar por los anteriores, así que se cierran. Vale
    /// tanto si iban «pendiente» como si se quedaron «en curso»: lo que no puede quedar es
    /// un hueco en medio, que se lee como que algo se rompió.
    /// </summary>
    private void DarPorHechosLosAnteriores(int i)
    {
        for (int j = 0; j < i; j++)
            if (Pasos[j].Estado is EstadoPaso.Pendiente or EstadoPaso.EnCurso)
                Pasos[j].Estado = EstadoPaso.Hecho;
    }

    /// <summary>
    /// Algo se torció. Lo que venía detrás NO se marca como fallo: no ha fallado, es que ya
    /// no se va a intentar, y decir que falló algo que nunca corrió es mentir.
    /// </summary>
    public void Fallar(string clave, string motivo)
    {
        int i = IndiceDe(clave);
        if (i < 0) return;

        DarPorHechosLosAnteriores(i);
        Pasos[i].Estado = EstadoPaso.Fallo;
        Pasos[i].Detalle = motivo;
        for (int j = i + 1; j < Pasos.Count; j++)
            if (Pasos[j].Estado == EstadoPaso.Pendiente) Pasos[j].Estado = EstadoPaso.Saltado;
    }

    /// <summary>El fichero entero se salta (ya estaba hecho, no se puede leer…).</summary>
    public void Saltar(string motivo)
    {
        foreach (var p in Pasos)
            if (p.Estado is EstadoPaso.Pendiente or EstadoPaso.EnCurso)
            {
                p.Estado = EstadoPaso.Saltado;
                if (p.Detalle.Length == 0) p.Detalle = motivo;
                break;   // el motivo se cuenta una vez, en el paso donde se paró
            }
        for (int j = 0; j < Pasos.Count; j++)
            if (Pasos[j].Estado == EstadoPaso.Pendiente) Pasos[j].Estado = EstadoPaso.Saltado;
    }

    /// <summary>Se acabó el lote: la caja se retira.</summary>
    public void Terminar()
    {
        Activa = false;
        Titulo = "";
    }

    private int IndiceDe(string clave)
    {
        for (int i = 0; i < Pasos.Count; i++)
            if (Pasos[i].Clave == clave) return i;
        return -1;
    }
}
