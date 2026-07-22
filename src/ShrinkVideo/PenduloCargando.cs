using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShrinkVideo;

/// <summary>
/// El péndulo de Newton, para la espera de un vídeo: cinco bolas colgando, la de un extremo
/// cae, y al chocar sale disparada la del otro.
///
/// Se eligió por lo que cuenta, no por bonito: un giro sin fin no dice nada, y estas esperas
/// pueden ser largas de verdad (con «Archivos a petición» el vídeo se está descargando
/// entero). El péndulo tiene ritmo y cadencia — se ve que el tiempo pasa y que algo sigue
/// vivo, incluso mirándolo de reojo.
///
/// Solo se anima el ÁNGULO de dos elementos. Nada de efectos ni de repintar: es la lección
/// que dejó la auditoría de rendimiento, y una animación de espera es justo la que no puede
/// permitirse robarle sitio al trabajo por el que se está esperando.
/// </summary>
public sealed class PenduloCargando
{
    private const double Bola = 13;      // diámetro; las bolas se tocan, como en el de verdad
    private const double Cuerda = 26;
    private const double Angulo = 34;    // lo que se levanta la bola del extremo
    private const double Medio = 0.45;   // segundos de cada mitad del vaivén

    public FrameworkElement Raiz { get; }

    private readonly TextBlock _texto;
    private readonly RotateTransform _izq = new();
    private readonly RotateTransform _der = new();
    private bool _enMarcha;

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Gray;

    public PenduloCargando(string texto = "Cargando…")
    {
        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // La barra de la que cuelgan todas, para que se lea como un péndulo y no como
        // cinco puntos sueltos que se mueven.
        var barra = new Border
        {
            Height = 2, Width = Bola * 5 + 6, CornerRadius = new CornerRadius(1),
            Background = Rec("Neutral800"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        for (int i = 0; i < 5; i++)
        {
            var giro = i == 0 ? _izq : i == 4 ? _der : null;
            fila.Children.Add(Colgante(giro));
        }

        _texto = new TextBlock
        {
            Text = texto, FontSize = 12, Foreground = Rec("Neutral500"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 280,
        };

        var pila = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        pila.Children.Add(barra);
        pila.Children.Add(fila);
        pila.Children.Add(_texto);

        Raiz = new Grid
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { pila },
        };
    }

    /// <summary>Una bola con su cuerda, girando desde el punto donde cuelga (arriba).</summary>
    private static FrameworkElement Colgante(RotateTransform? giro)
    {
        var cuerda = new Border
        {
            Width = 1, Height = Cuerda, Background = Rec("Neutral800"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var bola = new Ellipse
        {
            Width = Bola, Height = Bola, Fill = Rec("Accent"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var unidad = new Grid
        {
            Width = Bola, Height = Cuerda + Bola,
            RenderTransformOrigin = new Point(0.5, 0),   // cuelga del techo, no del centro
            Children = { cuerda, bola },
        };
        if (giro != null) unidad.RenderTransform = giro;
        return unidad;
    }

    public void Arrancar(string? texto = null)
    {
        if (texto != null) _texto.Text = texto;
        Raiz.Visibility = Visibility.Visible;
        if (_enMarcha) return;
        _enMarcha = true;

        // La de la izquierda arranca ya levantada y cae; al llegar abajo «choca» y sale la
        // de la derecha. Subir frena (EaseOut) y bajar acelera (EaseIn): sin eso el vaivén
        // parece un metrónomo y no un péndulo.
        _izq.BeginAnimation(RotateTransform.AngleProperty, Vaivén(-Angulo, cayendoPrimero: true));
        _der.BeginAnimation(RotateTransform.AngleProperty, Vaivén(Angulo, cayendoPrimero: false));
    }

    public void Parar()
    {
        Raiz.Visibility = Visibility.Collapsed;
        if (!_enMarcha) return;
        _enMarcha = false;
        _izq.BeginAnimation(RotateTransform.AngleProperty, null);
        _der.BeginAnimation(RotateTransform.AngleProperty, null);
        _izq.Angle = 0;
        _der.Angle = 0;
    }

    /// <summary>
    /// El ciclo completo dura cuatro medios: caer, esperar a que el otro vaya y vuelva, y
    /// subir. Las dos bolas usan la misma curva desplazada media vuelta.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Vaivén(double alto, bool cayendoPrimero)
    {
        var frena = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var acelera = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        KeyTime En(double medios) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(medios * Medio));

        if (cayendoPrimero)
        {
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(alto, En(0)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, En(1), acelera));   // cae
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, En(3)));            // espera al otro
            a.KeyFrames.Add(new EasingDoubleKeyFrame(alto, En(4), frena));  // vuelve a subir
        }
        else
        {
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, En(0)));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, En(1)));            // espera el golpe
            a.KeyFrames.Add(new EasingDoubleKeyFrame(alto, En(2), frena));  // sale disparada
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, En(3), acelera));   // y cae
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, En(4)));
        }
        return a;
    }
}
