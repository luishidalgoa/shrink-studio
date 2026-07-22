using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShrinkVideo;

/// <summary>
/// La espera, en cuatro círculos que laten en cascada. Cada uno hace tres cosas a la vez:
/// el aro crece y se atenúa, el punto de dentro se encoge hasta desaparecer y vuelve, y de
/// él sale una onda que se expande, se afina y se apaga. Cada círculo va 0,3 s por detrás
/// del anterior, así que el conjunto se lee como algo que recorre la fila.
///
/// La onda sale 0,9 s DESPUÉS de su propio latido: es lo que hace que parezca consecuencia
/// del pulso y no un tercer elemento animándose por su cuenta.
///
/// Solo se animan escala, opacidad y grosor de trazo. Ni un efecto, ni un repintado: es la
/// lección de la auditoría de rendimiento, y una animación de espera es justo la que no
/// puede robarle sitio al trabajo por el que se está esperando.
/// </summary>
public sealed class CirculosCargando
{
    private const int Cuantos = 4;
    private const double Aro = 20;
    private const double Punto = 16;
    private const double Ciclo = 2.0;        // segundos: el latido completo
    private const double Escalon = 0.3;      // lo que va cada círculo por detrás del anterior
    private const double RetrasoOnda = 0.9;  // lo que la onda va por detrás de su latido

    public FrameworkElement Raiz { get; }

    private readonly TextBlock _texto;
    private readonly List<(DependencyObject obj, DependencyProperty prop, AnimationTimeline anim)> _pistas = new();
    private bool _enMarcha;

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Gray;

    public CirculosCargando(string texto = "Cargando…")
    {
        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (int i = 0; i < Cuantos; i++) fila.Children.Add(Unidad(i));

        _texto = new TextBlock
        {
            Text = texto, FontSize = 12, Foreground = Rec("Neutral500"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 22, 0, 0),
            TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
        };

        var pila = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
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

    /// <summary>Un círculo: la onda detrás, el aro, y el punto encima.</summary>
    private FrameworkElement Unidad(int i)
    {
        var acompasado = TimeSpan.FromSeconds(i * Escalon);
        var color = Rec("Accent");

        // ── la onda ── nace pequeña y gruesa (casi un disco) y se va como un anillo fino
        var escalaOnda = new ScaleTransform(0.4, 0.4);
        var onda = new Ellipse
        {
            Width = Aro, Height = Aro, Stroke = color, StrokeThickness = 9, Opacity = 0,
            RenderTransform = escalaOnda, RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var retrasoOnda = TimeSpan.FromSeconds(i * Escalon + RetrasoOnda);
        Pista(escalaOnda, ScaleTransform.ScaleXProperty, Curva(retrasoOnda, (0, 0.4), (1, 2.2)));
        Pista(escalaOnda, ScaleTransform.ScaleYProperty, Curva(retrasoOnda, (0, 0.4), (1, 2.2)));
        Pista(onda, Shape.StrokeThicknessProperty, Curva(retrasoOnda, (0, 9), (1, 0)));
        Pista(onda, UIElement.OpacityProperty, Curva(retrasoOnda, (0, 1), (1, 0)));

        // ── el aro ── crece a 1,5 y vuelve, atenuándose a la mitad por el camino
        var escalaAro = new ScaleTransform(1, 1);
        var aro = new Ellipse
        {
            Width = Aro, Height = Aro, Stroke = color, StrokeThickness = 2,
            RenderTransform = escalaAro, RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Pista(escalaAro, ScaleTransform.ScaleXProperty, Curva(acompasado, (0, 1), (0.5, 1.5), (1, 1)));
        Pista(escalaAro, ScaleTransform.ScaleYProperty, Curva(acompasado, (0, 1), (0.5, 1.5), (1, 1)));
        Pista(aro, UIElement.OpacityProperty, Curva(acompasado, (0, 1), (0.5, 0.5), (1, 1)));

        // ── el punto ── se encoge hasta desaparecer y vuelve
        var escalaPunto = new ScaleTransform(1, 1);
        var punto = new Ellipse
        {
            Width = Punto, Height = Punto, Fill = color,
            RenderTransform = escalaPunto, RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Pista(escalaPunto, ScaleTransform.ScaleXProperty, Curva(acompasado, (0, 1), (0.5, 0), (1, 1)));
        Pista(escalaPunto, ScaleTransform.ScaleYProperty, Curva(acompasado, (0, 1), (0.5, 0), (1, 1)));

        return new Grid
        {
            Width = Aro, Height = Aro,
            Margin = new Thickness(i == 0 ? 0 : 10, 0, i == Cuantos - 1 ? 0 : 10, 0),
            Children = { onda, aro, punto },
        };
    }

    /// <summary>
    /// Una curva del ciclo por fotogramas clave, en fracciones del ciclo (0 = principio,
    /// 1 = final). Suave de entrada y de salida, como el original.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames Curva(TimeSpan retraso, params (double t, double v)[] puntos)
    {
        var a = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = retraso,
            Duration = TimeSpan.FromSeconds(Ciclo),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        foreach (var (t, v) in puntos)
            a.KeyFrames.Add(new EasingDoubleKeyFrame(v,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t * Ciclo)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        return a;
    }

    private void Pista(DependencyObject obj, DependencyProperty prop, AnimationTimeline anim) =>
        _pistas.Add((obj, prop, anim));

    public void Arrancar(string? texto = null)
    {
        if (texto != null) _texto.Text = texto;
        Raiz.Visibility = Visibility.Visible;
        if (_enMarcha) return;
        _enMarcha = true;
        foreach (var (obj, prop, anim) in _pistas) Aplicar(obj, prop, anim);
    }

    public void Parar()
    {
        Raiz.Visibility = Visibility.Collapsed;
        if (!_enMarcha) return;
        _enMarcha = false;
        // A null, no con un Storyboard: parar no depende entonces de cómo se arrancó.
        foreach (var (obj, prop, _) in _pistas) Aplicar(obj, prop, null);
    }

    private static void Aplicar(DependencyObject obj, DependencyProperty prop, AnimationTimeline? anim)
    {
        switch (obj)
        {
            case Animatable a: a.BeginAnimation(prop, anim); break;
            case UIElement u: u.BeginAnimation(prop, anim); break;
        }
    }
}
