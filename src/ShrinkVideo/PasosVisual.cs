using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShrinkVideo;

/// <summary>
/// El progreso de la identificación como un recorrido de pasos, en vertical: cada etapa con
/// su círculo y su rótulo, unidas por una línea de puntos.
///
/// El gesto que lo distingue es el paso de testigo: al completarse una etapa, su círculo se
/// estira hacia la siguiente como una gota, suelta un punto que baja por la línea tiñéndola,
/// y el círculo de destino se abulta al recibirlo. Al terminar las tres, se juntan y funden
/// en un único check grande con un halo y un destello verde que se apaga.
///
/// Se construye en código y no en XAML por las animaciones: cada figura necesita SU
/// instancia de transformación y de trazo (un Freezable compartido se congela y animarlo
/// revienta). Y todo lo que se anima es opacidad, escala o desplazamiento — nunca un
/// Effect, que obligaría a repintar su subárbol en cada fotograma.
/// </summary>
public sealed class PasosVisual
{
    // ── medidas ──
    private const double Diametro = 24;      // círculo de cada paso
    private const double Trazo = 2;
    private const double LargoConector = 22; // el tramo de puntos entre dos círculos
    private const double ViajeMs = 340;      // lo que tarda el testigo en bajar

    /// <summary>Perímetro en unidades de trazo: es lo que entiende StrokeDashArray.</summary>
    private static readonly double Vuelta = Math.PI * (Diametro - Trazo) / Trazo;

    public FrameworkElement Raiz { get; }

    private readonly Paso[] _pasos;
    private readonly Conector[] _conectores;
    private readonly Grid _lista;
    private readonly StackPanel _final;
    private readonly TextBlock _finTitulo;
    private readonly TextBlock _finDetalle;
    private readonly Ellipse _resplandor;

    private static Brush Rec(string clave) =>
        Application.Current?.TryFindResource(clave) as Brush ?? Brushes.Gray;

    public PasosVisual(params string[] rotulos)
    {
        _pasos = new Paso[rotulos.Length];
        _conectores = new Conector[Math.Max(0, rotulos.Length - 1)];

        // Una fila por paso y, entre ellas, una fila corta para el tramo de puntos.
        _lista = new Grid();
        _lista.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Diametro) });
        _lista.ColumnDefinitions.Add(new ColumnDefinition());

        int fila = 0;
        for (int i = 0; i < rotulos.Length; i++)
        {
            if (i > 0)
            {
                _lista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LargoConector) });
                _conectores[i - 1] = new Conector();
                Grid.SetRow(_conectores[i - 1].Raiz, fila);
                _lista.Children.Add(_conectores[i - 1].Raiz);
                fila++;
            }

            _lista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pasos[i] = new Paso(i + 1, rotulos[i]);
            Grid.SetRow(_pasos[i].Raiz, fila);
            Grid.SetRow(_pasos[i].Textos, fila);
            Grid.SetColumn(_pasos[i].Textos, 1);
            _lista.Children.Add(_pasos[i].Raiz);
            _lista.Children.Add(_pasos[i].Textos);
            fila++;
        }

        // El desenlace: un solo check grande con su halo, en el sitio de la lista. Aparece
        // cuando ella se apaga, así el cambio se lee como una fusión y no como un corte.
        var icono = new Grid
        {
            Width = 44, Height = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = new ScaleTransform(0.6, 0.6),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        icono.Children.Add(new Ellipse { Fill = Rec("EstadoOk"), Opacity = 0.18 });
        icono.Children.Add(new Ellipse
        {
            Width = 30, Height = 30, Fill = Rec("EstadoOk"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        icono.Children.Add(new Path
        {
            Data = Geometry.Parse("M16,22.5 L20.5,27 L28.5,17.5"),
            Stroke = Brushes.White, StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });

        _finTitulo = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Rec("Text"),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        };
        _finDetalle = new TextBlock
        {
            FontSize = 11.5, Foreground = Rec("Neutral500"),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
        };

        _final = new StackPanel
        {
            Opacity = 0, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _final.Children.Add(icono);
        _final.Children.Add(_finTitulo);
        _final.Children.Add(_finDetalle);

        // El destello del final: un degradado radial, NO un desenfoque. Se ve igual de suave
        // y no obliga a WPF a componer una superficie aparte en cada fotograma.
        _resplandor = new Ellipse
        {
            Width = 300, Height = 190, Opacity = 0, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x7A, 0x7B, 0xD8, 0x8F), 0),
                    new GradientStop(Color.FromArgb(0x24, 0x7B, 0xD8, 0x8F), 0.55),
                    new GradientStop(Color.FromArgb(0x00, 0x7B, 0xD8, 0x8F), 1),
                },
            },
        };

        var raiz = new Grid();
        raiz.Children.Add(_resplandor);
        raiz.Children.Add(_lista);
        raiz.Children.Add(_final);
        Raiz = raiz;

        Reiniciar();
    }

    // ─────────────────────────── estados ───────────────────────────

    public void Reiniciar()
    {
        foreach (var p in _pasos) { p.Soltar(); p.Pendiente(); }
        foreach (var c in _conectores) c.Apagar();
        _lista.BeginAnimation(UIElement.OpacityProperty, null);
        _lista.Opacity = 1;
        _lista.Visibility = Visibility.Visible;
        _final.BeginAnimation(UIElement.OpacityProperty, null);
        _final.Opacity = 0;
        _resplandor.BeginAnimation(UIElement.OpacityProperty, null);
        _resplandor.Opacity = 0;
    }

    public void EnCurso(int i) => _pasos[i].EnCurso();

    /// <summary>
    /// Un paso queda hecho: check, y el testigo baja hacia el siguiente.
    /// </summary>
    public void Hecha(int i, string? detalle = null)
    {
        _pasos[i].Hecho(detalle);

        if (i < _conectores.Length)
        {
            _pasos[i].Estirar();                        // la gota que se alarga al soltarlo
            _conectores[i].Encender();                  // el punto que baja tiñendo la línea
            var destino = _pasos[i + 1];
            Retrasar(ViajeMs, () => destino.Abultar());  // y el de abajo lo acusa
        }
    }

    /// <summary>Los pasos se juntan y funden en un único check, con destello.</summary>
    public void Terminado(string titulo, string? detalle = null)
    {
        _finTitulo.Text = titulo;
        _finDetalle.Text = detalle ?? "";

        // Los de los extremos caminan hacia el del medio: la distancia es exactamente lo que
        // los separa, el círculo más el tramo de puntos.
        double separacion = Diametro + LargoConector;
        double medio = (_pasos.Length - 1) / 2.0;
        for (int i = 0; i < _pasos.Length; i++) _pasos[i].Recoger((medio - i) * separacion);

        var suave = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var apagar = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = suave };
        apagar.Completed += (_, _) => _lista.Visibility = Visibility.Hidden;
        _lista.BeginAnimation(UIElement.OpacityProperty, apagar);

        _final.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
            { BeginTime = TimeSpan.FromMilliseconds(160), EasingFunction = suave });

        // 0,6 → 1,06 → 1: el rebote corto es lo que hace que parezca que «aterriza»
        var escala = (ScaleTransform)((Grid)_final.Children[0]).RenderTransform;
        var rebote = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(160) };
        rebote.KeyFrames.Add(new EasingDoubleKeyFrame(1.06,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), suave));
        rebote.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        escala.BeginAnimation(ScaleTransform.ScaleXProperty, rebote);
        escala.BeginAnimation(ScaleTransform.ScaleYProperty, rebote);

        var brillo = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(140) };
        brillo.KeyFrames.Add(new EasingDoubleKeyFrame(1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), suave));
        brillo.KeyFrames.Add(new EasingDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)),
            new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        _resplandor.BeginAnimation(UIElement.OpacityProperty, brillo);
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
        public StackPanel Textos { get; }

        private readonly Ellipse _pista;
        private readonly Ellipse _arco;
        private readonly Ellipse _anillo;
        private readonly TextBlock _num;
        private readonly Path _check;
        private readonly TextBlock _rotulo;
        private readonly TextBlock _detalle;
        private readonly RotateTransform _giro = new();
        private readonly ScaleTransform _escala = new(1, 1);
        private readonly TranslateTransform _mueve = new();
        private readonly TranslateTransform _mueveTexto = new();

        public Paso(int numero, string rotulo)
        {
            Raiz = new Grid
            {
                Width = Diametro, Height = Diametro,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = new TransformGroup { Children = { _escala, _mueve } },
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
                Text = numero.ToString(), FontSize = 11, Foreground = Rec("Neutral600"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _check = new Path
            {
                Data = Geometry.Parse("M7,12.5 L10.5,16 L17,8.5"),
                Stroke = Rec("EstadoOk"), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Opacity = 0,
            };

            Raiz.Children.Add(_pista);
            Raiz.Children.Add(_anillo);
            Raiz.Children.Add(_arco);
            Raiz.Children.Add(_num);
            Raiz.Children.Add(_check);

            _rotulo = new TextBlock
            {
                Text = rotulo, FontSize = 12.5, Foreground = Rec("Neutral500"),
                TextWrapping = TextWrapping.Wrap,
            };
            _detalle = new TextBlock
            {
                FontSize = 11, Foreground = Rec("Neutral500"),
                Visibility = Visibility.Collapsed, Margin = new Thickness(0, 1, 0, 0),
            };
            Textos = new StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _mueveTexto,
            };
            Textos.Children.Add(_rotulo);
            Textos.Children.Add(_detalle);
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
            _rotulo.Foreground = Rec("Neutral500");
            _rotulo.FontWeight = FontWeights.Normal;
            _detalle.Visibility = Visibility.Collapsed;
            Raiz.Opacity = 0.45;
            Textos.Opacity = 0.45;
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
            _rotulo.Foreground = Rec("Text");
            _rotulo.FontWeight = FontWeights.Medium;
            Raiz.Opacity = 1;
            Textos.Opacity = 1;
        }

        public void Hecho(string? detalle)
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

            _rotulo.Foreground = Rec("Text");
            _rotulo.FontWeight = FontWeights.Normal;
            Raiz.Opacity = 1;
            Textos.Opacity = 1;
            if (!string.IsNullOrWhiteSpace(detalle))
            {
                _detalle.Text = detalle;
                _detalle.Visibility = Visibility.Visible;
            }
        }

        /// <summary>La gota: se alarga hacia el de abajo al soltar el testigo.</summary>
        public void Estirar() => Deformar(0.94, 1.16, 90, 210);

        /// <summary>El de destino acusa la llegada con un abultamiento.</summary>
        public void Abultar() => Deformar(1.13, 1.13, 110, 190);

        /// <summary>
        /// Se recoge hacia el centro mientras se apaga: sin ese desplazamiento la fusión
        /// final se lee como un corte entre dos imágenes, no como tres cosas volviéndose una.
        /// </summary>
        public void Recoger(double dy)
        {
            if (dy == 0) return;
            var caminar = new DoubleAnimation(0, dy, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            _mueve.BeginAnimation(TranslateTransform.YProperty, caminar);
            _mueveTexto.BeginAnimation(TranslateTransform.YProperty, caminar);
        }

        public void Soltar()
        {
            _mueve.BeginAnimation(TranslateTransform.YProperty, null);
            _mueveTexto.BeginAnimation(TranslateTransform.YProperty, null);
            _mueve.Y = 0;
            _mueveTexto.Y = 0;
        }

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
        private readonly Canvas _tapa;      // recorta los puntos verdes según baja el testigo
        private readonly Ellipse _testigo;
        private readonly TranslateTransform _mueve = new();

        public Conector()
        {
            Raiz = new Grid { Width = Diametro, Height = LargoConector };

            Raiz.Children.Add(Puntos(Rec("Neutral800")));

            // Los puntos verdes van dentro de un lienzo que crece hacia abajo: así se
            // encienden de uno en uno según pasa el testigo, no todos a la vez.
            _tapa = new Canvas
            {
                Height = 0, ClipToBounds = true, Width = Diametro,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var verdes = Puntos(Rec("EstadoOk"));
            verdes.Width = Diametro;
            verdes.Height = LargoConector;
            _tapa.Children.Add(verdes);
            Raiz.Children.Add(_tapa);

            _testigo = new Ellipse
            {
                Width = 5, Height = 5, Fill = Rec("EstadoOk"), Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = _mueve,
            };
            Raiz.Children.Add(_testigo);
        }

        private static Path Puntos(Brush color) => new()
        {
            Data = Geometry.Parse($"M{Diametro / 2},0 L{Diametro / 2},{LargoConector}"),
            Stroke = color, StrokeThickness = 2.2,
            StrokeDashArray = new DoubleCollection { 0.01, 2.6 },
            StrokeDashCap = PenLineCap.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };

        public void Apagar()
        {
            _tapa.BeginAnimation(FrameworkElement.HeightProperty, null);
            _tapa.Height = 0;
            _testigo.BeginAnimation(UIElement.OpacityProperty, null);
            _mueve.BeginAnimation(TranslateTransform.YProperty, null);
            _testigo.Opacity = 0;
            _mueve.Y = 0;
        }

        public void Encender()
        {
            var suave = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            _testigo.Opacity = 1;
            _mueve.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, LargoConector - 5, TimeSpan.FromMilliseconds(ViajeMs))
                { EasingFunction = suave });
            _tapa.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation(0, LargoConector, TimeSpan.FromMilliseconds(ViajeMs))
                { EasingFunction = suave });
            _testigo.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
                { BeginTime = TimeSpan.FromMilliseconds(ViajeMs - 40) });
        }
    }
}
