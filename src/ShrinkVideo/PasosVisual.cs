using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShrinkVideo;

/// <summary>
/// El progreso de la identificación como un recorrido de pasos: círculos numerados unidos
/// por una línea de puntos, con UN solo rótulo debajo que va contando qué se está haciendo.
///
/// El gesto que lo distingue es el paso de testigo: al completarse un paso, su círculo se
/// estira hacia el siguiente como una gota, suelta un punto que recorre la línea tiñéndola,
/// y el círculo de destino se abulta al recibirlo. Al terminar los tres, todo se funde en un
/// único check grande con un halo y un destello verde que se apaga.
///
/// Se construye en código y no en XAML por las animaciones: cada figura necesita SU
/// instancia de transformación y de trazo (un Freezable compartido se congela y animarlo
/// revienta). Y todo lo que se anima es opacidad, escala o desplazamiento — nunca un
/// Effect, que obligaría a repintar su subárbol en cada fotograma.
/// </summary>
public sealed class PasosVisual
{
    // ── medidas ──
    private const double Diametro = 26;      // círculo de cada paso
    private const double Trazo = 2;
    private const double LargoConector = 44;
    private const double ViajeMs = 380;      // lo que tarda el testigo en cruzar

    /// <summary>Perímetro en unidades de trazo: es lo que entiende StrokeDashArray.</summary>
    private static readonly double Vuelta = Math.PI * (Diametro - Trazo) / Trazo;

    public FrameworkElement Raiz { get; }

    private readonly string[] _rotulos;
    private readonly Paso[] _pasos;
    private readonly Conector[] _conectores;
    private readonly TextBlock _titulo;
    private readonly TextBlock _detalle;
    private readonly Grid _fila;
    private readonly Grid _final;
    private readonly Ellipse _resplandor;

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Gray;

    public PasosVisual(params string[] rotulos)
    {
        _rotulos = rotulos;
        _pasos = new Paso[rotulos.Length];
        _conectores = new Conector[Math.Max(0, rotulos.Length - 1)];

        _fila = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        var linea = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < rotulos.Length; i++)
        {
            if (i > 0)
            {
                _conectores[i - 1] = new Conector();
                linea.Children.Add(_conectores[i - 1].Raiz);
            }
            _pasos[i] = new Paso(i + 1);
            linea.Children.Add(_pasos[i].Raiz);
        }
        _fila.Children.Add(linea);

        // El desenlace: un solo check grande con su halo. Vive encima de la fila y aparece
        // cuando ella se apaga, así el cambio se lee como una fusión y no como un corte.
        _final = new Grid
        {
            Width = 44, Height = 44, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransform = new ScaleTransform(0.6, 0.6),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        _final.Children.Add(new Ellipse { Fill = Rec("EstadoOk"), Opacity = 0.18 });
        _final.Children.Add(new Ellipse
        {
            Width = 30, Height = 30, Fill = Rec("EstadoOk"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _final.Children.Add(new Path
        {
            Data = Geometry.Parse("M16,22.5 L20.5,27 L28.5,17.5"),
            Stroke = Brushes.White, StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        _fila.Children.Add(_final);

        _titulo = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Rec("Text"),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
        };
        _detalle = new TextBlock
        {
            FontSize = 11.5, Foreground = Rec("Neutral500"),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        };

        var pila = new StackPanel();
        pila.Children.Add(_fila);
        pila.Children.Add(_titulo);
        pila.Children.Add(_detalle);

        // El destello del final: un degradado radial, NO un desenfoque. Se ve igual de suave
        // y no obliga a WPF a componer una superficie aparte en cada fotograma.
        _resplandor = new Ellipse
        {
            Width = 260, Height = 150, Opacity = 0, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -34, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Fill = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x80, 0x7B, 0xD8, 0x8F), 0),
                    new GradientStop(Color.FromArgb(0x28, 0x7B, 0xD8, 0x8F), 0.55),
                    new GradientStop(Color.FromArgb(0x00, 0x7B, 0xD8, 0x8F), 1),
                },
            },
        };

        var raiz = new Grid();
        raiz.Children.Add(_resplandor);
        raiz.Children.Add(pila);
        Raiz = raiz;

        Reiniciar();
    }

    // ─────────────────────────── estados ───────────────────────────

    public void Reiniciar()
    {
        foreach (var p in _pasos) p.Pendiente();
        foreach (var c in _conectores) c.Apagar();
        _fila.Children[0].BeginAnimation(UIElement.OpacityProperty, null);
        _fila.Children[0].Opacity = 1;
        _final.BeginAnimation(UIElement.OpacityProperty, null);
        _final.Opacity = 0;
        _resplandor.BeginAnimation(UIElement.OpacityProperty, null);
        _resplandor.Opacity = 0;
        _titulo.Text = "";
        _detalle.Text = "";
    }

    public void EnCurso(int i)
    {
        _pasos[i].EnCurso();
        Rotular(_rotulos[i], "");
    }

    /// <summary>
    /// Un paso queda hecho: check, y el testigo sale hacia el siguiente. Cuando es el
    /// último, arranca la fusión final.
    /// </summary>
    public void Hecha(int i, string? detalle = null)
    {
        _pasos[i].Hecho();
        if (detalle != null) _detalle.Text = detalle;

        if (i < _conectores.Length)
        {
            _pasos[i].Estirar();                       // la gota que se alarga al soltarlo
            _conectores[i].Encender();                 // el punto que cruza tiñendo la línea
            var destino = _pasos[i + 1];
            Retrasar(ViajeMs, () => destino.Abultar()); // y el de enfrente lo acusa
        }
    }

    /// <summary>Los tres pasos se funden en un único check, con destello.</summary>
    public void Terminado(string titulo, string? detalle = null)
    {
        Rotular(titulo, detalle ?? "");

        var suave = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        _fila.Children[0].BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = suave });

        _final.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
            { BeginTime = TimeSpan.FromMilliseconds(140), EasingFunction = suave });

        // 0,6 → 1,06 → 1: el rebote corto es lo que hace que parezca que «aterriza»
        var escala = (ScaleTransform)_final.RenderTransform;
        var salto = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(140) };
        salto.KeyFrames.Add(new EasingDoubleKeyFrame(1.06,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), suave));
        salto.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, salto);
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, salto);

        var brillo = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(120) };
        brillo.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), suave));
        brillo.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        _resplandor.BeginAnimation(UIElement.OpacityProperty, brillo);
    }

    /// <summary>El rótulo cruza en vez de saltar de golpe: el cambio se sigue con la vista.</summary>
    private void Rotular(string titulo, string detalle)
    {
        if (_titulo.Text == titulo) { _detalle.Text = detalle; return; }

        var fuera = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fuera.Completed += (_, _) =>
        {
            _titulo.Text = titulo;
            _detalle.Text = detalle;
            _titulo.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        };
        _titulo.BeginAnimation(UIElement.OpacityProperty, fuera);
    }

    private static void Retrasar(double ms, Action accion)
    {
        var t = new System.Windows.Threading.DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); accion(); };
        t.Start();
    }

    // ─────────────────────────── un paso ───────────────────────────

    private sealed class Paso
    {
        public Grid Raiz { get; }
        private readonly Ellipse _pista;
        private readonly Ellipse _arco;
        private readonly Ellipse _anillo;
        private readonly TextBlock _num;
        private readonly Path _check;
        private readonly RotateTransform _giro = new();
        private readonly ScaleTransform _escala = new(1, 1);

        public Paso(int numero)
        {
            Raiz = new Grid
            {
                Width = Diametro, Height = Diametro,
                RenderTransform = _escala,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };

            _pista = new Ellipse { Stroke = Rec("Neutral800"), StrokeThickness = Trazo };

            // Un arco de un cuarto de vuelta sobre la pista: es el que gira mientras trabaja.
            _arco = new Ellipse
            {
                Stroke = Rec("Accent"), StrokeThickness = Trazo,
                StrokeDashArray = new DoubleCollection { Vuelta * 0.28, Vuelta },
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = _giro, RenderTransformOrigin = new Point(0.5, 0.5),
                Opacity = 0,
            };
            _anillo = new Ellipse { Stroke = Rec("EstadoOk"), StrokeThickness = Trazo, Opacity = 0 };

            _num = new TextBlock
            {
                Text = numero.ToString(), FontSize = 11.5, Foreground = Rec("Neutral600"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _check = new Path
            {
                Data = Geometry.Parse("M8,13.5 L11.5,17 L18,9.5"),
                Stroke = Rec("EstadoOk"), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Opacity = 0,
            };

            Raiz.Children.Add(_pista);
            Raiz.Children.Add(_anillo);
            Raiz.Children.Add(_arco);
            Raiz.Children.Add(_num);
            Raiz.Children.Add(_check);
        }

        public void Pendiente()
        {
            _giro.BeginAnimation(RotateTransform.AngleProperty, null);
            _arco.Opacity = 0;
            _anillo.Opacity = 0;
            _check.Opacity = 0;
            _num.Opacity = 1;
            _num.Foreground = Rec("Neutral600");
            _pista.Opacity = 1;
        }

        public void EnCurso()
        {
            _anillo.Opacity = 0;
            _check.Opacity = 0;
            _num.Opacity = 1;
            _num.Foreground = Rec("Text");
            _arco.Opacity = 1;
            _giro.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                { RepeatBehavior = RepeatBehavior.Forever });
        }

        public void Hecho()
        {
            _giro.BeginAnimation(RotateTransform.AngleProperty, null);
            _arco.Opacity = 0;
            _pista.Opacity = 0;
            _anillo.Opacity = 1;
            _num.Opacity = 0;

            // El trazo se dibuja de izquierda a derecha: arranca recogido y se suelta.
            const double largo = 6.5;   // en unidades de grosor
            _check.StrokeDashArray = new DoubleCollection { largo, largo };
            _check.StrokeDashOffset = largo;
            _check.Opacity = 1;
            _check.BeginAnimation(Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(largo, 0, TimeSpan.FromMilliseconds(240))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        }

        /// <summary>La gota: se alarga hacia el siguiente al soltar el testigo.</summary>
        public void Estirar() => Deformar(1.16, 0.94, 90, 210);

        /// <summary>El de destino acusa la llegada con un abultamiento.</summary>
        public void Abultar() => Deformar(1.13, 1.13, 110, 190);

        private void Deformar(double x, double y, double ida, double vuelta)
        {
            var suave = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimationUsingKeyFrames Curva(double pico)
            {
                var a = new DoubleAnimationUsingKeyFrames();
                a.KeyFrames.Add(new EasingDoubleKeyFrame(pico,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ida)), suave));
                a.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ida + vuelta)),
                    new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                return a;
            }
            _escala.BeginAnimation(ScaleTransform.ScaleXProperty, Curva(x));
            _escala.BeginAnimation(ScaleTransform.ScaleYProperty, Curva(y));
        }
    }

    // ─────────────────────────── la línea entre pasos ───────────────────────────

    private sealed class Conector
    {
        public Grid Raiz { get; }
        private readonly Canvas _tapa;      // recorta los puntos verdes según avanza
        private readonly Ellipse _testigo;
        private readonly TranslateTransform _mueve = new();

        public Conector()
        {
            Raiz = new Grid
            {
                Width = LargoConector, Height = Diametro,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Raiz.Children.Add(Puntos(Rec("Neutral800")));

            // Los puntos verdes van dentro de un lienzo que se ensancha: así se encienden de
            // uno en uno según pasa el testigo, en vez de todos a la vez.
            _tapa = new Canvas
            {
                Width = 0, ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Height = Diametro,
            };
            var verdes = Puntos(Rec("EstadoOk"));
            verdes.Width = LargoConector;
            verdes.Height = Diametro;
            _tapa.Children.Add(verdes);
            Raiz.Children.Add(_tapa);

            _testigo = new Ellipse
            {
                Width = 5, Height = 5, Fill = Rec("EstadoOk"), Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _mueve,
            };
            Raiz.Children.Add(_testigo);
        }

        private static Path Puntos(Brush color) => new()
        {
            Data = Geometry.Parse($"M0,{Diametro / 2} L{LargoConector},{Diametro / 2}"),
            Stroke = color, StrokeThickness = 2.2,
            StrokeDashArray = new DoubleCollection { 0.01, 2.9 },
            StrokeDashCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };

        public void Apagar()
        {
            _tapa.BeginAnimation(FrameworkElement.WidthProperty, null);
            _tapa.Width = 0;
            _testigo.BeginAnimation(UIElement.OpacityProperty, null);
            _mueve.BeginAnimation(TranslateTransform.XProperty, null);
            _testigo.Opacity = 0;
            _mueve.X = 0;
        }

        public void Encender()
        {
            var suave = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            _testigo.Opacity = 1;
            _mueve.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, LargoConector - 5, TimeSpan.FromMilliseconds(ViajeMs))
                { EasingFunction = suave });
            _tapa.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(0, LargoConector, TimeSpan.FromMilliseconds(ViajeMs))
                { EasingFunction = suave });
            _testigo.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
                { BeginTime = TimeSpan.FromMilliseconds(ViajeMs - 40) });
        }
    }
}
