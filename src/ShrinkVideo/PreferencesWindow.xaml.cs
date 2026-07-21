using System.Windows;
using System.Windows.Input;

namespace ShrinkVideo;

public partial class PreferencesWindow : Window
{
    private const string NoPreset = "— ninguno —";

    /// <summary>Ajustes resultantes tras pulsar Guardar (null si se cancela).</summary>
    public Settings? Result { get; private set; }

    public PreferencesWindow(Settings current, IEnumerable<string> presetNames)
    {
        InitializeComponent();

        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        btnX.Click += (_, _) => Close();
        btnCancel.Click += (_, _) => Close();
        btnSave.Click += (_, _) => Save();

        // General
        cboDefPreset.Items.Add(NoPreset);
        foreach (var n in presetNames) cboDefPreset.Items.Add(n);
        cboDefPreset.SelectedItem = string.IsNullOrEmpty(current.DefaultPreset) ? NoPreset
            : (cboDefPreset.Items.Contains(current.DefaultPreset) ? current.DefaultPreset : NoPreset);
        txtDefLang.Text = current.DefaultLang;
        chkRecurse.IsChecked = current.Recurse;
        chkUpdates.IsChecked = current.CheckUpdatesOnStart;
        // el estado real lo manda el registro, no un ajuste guardado
        chkShell.IsChecked = ShellIntegration.IsRegistered();

        // Al comprimir
        rbAsk.IsChecked = current.AfterCompress == AfterCompress.Ask;
        rbRecycle.IsChecked = current.AfterCompress == AfterCompress.RecycleOriginal;
        rbKeep.IsChecked = current.AfterCompress == AfterCompress.Keep;

        // Rendimiento y disco
        txtMinFree.Text = current.MinFreeMb.ToString();
        chkHw.IsChecked = current.UseHardware;
    }

    private void Save()
    {
        // La integración con el Explorador se aplica al guardar, no al marcar la casilla.
        bool quiere = chkShell.IsChecked == true;
        if (quiere != ShellIntegration.IsRegistered())
        {
            bool ok = quiere ? ShellIntegration.Register() : ShellIntegration.Unregister();
            if (!ok)
                DialogWindow.Aviso(this, "Menú del Explorador", quiere ? "No se pudo añadir la entrada al menú del Explorador."
                           : "No se pudo quitar la entrada del menú del Explorador.");
        }

        var s = new Settings
        {
            DefaultPreset = cboDefPreset.SelectedItem is string p && p != NoPreset ? p : "",
            DefaultLang = string.IsNullOrWhiteSpace(txtDefLang.Text) ? "spa" : txtDefLang.Text.Trim(),
            Recurse = chkRecurse.IsChecked == true,
            CheckUpdatesOnStart = chkUpdates.IsChecked == true,
            AfterCompress = rbRecycle.IsChecked == true ? AfterCompress.RecycleOriginal
                          : rbKeep.IsChecked == true ? AfterCompress.Keep
                          : AfterCompress.Ask,
            MinFreeMb = int.TryParse(txtMinFree.Text.Trim(), out var mb) ? Math.Clamp(mb, 50, 100_000) : 200,
            UseHardware = chkHw.IsChecked == true,
        };
        Result = s;
        DialogResult = true;
    }
}
