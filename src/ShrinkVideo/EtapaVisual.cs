using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShrinkVideo;

/// <summary>
/// Una etapa de la identificación, con su icono animado: círculo discontinuo (pendiente) →
/// arco girando (en curso) → check que se dibuja de izquierda a derecha y da un pequeño
/// salto (hecha).
///
/// Se construye en código y no en XAML por las animaciones: cada icono necesita SU
/// instancia de transformación y de trazo (un Freezable compartido se congela y animarlo
/// revienta — la lección del haz de borde, aplicada de serie).
/// </summary>
public sealed class EtapaVisual
{
    public FrameworkElement Raiz { get; }

    private readonly Grid _icono;
    private readonly TextBlock _texto;
    private readonly TextBlock _detalle;
    private RotateTransform? _giro;

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Gray;

    public EtapaVisual(string texto)
    {
        _icono = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top };
        _texto = new TextBlock
        {
            Text = texto,
            FontSize = 12.5,
            Foreground = Rec("Neutral500"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _detalle = new TextBlock
        {
            FontSize = 11,
            Foreground = Rec("Neutral500"),
            Visibility = Visibility.Collapsed,
        };

        var textos = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
        textos.Children.Add(_texto);
        textos.Children.Add(_detalle);

        var fila = new Grid { Margin = new Thickness(0, 0, 0, 10), Opacity = 0.45 };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(textos, 1);
        fila.Children.Add(_icono);
        fila.Children.Add(textos);
        Raiz = fila;

        Pendiente();
    }

    /// <summary>Círculo de puntos, quieto: aún no le toca.</summary>
    public void Pendiente()
    {
        PararGiro();
        _icono.Children.Clear();
        _icono.Children.Add(new Ellipse
        {
            Width = 16, Height = 16,
            Stroke = Rec("Neutral500"),
            StrokeThickness = 1.6,
            StrokeDashArray = new DoubleCollection { 1.6, 2.4 },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Raiz.Opacity = 0.45;
        _texto.Foreground = Rec("Neutral500");
        _texto.FontWeight = FontWeights.Normal;
    }

    /// <summary>Arco girando: está en ello ahora mismo.</summary>
    public void EnCurso()
    {
        _icono.Children.Clear();
        var giro = new RotateTransform();
        var arco = new Ellipse
        {
            Width = 16, Height = 16,
            Stroke = Rec("Accent"),
            StrokeThickness = 2,
            // Un arco de ~tres cuartos: la circunferencia de un círculo de 14 px son ~44 px,
            // que con trazo de 2 son 22 unidades de guion. 15 pintadas + 7 vacías.
            StrokeDashArray = new DoubleCollection { 15, 7 },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = giro,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _icono.Children.Add(arco);

        // BeginAnimation sobre la propiedad, no Storyboard: pararlo es pasar null y no
        // depende de cómo se arrancó.
        giro.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.0)) { RepeatBehavior = RepeatBehavior.Forever });
        _giro = giro;

        Raiz.Opacity = 1;
        _texto.Foreground = Rec("Text");
        _texto.FontWeight = FontWeights.Medium;
    }

    /// <summary>
    /// Check verde: el trazo se dibuja de izquierda a derecha y el icono da un pequeño salto
    /// de tamaño al terminar (el «splash» del diseño), volviendo a su tamaño real.
    /// </summary>
    public void Hecha(string? detalle = null)
    {
        PararGiro();
        _icono.Children.Clear();

        var escala = new ScaleTransform(1, 1);
        var grupo = new Grid
        {
            Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = escala,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        grupo.Children.Add(new Ellipse { Fill = Rec("OrgOk") });

        // El check en blanco. Longitud del trazo ≈ 11,5 px: el guion arranca «recogido»
        // (offset = longitud) y baja a 0 — eso es lo que lo dibuja de izquierda a derecha.
        const double largo = 11.5;
        var check = new Path
        {
            Data = Geometry.Parse("M4.5,9.5 L7.5,12.5 L13.5,5.5"),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = new DoubleCollection { largo / 2, largo / 2 },   // en unidades de grosor
            StrokeDashOffset = largo / 2,
        };
        grupo.Children.Add(check);
        _icono.Children.Add(grupo);

        check.BeginAnimation(Shape.StrokeDashOffsetProperty,
            new DoubleAnimation(largo / 2, 0, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

        // El salto empieza cuando el trazo ya casi está: 1 → 1,3 → 1
        var salto = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(180) };
        salto.KeyFrames.Add(new EasingDoubleKeyFrame(1.3,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        salto.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, salto);
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, salto);

        Raiz.Opacity = 1;
        _texto.Foreground = Rec("Text");
        _texto.FontWeight = FontWeights.Normal;
        if (!string.IsNullOrWhiteSpace(detalle))
        {
            _detalle.Text = detalle;
            _detalle.Visibility = Visibility.Visible;
        }
    }

    private void PararGiro()
    {
        _giro?.BeginAnimation(RotateTransform.AngleProperty, null);
        _giro = null;
    }
}
