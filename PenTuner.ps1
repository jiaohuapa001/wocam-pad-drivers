param([switch]$ApplyInitial, [switch]$Check)
$ErrorActionPreference = 'Stop'
$utility = 'C:\Program Files\Tablet\Wacom\PrefUtil.exe'
$live = Join-Path $env:APPDATA 'WTablet\Wacom_Tablet.dat'
$baseline = Join-Path $PSScriptRoot 'original.wacomprefs'
function Run-Utility([string]$operation, [string]$file) {
    if ($file.Contains('"')) { throw 'Invalid file path' }
    & $utility /silent $operation $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Wacom utility failed: $LASTEXITCODE" }
}
function Get-Pen($doc) {
    $nodes = @($doc.SelectNodes('//TabletArray/ArrayElement[contains(TabletCommInterface/CommPort,"VID_056A") and contains(TabletCommInterface/CommPort,"PID_037A")]/TabletTransducerArray/ArrayElement[ApplicationAssociated="0"]'))
    if ($nodes.Count -ne 1) { throw 'Expected exactly one CTL-472 global pen profile; no settings changed.' }
    return $nodes[0]
}
function Read-Doc([string]$path) {
    $d = New-Object System.Xml.XmlDocument
    $d.PreserveWhitespace = $true
    $d.Load($path)
    return ,$d
}
function Get-State {
    $d = Read-Doc $live
    $pen = Get-Pen $d
    $tip = $pen.TransducerTipButtonSettings
    [pscustomobject]@{ Down = [int]$tip.UpperPressureThreshold.InnerText; Up = [int]$tip.LowerPressureThreshold.InnerText; Maximum = [int]$tip.PressureResolution.InnerText; DoubleClick = $pen.DoubleClickOnOff.InnerText }
}
function Apply-Thresholds([double]$down, [double]$up) {
    if ($down -le $up -or $up -lt 0 -or $down -gt 95) { throw 'Require: 0 <= release < press <= 95 percent.' }
    if (-not (Test-Path -LiteralPath $baseline)) { throw 'Original backup is missing.' }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $before = Join-Path $PSScriptRoot "before-$stamp.wacomprefs"
    Run-Utility '/backup' $before
    $d = Read-Doc $before
    $pen = Get-Pen $d
    $tip = $pen.TransducerTipButtonSettings
    $max = [int]$tip.PressureResolution.InnerText
    $press = [int][Math]::Round($max * $down / 100)
    $release = [int][Math]::Round($max * $up / 100)
    if ($press -le $release) { throw 'Thresholds round to the same value; increase their separation.' }
    $tip.UpperPressureThreshold.InnerText = [string]$press
    $tip.LowerPressureThreshold.InnerText = [string]$release
    $pen.DoubleClickOnOff.InnerText = 'false'
    $target = Join-Path $PSScriptRoot "applied-$stamp.wacomprefs"
    $d.Save($target)
    Run-Utility '/restore' $target
    $state = Get-State
    if ($state.Down -ne $press -or $state.Up -ne $release -or $state.DoubleClick -ne 'false') {
        Run-Utility '/restore' $before
        throw 'Driver did not retain requested settings. Restored the previous backup.'
    }
    return $state
}
if ($Check) { Get-State | ConvertTo-Json; exit }
if ($ApplyInitial) { Apply-Thresholds 25 24 | ConvertTo-Json; exit }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.Text = 'CTL-472 Pen Threshold Tuner'
$form.ClientSize = New-Object System.Drawing.Size(540,330)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(20,15)
$label.Size = New-Object System.Drawing.Size(500,70)
$label.Text = "Wacom pressure threshold experiment (global profile).`r`nHigher release pressure may stop faint connecting strokes earlier.`r`nToo high can remove light strokes. Test in Xournal++ after applying."
$form.Controls.Add($label)
$state = Get-State
$inputs = @()
$i = 0
foreach ($name in @('Press threshold (%)', 'Release threshold (%)')) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $name
    $l.Location = New-Object System.Drawing.Point(20,(100 + $i*45))
    $l.Size = New-Object System.Drawing.Size(250,25)
    $form.Controls.Add($l)
    $n = New-Object System.Windows.Forms.NumericUpDown
    $n.Location = New-Object System.Drawing.Point(300,(95 + $i*45))
    $n.Minimum = 0; $n.Maximum = 95; $n.DecimalPlaces = 1; $n.Increment = 1
    if ($i -eq 0) { $n.Value = [decimal]($state.Down*100/$state.Maximum) } else { $n.Value = [decimal]($state.Up*100/$state.Maximum) }
    $form.Controls.Add($n); $inputs += $n; $i++
}
$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(20,245)
$status.Size = New-Object System.Drawing.Size(500,75)
$status.Text = "Apply disables tip double-click and briefly restarts the driver.`r`nPressure curve and Windows Ink are preserved.`r`nSaved values are verified; handwriting improvement requires testing."
$form.Controls.Add($status)
$apply = New-Object System.Windows.Forms.Button
$apply.Text = 'Apply thresholds'; $apply.Size = New-Object System.Drawing.Size(200,35)
$apply.Location = New-Object System.Drawing.Point(20,195)
$apply.Add_Click({
    try { $s = Apply-Thresholds ([double]$inputs[0].Value) ([double]$inputs[1].Value); $status.Text = "Saved: press $($s.Down), release $($s.Up), maximum $($s.Maximum).`r`nTest quick handwriting now. Restore if light strokes disappear." }
    catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Could not apply') }
})
$form.Controls.Add($apply)
$restore = New-Object System.Windows.Forms.Button
$restore.Text = 'Restore original settings'; $restore.Size = New-Object System.Drawing.Size(230,35)
$restore.Location = New-Object System.Drawing.Point(250,195)
$restore.Add_Click({
    try { Run-Utility '/restore' $baseline; $s = Get-State; $inputs[0].Value = [decimal]($s.Down*100/$s.Maximum); $inputs[1].Value = [decimal]($s.Up*100/$s.Maximum); $status.Text = 'Original full Wacom settings restored.' }
    catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Could not restore') }
})
$form.Controls.Add($restore)
[void]$form.ShowDialog()
