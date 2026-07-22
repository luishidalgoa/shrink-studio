using System.Windows;
using System.Windows.Input;

namespace ShrinkVideo;

/// <summary>
/// El diálogo de la app. Sustituye a <c>MessageBox</c>, que dibuja el cuadro gris del
/// sistema y rompe el tema justo en el momento en que hay que leer con calma.
///
/// Se queda con lo que MessageBox hacía bien —modal, centrado en su ventana, Esc cancela,
/// Intro acepta— y añade lo que le faltaba: el mensaje se puede seleccionar y copiar, que
/// es lo primero que quieres hacer cuando el aviso trae una ruta o el texto de un error.
/// </summary>
public partial class DialogWindow : Window
{
    private DialogWindow(string titulo, string mensaje, string aceptar, string? cancelar,
                         string? entrada = null, string? pista = null)
    {
        InitializeComponent();

        if (entrada != null)
        {
            txtEntrada.Visibility = Visibility.Visible;
            txtEntrada.Text = entrada;
            if (pista != null) txtEntrada.ToolTip = pista;
        }

        lblTitulo.Text = titulo;
        txtMensaje.Text = mensaje;
        btnSi.Content = aceptar;

        if (cancelar != null)
        {
            btnNo.Content = cancelar;
            btnNo.Visibility = Visibility.Visible;
        }

        btnSi.Click += (_, _) => { DialogResult = true; };
        btnNo.Click += (_, _) => { DialogResult = false; };

        // El texto es de solo lectura pero recibe el foco, y con él dentro la tecla Intro
        // no llegaría al botón por defecto.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            else if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        };

        // Si hay algo que escribir, el foco va ahí: teclear es la razón de que el diálogo
        // esté abierto, y llegar y tener que pinchar primero sobra.
        Loaded += (_, _) =>
        {
            if (txtEntrada.Visibility == Visibility.Visible) { txtEntrada.Focus(); txtEntrada.SelectAll(); }
            else btnSi.Focus();
        };
    }

    private static bool Mostrar(Window? dueno, string titulo, string mensaje,
                                string aceptar, string? cancelar)
    {
        var v = new DialogWindow(titulo, mensaje, aceptar, cancelar);

        // Sin dueño visible, WindowStartupLocation=CenterOwner deja la ventana en una
        // esquina; se cae a centrar en la pantalla, que es lo que espera cualquiera.
        if (dueno is { IsVisible: true }) v.Owner = dueno;
        else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return v.ShowDialog() == true;
    }

    /// <summary>Informa de algo. Un solo botón.</summary>
    public static void Aviso(Window? dueno, string titulo, string mensaje, string aceptar = "Entendido") =>
        Mostrar(dueno, titulo, mensaje, aceptar, null);

    /// <summary>
    /// Pide una confirmación. Devuelve true solo si se acepta: cerrar con Esc o con la X
    /// cuenta como «no», que es lo prudente cuando la acción toca ficheros.
    /// </summary>
    public static bool Confirmar(Window? dueno, string titulo, string mensaje,
                                 string aceptar = "Sí", string cancelar = "No") =>
        Mostrar(dueno, titulo, mensaje, aceptar, cancelar);

    /// <summary>
    /// Pide escribir algo. Devuelve null si se cancela — que no es lo mismo que devolver
    /// cadena vacía, porque vacío puede ser una respuesta válida.
    /// </summary>
    public static string? Escribir(Window? dueno, string titulo, string mensaje,
                                   string valor = "", string pista = "",
                                   string aceptar = "Guardar", string cancelar = "Cancelar")
    {
        var v = new DialogWindow(titulo, mensaje, aceptar, cancelar, valor, pista);
        if (dueno is { IsVisible: true }) v.Owner = dueno;
        else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return v.ShowDialog() == true ? v.txtEntrada.Text.Trim() : null;
    }
}
