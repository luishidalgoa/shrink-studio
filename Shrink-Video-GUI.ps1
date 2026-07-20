<#
    Interfaz gráfica para Shrink-Video.ps1  (WPF, tema oscuro)
    - Analiza la carpeta y muestra los vídeos identificados con miniaturas y pistas.
    - Selección múltiple con casillas; borrado a la Papelera por archivo o carpeta.
    - Idiomas de audio y subtítulos detectados automáticamente, seleccionables.
    - Audio por defecto: máxima calidad (se copia el original sin recomprimir).
    - Cancelar mata el árbol de procesos completo (pwsh + ffmpeg).
    La lógica de compresión sigue viviendo en Shrink-Video.ps1 (única fuente).
    Lánzalo con doble clic en "Comprimir vídeos.cmd".
#>
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms, Microsoft.VisualBasic

$engine = Join-Path $PSScriptRoot "Shrink-Video.ps1"
if (-not (Test-Path -LiteralPath $engine)) {
    [void][System.Windows.MessageBox]::Show("No encuentro Shrink-Video.ps1 junto a esta interfaz.","Error",'OK','Error'); return
}
$thumbDir = Join-Path $env:TEMP "shrinkgui_thumbs"
New-Item -ItemType Directory -Force $thumbDir | Out-Null

# ---------- modelo de fila (con notificación de cambios para la lista) ----------
Add-Type -TypeDefinition @"
using System.ComponentModel;
public class VideoRow : INotifyPropertyChanged {
    public event PropertyChangedEventHandler PropertyChanged;
    void N(string p){ if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs(p)); }
    bool _sel = true;  string _estado = "";  string _codec = "";  string _audio = "";  string _subs = "";  string _dur = "";
    public bool   Sel    { get { return _sel; }    set { _sel = value;    N("Sel"); } }
    public string Estado { get { return _estado; } set { _estado = value; N("Estado"); } }
    public string Codec  { get { return _codec; }  set { _codec = value;  N("Codec"); } }
    public string Audio  { get { return _audio; }  set { _audio = value;  N("Audio"); } }
    public string Subs   { get { return _subs; }   set { _subs = value;   N("Subs"); } }
    public string Dur    { get { return _dur; }    set { _dur = value;    N("Dur"); } }
    public string Name   { get; set; }
    public string Dir    { get; set; }
    public string SizeMB { get; set; }
    public string Path   { get; set; }
}
"@

# ---------- XAML ----------
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Comprimir vídeos" Width="1180" Height="760" MinWidth="980" MinHeight="620"
        WindowStartupLocation="CenterScreen" Background="#0F1216">
  <Window.Resources>
    <SolidColorBrush x:Key="Card" Color="#1A1F26"/>
    <SolidColorBrush x:Key="Text" Color="#E8EAED"/>
    <SolidColorBrush x:Key="Dim"  Color="#8A93A0"/>
    <SolidColorBrush x:Key="Accent" Color="#6CE8D0"/>
    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{StaticResource Text}"/></Style>
    <Style TargetType="Label"><Setter Property="Foreground" Value="{StaticResource Dim}"/></Style>
    <Style TargetType="TextBox">
      <Setter Property="Background" Value="#232A33"/><Setter Property="Foreground" Value="{StaticResource Text}"/>
      <Setter Property="BorderBrush" Value="#333B46"/><Setter Property="Padding" Value="7,5"/>
      <Setter Property="CaretBrush" Value="{StaticResource Accent}"/>
      <Setter Property="Template">
        <Setter.Value><ControlTemplate TargetType="TextBox">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1" CornerRadius="7"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/></Border>
        </ControlTemplate></Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="Button">
      <Setter Property="Background" Value="#232A33"/><Setter Property="Foreground" Value="{StaticResource Text}"/>
      <Setter Property="Padding" Value="14,7"/><Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Cursor" Value="Hand"/><Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Template">
        <Setter.Value><ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.85"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.35"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate></Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="CheckBox">
      <Setter Property="Foreground" Value="{StaticResource Text}"/><Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style TargetType="ComboBox"><Setter Property="Padding" Value="8,4"/></Style>
    <Style TargetType="ListView">
      <Setter Property="Background" Value="{StaticResource Card}"/><Setter Property="Foreground" Value="{StaticResource Text}"/>
      <Setter Property="BorderThickness" Value="0"/>
    </Style>
    <Style TargetType="GridViewColumnHeader">
      <Setter Property="Background" Value="#232A33"/><Setter Property="Foreground" Value="{StaticResource Dim}"/>
      <Setter Property="Padding" Value="8,6"/><Setter Property="BorderThickness" Value="0"/>
    </Style>
    <Style TargetType="ListViewItem">
      <Setter Property="Foreground" Value="{StaticResource Text}"/>
      <Setter Property="Padding" Value="4"/>
      <Style.Triggers>
        <Trigger Property="IsSelected" Value="True"><Setter Property="Background" Value="#26404A"/></Trigger>
        <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#222A33"/></Trigger>
      </Style.Triggers>
    </Style>
  </Window.Resources>

  <Grid Margin="16">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <!-- origen / destino -->
    <Grid Grid.Row="0" Margin="0,0,0,10">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock Text="Origen" VerticalAlignment="Center" Foreground="#8A93A0" Margin="0,0,8,0"/>
      <TextBox x:Name="txtSrc" Grid.Column="1"/>
      <Button x:Name="btnSrc" Grid.Column="2" Content="📂" Margin="6,0,14,0" Padding="10,6"/>
      <Button x:Name="btnSrcFile" Grid.Column="3" Content="🎬" Margin="0,0,14,0" Padding="10,6" ToolTip="Elegir un archivo suelto"/>
      <TextBlock Text="Destino" Grid.Column="4" VerticalAlignment="Center" Foreground="#8A93A0" Margin="0,0,8,0"/>
      <TextBox x:Name="txtOut" Grid.Column="5" ToolTip="Vacío = subcarpeta 'comprimido' junto al origen"/>
      <Button x:Name="btnOut" Grid.Column="6" Content="📂" Margin="6,0,0,0" Padding="10,6"/>
    </Grid>

    <!-- opciones -->
    <Border Grid.Row="1" Background="#1A1F26" CornerRadius="12" Padding="14,10" Margin="0,0,0,10">
      <StackPanel>
        <WrapPanel>
          <TextBlock Text="Idioma principal" Foreground="#8A93A0" VerticalAlignment="Center" Margin="0,0,6,0"/>
          <ComboBox x:Name="cboLang" Width="70" IsEditable="True" Text="spa" Margin="0,0,18,0"/>
          <TextBlock Text="Calidad vídeo" Foreground="#8A93A0" VerticalAlignment="Center" Margin="0,0,6,0"/>
          <ComboBox x:Name="cboQ" Width="150" SelectedIndex="0" Margin="0,0,18,0">
            <ComboBoxItem Content="Automática"/><ComboBoxItem Content="22 · muy alta"/><ComboBoxItem Content="24 · alta"/>
            <ComboBoxItem Content="27 · equilibrada"/><ComboBoxItem Content="30 · muy comprimida"/>
          </ComboBox>
          <TextBlock Text="Resolución máx." Foreground="#8A93A0" VerticalAlignment="Center" Margin="0,0,6,0"/>
          <ComboBox x:Name="cboRes" Width="110" SelectedIndex="0" Margin="0,0,18,0">
            <ComboBoxItem Content="Sin cambio"/><ComboBoxItem Content="1080p"/><ComboBoxItem Content="720p"/><ComboBoxItem Content="480p"/>
          </ComboBox>
          <TextBlock Text="Audio" Foreground="#8A93A0" VerticalAlignment="Center" Margin="0,0,6,0"/>
          <ComboBox x:Name="cboAud" Width="190" SelectedIndex="0" Margin="0,0,18,0"
                    ToolTip="Máxima = se copia el audio original sin pérdida adicional">
            <ComboBoxItem Content="Máxima (copiar original)"/><ComboBoxItem Content="AAC 192 kbps"/>
            <ComboBoxItem Content="AAC 160 kbps"/><ComboBoxItem Content="AAC 128 kbps"/><ComboBoxItem Content="AAC 96 kbps"/>
          </ComboBox>
          <CheckBox x:Name="chkRec"   Content="Subcarpetas" IsChecked="True" Margin="0,0,14,0"/>
          <CheckBox x:Name="chkForce" Content="Reprocesar hechos" Margin="0,0,14,0"/>
          <CheckBox x:Name="chkDry"   Content="Solo simular"/>
        </WrapPanel>
        <WrapPanel Margin="0,8,0,0">
          <TextBlock Text="Audio detectado:" Foreground="#8A93A0" VerticalAlignment="Center" Margin="0,0,8,0"/>
          <WrapPanel x:Name="pnlALang" VerticalAlignment="Center"/>
          <TextBlock Text="   Subtítulos:" Foreground="#8A93A0" VerticalAlignment="Center" Margin="10,0,8,0"/>
          <WrapPanel x:Name="pnlSLang" VerticalAlignment="Center"/>
          <TextBlock x:Name="lblLangHint" Text="(pulsa Analizar para detectarlos)" Foreground="#5A6470" VerticalAlignment="Center"/>
        </WrapPanel>
      </StackPanel>
    </Border>

    <!-- lista + panel lateral -->
    <Grid Grid.Row="2">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/><ColumnDefinition Width="300"/>
      </Grid.ColumnDefinitions>
      <Border Background="#1A1F26" CornerRadius="12" Padding="4">
        <ListView x:Name="lst" SelectionMode="Extended">
          <ListView.View>
            <GridView>
              <GridViewColumn Width="34">
                <GridViewColumn.CellTemplate><DataTemplate>
                  <CheckBox IsChecked="{Binding Sel, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"/>
                </DataTemplate></GridViewColumn.CellTemplate>
              </GridViewColumn>
              <GridViewColumn Header="Vídeo" Width="330" DisplayMemberBinding="{Binding Name}"/>
              <GridViewColumn Header="Carpeta" Width="150" DisplayMemberBinding="{Binding Dir}"/>
              <GridViewColumn Header="Tamaño" Width="76" DisplayMemberBinding="{Binding SizeMB}"/>
              <GridViewColumn Header="Duración" Width="76" DisplayMemberBinding="{Binding Dur}"/>
              <GridViewColumn Header="Vídeo" Width="64" DisplayMemberBinding="{Binding Codec}"/>
              <GridViewColumn Header="Audio" Width="90" DisplayMemberBinding="{Binding Audio}"/>
              <GridViewColumn Header="Subs" Width="70" DisplayMemberBinding="{Binding Subs}"/>
              <GridViewColumn Header="Estado" Width="110" DisplayMemberBinding="{Binding Estado}"/>
            </GridView>
          </ListView.View>
        </ListView>
      </Border>
      <Border Grid.Column="1" Background="#1A1F26" CornerRadius="12" Margin="10,0,0,0" Padding="12">
        <StackPanel>
          <Border CornerRadius="9" Background="#0F1216" Height="152">
            <Image x:Name="imgPrev" Stretch="Uniform"/>
          </Border>
          <TextBlock x:Name="lblPrevName" Margin="0,10,0,2" TextWrapping="Wrap" FontWeight="SemiBold"/>
          <TextBlock x:Name="lblPrevInfo" Foreground="#8A93A0" TextWrapping="Wrap"/>
          <Separator Margin="0,12" Background="#333B46"/>
          <Button x:Name="btnMarkAll"  Content="Marcar todos" Margin="0,0,0,6"/>
          <Button x:Name="btnMarkNone" Content="Desmarcar todos" Margin="0,0,0,6"/>
          <Button x:Name="btnDelSel"   Content="🗑  Eliminar marcados" Background="#4A2830" Margin="0,10,0,6"
                  ToolTip="Envía los archivos marcados a la Papelera de reciclaje"/>
          <Button x:Name="btnDelDir"   Content="🗑  Eliminar carpeta…" Background="#4A2830"
                  ToolTip="Envía una carpeta entera a la Papelera de reciclaje"/>
        </StackPanel>
      </Border>
    </Grid>

    <!-- acciones -->
    <Grid Grid.Row="3" Margin="0,12,0,0">
      <StackPanel Orientation="Horizontal">
        <Button x:Name="btnScan"   Content="🔍  Analizar" Background="#20444F" Padding="18,9" Margin="0,0,10,0"/>
        <Button x:Name="btnRun"    Content="▶  Comprimir marcados" Background="#1E4D33" Padding="18,9" Margin="0,0,10,0"/>
        <Button x:Name="btnCancel" Content="■  Cancelar" IsEnabled="False" Padding="18,9" Margin="0,0,10,0"/>
        <Button x:Name="btnOpen"   Content="Abrir destino" Padding="18,9"/>
      </StackPanel>
      <TextBlock x:Name="lblProg" Text="Listo." HorizontalAlignment="Right" VerticalAlignment="Center" Foreground="#6CE8D0"/>
    </Grid>

    <!-- log -->
    <Expander Grid.Row="4" Header="Registro" Foreground="#8A93A0" Margin="0,10,0,0" IsExpanded="False">
      <Border Background="#12161B" CornerRadius="9" Margin="0,6,0,0">
        <TextBox x:Name="txtLog" Height="140" IsReadOnly="True" Background="Transparent" BorderThickness="0"
                 FontFamily="Consolas" FontSize="12" VerticalScrollBarVisibility="Auto" TextWrapping="NoWrap"/>
      </Border>
    </Expander>
  </Grid>
</Window>
"@

$win = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$ui = @{}
foreach ($n in @("txtSrc","btnSrc","btnSrcFile","txtOut","btnOut","cboLang","cboQ","cboRes","cboAud","chkRec","chkForce","chkDry",
                 "pnlALang","pnlSLang","lblLangHint","lst","imgPrev","lblPrevName","lblPrevInfo","btnMarkAll","btnMarkNone",
                 "btnDelSel","btnDelDir","btnScan","btnRun","btnCancel","btnOpen","lblProg","txtLog")) {
    $ui[$n] = $win.FindName($n)
}

$rows = New-Object System.Collections.ObjectModel.ObservableCollection[VideoRow]
$ui.lst.ItemsSource = $rows

$script:proc = $null; $script:logFile = $null; $script:errFile = $null; $script:probeFile = $null; $script:probeRead = 0

function Read-Shared([string]$path) {
    if (-not $path -or -not (Test-Path -LiteralPath $path)) { return "" }
    try {
        $fs = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        $sr = New-Object IO.StreamReader($fs); $t = $sr.ReadToEnd(); $sr.Close(); $fs.Close(); return $t
    } catch { return "" }
}
function Get-EffectiveOutput {
    if ($ui.txtOut.Text.Trim()) { return $ui.txtOut.Text.Trim() }
    $src = $ui.txtSrc.Text.Trim(); if (-not $src) { return "" }
    try { $it = Get-Item -LiteralPath $src -ErrorAction Stop
          $base = if ($it.PSIsContainer) { $it.FullName } else { $it.DirectoryName }
          return (Join-Path $base "comprimido") } catch { return "" }
}
function New-LangChip($panel, [string]$code, [bool]$on) {
    $cb = New-Object System.Windows.Controls.CheckBox
    $cb.Content = $code; $cb.IsChecked = $on; $cb.Margin = "0,0,10,0"
    $cb.Foreground = [Windows.Media.Brushes]::White
    $panel.Children.Add($cb) | Out-Null
}
function Get-CheckedLangs($panel) {
    @($panel.Children | Where-Object { $_.IsChecked } | ForEach-Object { [string]$_.Content })
}

# ---------- selección de rutas ----------
$ui.btnSrc.Add_Click({
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = "Elige la carpeta con los vídeos"
    if ($d.ShowDialog() -eq 'OK') { $ui.txtSrc.Text = $d.SelectedPath }
})
$ui.btnSrcFile.Add_Click({
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Filter = "Vídeos|*.mkv;*.mp4;*.avi;*.m4v;*.mov;*.wmv;*.ts;*.webm|Todos|*.*"
    if ($d.ShowDialog() -eq 'OK') { $ui.txtSrc.Text = $d.FileName }
})
$ui.btnOut.Add_Click({
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = "Carpeta de destino"
    if ($d.ShowDialog() -eq 'OK') { $ui.txtOut.Text = $d.SelectedPath }
})
$ui.btnOpen.Add_Click({
    $o = Get-EffectiveOutput
    if ($o) { New-Item -ItemType Directory -Force $o | Out-Null; Start-Process explorer.exe $o }
})

# ---------- analizar ----------
$ui.btnScan.Add_Click({
    $src = $ui.txtSrc.Text.Trim()
    if (-not $src -or -not (Test-Path -LiteralPath $src)) {
        [void][System.Windows.MessageBox]::Show("Indica un archivo o carpeta de origen válido.","Origen",'OK','Warning'); return
    }
    $rows.Clear(); $ui.pnlALang.Children.Clear(); $ui.pnlSLang.Children.Clear(); $ui.lblLangHint.Text = " detectando…"
    $exts = @(".mkv",".mp4",".avi",".m4v",".mov",".wmv",".ts",".webm")
    $item = Get-Item -LiteralPath $src
    $files = if ($item.PSIsContainer) {
        Get-ChildItem -LiteralPath $src -File -Recurse:($ui.chkRec.IsChecked) | Where-Object { $exts -contains $_.Extension.ToLower() }
    } else { @($item) }
    $outDir = Get-EffectiveOutput
    $files = @($files | Where-Object { $_.DirectoryName -ne $outDir } | Sort-Object FullName)
    foreach ($f in $files) {
        $r = New-Object VideoRow
        $r.Name = $f.Name; $r.Dir = $f.Directory.Name; $r.Path = $f.FullName
        $r.SizeMB = "{0:n0} MB" -f ($f.Length/1MB); $r.Estado = "…"
        $rows.Add($r)
    }
    $ui.lblProg.Text = "$($rows.Count) vídeo(s) encontrados. Leyendo pistas…"
    if (-not $rows.Count) { $ui.lblLangHint.Text = "(nada que analizar)"; return }

    # sondeo de pistas en segundo plano (JSONL por línea)
    $listFile = Join-Path $env:TEMP "shrinkgui_scan_in.txt"
    $script:probeFile = Join-Path $env:TEMP "shrinkgui_scan_out.jsonl"
    $files.FullName | Set-Content -LiteralPath $listFile -Encoding UTF8
    "" | Set-Content -LiteralPath $script:probeFile -Encoding UTF8
    $script:probeRead = 0
    $probeCmd = "Get-Content -LiteralPath '$listFile' | ForEach-Object { " +
        "`$p = ffprobe -v error -show_entries 'stream=codec_type,codec_name:stream_tags=language:format=duration' -of json -- `$_ 2>`$null | ConvertFrom-Json; " +
        "`$v = @(`$p.streams | Where-Object codec_type -eq 'video')[0]; " +
        "`$a = @(`$p.streams | Where-Object codec_type -eq 'audio' | ForEach-Object { if (`$_.tags.language) { `$_.tags.language } else { '?' } }); " +
        "`$s = @(`$p.streams | Where-Object codec_type -eq 'subtitle' | ForEach-Object { if (`$_.tags.language) { `$_.tags.language } else { '?' } }); " +
        "[pscustomobject]@{ path=`$_; codec=`$v.codec_name; dur=[int][double]`$p.format.duration; audio=`$a; subs=`$s } | ConvertTo-Json -Compress " +
        "} | Add-Content -LiteralPath '$($script:probeFile)' -Encoding UTF8"
    Start-Process pwsh -ArgumentList @('-NoProfile','-Command',$probeCmd) -WindowStyle Hidden | Out-Null
    $timerProbe.Start()
})

# ---------- timer: resultados del sondeo ----------
$timerProbe = New-Object System.Windows.Threading.DispatcherTimer
$timerProbe.Interval = [TimeSpan]::FromMilliseconds(400)
$timerProbe.Add_Tick({
    $txt = Read-Shared $script:probeFile
    if (-not $txt) { return }
    $lines = @($txt -split "`n" | Where-Object { $_.Trim() })
    $langsA = @{}; $langsS = @{}
    foreach ($cb in $ui.pnlALang.Children) { $langsA[[string]$cb.Content] = $true }
    foreach ($cb in $ui.pnlSLang.Children) { $langsS[[string]$cb.Content] = $true }
    for ($i = $script:probeRead; $i -lt $lines.Count; $i++) {
        try { $o = $lines[$i] | ConvertFrom-Json } catch { continue }
        $row = $rows | Where-Object { $_.Path -eq $o.path } | Select-Object -First 1
        if ($row) {
            $row.Codec = "$($o.codec)"
            $row.Dur = "{0}:{1:d2}:{2:d2}" -f [int]($o.dur/3600), [int](($o.dur%3600)/60), [int]($o.dur%60)
            $row.Audio = (@($o.audio) | Select-Object -Unique) -join "+"
            $row.Subs = (@($o.subs) | Select-Object -Unique) -join "+"
            $row.Estado = "listo"
        }
        foreach ($l in @($o.audio)) { if ($l -and -not $langsA[$l]) { New-LangChip $ui.pnlALang $l $true;  $langsA[$l] = $true } }
        foreach ($l in @($o.subs))  { if ($l -and -not $langsS[$l]) { New-LangChip $ui.pnlSLang $l $true;  $langsS[$l] = $true } }
    }
    $script:probeRead = $lines.Count
    if ($lines.Count -ge $rows.Count -and $rows.Count -gt 0) {
        $timerProbe.Stop()
        $ui.lblLangHint.Text = ""
        $ui.lblProg.Text = "$($rows.Count) vídeo(s) analizados."
        if (-not $ui.pnlSLang.Children.Count) { $ui.lblLangHint.Text = "(sin subtítulos detectados)" }
    }
})

# ---------- previsualización ----------
$ui.lst.Add_SelectionChanged({
    $r = $ui.lst.SelectedItem
    if (-not $r) { return }
    $ui.lblPrevName.Text = $r.Name
    $ui.lblPrevInfo.Text = "$($r.Dir)  ·  $($r.SizeMB)  ·  $($r.Dur)`n$($r.Codec)  ·  audio: $($r.Audio)" +
                           $(if ($r.Subs) { "  ·  subs: $($r.Subs)" } else { "" })
    $md5 = [System.BitConverter]::ToString([System.Security.Cryptography.MD5]::Create().ComputeHash(
           [Text.Encoding]::UTF8.GetBytes($r.Path))).Replace("-","")
    $thumb = Join-Path $thumbDir "$md5.jpg"
    if (-not (Test-Path -LiteralPath $thumb)) {
        ffmpeg -v error -ss 120 -i $r.Path -frames:v 1 -vf "scale=480:-2" -q:v 4 -y $thumb 2>$null
        if (-not (Test-Path -LiteralPath $thumb)) {
            ffmpeg -v error -ss 3 -i $r.Path -frames:v 1 -vf "scale=480:-2" -q:v 4 -y $thumb 2>$null
        }
    }
    if (Test-Path -LiteralPath $thumb) {
        $bmp = New-Object System.Windows.Media.Imaging.BitmapImage
        $bmp.BeginInit(); $bmp.UriSource = $thumb; $bmp.CacheOption = 'OnLoad'; $bmp.EndInit()
        $ui.imgPrev.Source = $bmp
    } else { $ui.imgPrev.Source = $null }
})

# ---------- marcar / eliminar ----------
$ui.btnMarkAll.Add_Click({ foreach ($r in $rows) { $r.Sel = $true } })
$ui.btnMarkNone.Add_Click({ foreach ($r in $rows) { $r.Sel = $false } })

$ui.btnDelSel.Add_Click({
    $sel = @($rows | Where-Object Sel)
    if (-not $sel) { [void][System.Windows.MessageBox]::Show("No hay vídeos marcados.","Eliminar",'OK','Information'); return }
    $mb = ($sel | ForEach-Object { (Get-Item -LiteralPath $_.Path).Length } | Measure-Object -Sum).Sum / 1MB
    $q = [System.Windows.MessageBox]::Show(
        ("Enviar {0} vídeo(s) ({1:n0} MB) a la Papelera de reciclaje?" -f $sel.Count, $mb),
        "Eliminar marcados",'YesNo','Warning')
    if ($q -ne 'Yes') { return }
    foreach ($r in $sel) {
        try {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($r.Path,'OnlyErrorDialogs','SendToRecycleBin')
            $rows.Remove($r) | Out-Null
        } catch { $r.Estado = "error al borrar" }
    }
    $ui.lblProg.Text = "Enviados a la Papelera."
})

$ui.btnDelDir.Add_Click({
    $dirs = @($rows | Where-Object Sel | ForEach-Object { Split-Path $_.Path -Parent } | Select-Object -Unique)
    if (-not $dirs) { $s = $ui.txtSrc.Text.Trim(); if ($s -and (Test-Path -LiteralPath $s -PathType Container)) { $dirs = @($s) } }
    if (-not $dirs) { [void][System.Windows.MessageBox]::Show("Marca algún vídeo o indica una carpeta de origen.","Eliminar carpeta",'OK','Information'); return }
    $q = [System.Windows.MessageBox]::Show(
        ("Enviar estas {0} carpeta(s) COMPLETAS a la Papelera?`n`n{1}" -f $dirs.Count, ($dirs -join "`n")),
        "Eliminar carpeta",'YesNo','Warning')
    if ($q -ne 'Yes') { return }
    foreach ($d in $dirs) {
        try { [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($d,'OnlyErrorDialogs','SendToRecycleBin') } catch {}
    }
    foreach ($r in @($rows | Where-Object { $dirs -contains (Split-Path $_.Path -Parent) })) { $rows.Remove($r) | Out-Null }
    $ui.lblProg.Text = "Carpeta(s) enviadas a la Papelera."
})

# ---------- comprimir ----------
$timerRun = New-Object System.Windows.Threading.DispatcherTimer
$timerRun.Interval = [TimeSpan]::FromMilliseconds(400)
$timerRun.Add_Tick({
    if (-not $script:proc) { return }
    $out = Read-Shared $script:logFile
    if ($out -and $out.Length -ne $ui.txtLog.Text.Length) {
        $ui.txtLog.Text = $out; $ui.txtLog.ScrollToEnd()
    }
    $err = Read-Shared $script:errFile
    if ($err) {
        $last = ($err -split "[`r`n]" | Where-Object { $_.Trim() } | Select-Object -Last 1)
        if ($last) { $ui.lblProg.Text = $last.Trim() }
    }
    if ($script:proc.HasExited) {
        $timerRun.Stop()
        $code = $script:proc.ExitCode
        $ui.lblProg.Text = if ($code -eq 0) { "Terminado ✓" } else { "Terminado con errores (código $code)" }
        $ui.btnRun.IsEnabled = $true; $ui.btnCancel.IsEnabled = $false
        $script:proc = $null
    }
})

$ui.btnRun.Add_Click({
    $sel = @($rows | Where-Object Sel)
    if (-not $sel) { [void][System.Windows.MessageBox]::Show("Analiza y marca al menos un vídeo.","Comprimir",'OK','Warning'); return }

    $common = @()
    if ($ui.txtOut.Text.Trim()) { $common += @('-Output', $ui.txtOut.Text.Trim()) }
    elseif ((Get-EffectiveOutput)) { $common += @('-Output', (Get-EffectiveOutput)) }
    if ($ui.cboLang.Text.Trim()) { $common += @('-Lang', $ui.cboLang.Text.Trim()) }
    $aLangs = Get-CheckedLangs $ui.pnlALang
    if ($aLangs) { $common += @('-KeepLangs', ($aLangs -join ',')) }
    $sLangs = Get-CheckedLangs $ui.pnlSLang
    if ($ui.pnlSLang.Children.Count -and -not $sLangs) { $common += '-NoSubs' }
    elseif ($sLangs -and $sLangs.Count -lt $ui.pnlSLang.Children.Count) { $common += @('-SubLangs', ($sLangs -join ',')) }
    $q = @{1=22;2=24;3=27;4=30}[$ui.cboQ.SelectedIndex]; if ($q) { $common += @('-Quality',$q) }
    $res = @{1=1080;2=720;3=480}[$ui.cboRes.SelectedIndex]; if ($res) { $common += @('-MaxHeight',$res) }
    $ab = @{0=0;1=192;2=160;3=128;4=96}[$ui.cboAud.SelectedIndex]
    $common += @('-AudioBitrate',$ab)
    if ($ui.chkForce.IsChecked) { $common += '-Force' }
    if ($ui.chkDry.IsChecked)   { $common += '-DryRun' }

    # lista de marcados -> guion que llama al motor fichero a fichero
    $listFile = Join-Path $env:TEMP "shrinkgui_run_list.txt"
    $sel.Path | Set-Content -LiteralPath $listFile -Encoding UTF8
    $argStr = ($common | ForEach-Object { "'$($_ -replace "'","''")'" }) -join ','
    $runCmd = "Get-Content -LiteralPath '$listFile' | ForEach-Object { & '$engine' -Path `$_ @($argStr) }"

    $id = [Guid]::NewGuid().ToString("N")
    $script:logFile = Join-Path $env:TEMP "shrink_$id.out"
    $script:errFile = Join-Path $env:TEMP "shrink_$id.err"
    "" | Set-Content $script:logFile; "" | Set-Content $script:errFile
    $ui.txtLog.Text = ""; $ui.lblProg.Text = "Procesando $($sel.Count) vídeo(s)…"

    $script:proc = Start-Process pwsh -ArgumentList @('-NoProfile','-Command',$runCmd) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $script:logFile -RedirectStandardError $script:errFile
    $ui.btnRun.IsEnabled = $false; $ui.btnCancel.IsEnabled = $true
    $timerRun.Start()
})

# ---------- cancelar: matar el ÁRBOL entero (pwsh + ffmpeg hijos) ----------
function Stop-ProcTree {
    if ($script:proc -and -not $script:proc.HasExited) {
        # /T = incluye procesos hijos; sin esto el ffmpeg sobrevive al cancelar
        Start-Process taskkill -ArgumentList @('/PID',"$($script:proc.Id)",'/T','/F') -WindowStyle Hidden -Wait
    }
}
$ui.btnCancel.Add_Click({
    Stop-ProcTree
    $timerRun.Stop(); $script:proc = $null
    $ui.lblProg.Text = "Cancelado (proceso y ffmpeg detenidos)."
    $ui.btnRun.IsEnabled = $true; $ui.btnCancel.IsEnabled = $false
})
$win.Add_Closing({
    param($s, $e)
    if ($script:proc -and -not $script:proc.HasExited) {
        $r = [System.Windows.MessageBox]::Show("Hay una compresión en curso. ¿Cerrar y cancelarla?","Salir",'YesNo','Warning')
        if ($r -eq 'Yes') { Stop-ProcTree } else { $e.Cancel = $true }
    }
})

[void]$win.ShowDialog()
