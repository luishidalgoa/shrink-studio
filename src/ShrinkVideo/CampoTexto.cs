using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShrinkVideo;

/// <summary>
/// El campo de texto con marco e icono que sale por toda la app: Origen, Destino, Carpeta a
/// organizar…
///
/// Antes cada uno se montaba a mano con el mismo bloque copiado: un Border que dibujaba el
/// marco y, dentro, un TextBox sin borde. Eso es lo que dejó estos campos sin el haz de
/// foco: el haz se le puso al TextBox, pero ahí el borde no es suyo, es del Border de
/// fuera — y como el bloque estaba duplicado en tres sitios, arreglarlo en uno no arreglaba
/// los otros.
///
/// Hereda de TextBox a propósito, en vez de ser un UserControl que lo envuelva: así todo el
/// código que ya existía —.Text, .LostFocus, .SelectionStart— sigue valiendo tal cual, y el
/// foco del teclado llega al control de verdad y no a una caja intermedia.
/// </summary>
public class CampoTexto : TextBox
{
    /// <summary>
    /// Icono de la izquierda. Sin él, el hueco no se reserva: un campo sin icono queda
    /// alineado como cualquier otro campo normal.
    /// </summary>
    public static readonly DependencyProperty IconoProperty =
        DependencyProperty.Register(nameof(Icono), typeof(Geometry), typeof(CampoTexto),
            new PropertyMetadata(null));

    public Geometry? Icono
    {
        get => (Geometry?)GetValue(IconoProperty);
        set => SetValue(IconoProperty, value);
    }
}
