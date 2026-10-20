# Read-only dump of WLMOUSE settings over the vendor HID feature channel.
# Only "get" commands (bit 7 of the command byte set) are sent; nothing is written to the mouse.
#
# Frame (65 bytes as seen by Windows HidD_*Feature):
#   [0] report id (0)   [1] status: 0 in request, 0xA1 = OK in response, <0xA1 busy, >0xA1 error
#   [2] 0               [3] target (2 = mouse, 0 = receiver)   [4] payload length
#   [5] command class   [6] command (|0x80 = get)               [7..] payload (profile index first)

$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Runtime.InteropServices; using Microsoft.Win32.SafeHandles;
public static class WlmHid {
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sa, uint disp, uint flags, IntPtr t);
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] b, int len);
  [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] b, int len);
}
'@

$dev = Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^HID\\VID_36A7&PID_[0-9A-F]{4}&MI_02' } | Select-Object -First 1
if (-not $dev) { throw 'WLMOUSE config interface not found' }
$path = '\\?\' + ($dev.InstanceId -replace '\\','#') + '#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$h = [WlmHid]::CreateFile($path, [uint32]3221225472, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
if ($h.IsInvalid) { throw "cannot open $path" }

function Invoke-Wlm([byte]$target, [byte]$len, [byte]$class, [byte]$cmd, [byte[]]$payload = @()) {
  if (($cmd -band 0x80) -eq 0) { throw 'refusing to send a non-get command' }
  $req = New-Object byte[] 65
  $req[3] = $target; $req[4] = $len; $req[5] = $class; $req[6] = $cmd
  for ($i = 0; $i -lt $payload.Length; $i++) { $req[7 + $i] = $payload[$i] }
  for ($attempt = 0; $attempt -lt 5; $attempt++) {
    [void][WlmHid]::HidD_SetFeature($h, $req, 65)
    for ($poll = 0; $poll -lt 30; $poll++) {
      Start-Sleep -Milliseconds 30
      $res = New-Object byte[] 65
      if (-not [WlmHid]::HidD_GetFeature($h, $res, 65)) { continue }
      if ($res[1] -eq 0xA1 -and $res[6] -eq $cmd) { return $res }
      if ($res[1] -gt 0xA1) { break }   # error: resend
    }
  }
  throw ('no answer to cmd 0x{0:X2}' -f $cmd)
}
function U16($b, $i) { ([int]$b[$i] -shl 8) + [int]$b[$i + 1] }

try {
  $r = Invoke-Wlm 2 16 0 0x81;  $fw = '{0}.{1}.{2}.{3}' -f $r[7],$r[8],$r[9],$r[10]
  $r = Invoke-Wlm 2 20 0 0x82;  $sn = [Text.Encoding]::ASCII.GetString($r, 9, 15).Trim([char]0)
  $r = Invoke-Wlm 2 2 0 0x83;   $charging = $r[7] -eq 1; $bat = $r[8]
  $r = Invoke-Wlm 2 1 0 0x85;   $p = [byte]$r[7]
  $r = Invoke-Wlm 2 2 1 0x8C;   $dpiMax = U16 $r 7

  $r = Invoke-Wlm 2 10 1 0x81 @($p, 6)
  $stages = for ($k = 0; $k -lt $r[8]; $k++) { '{0}x{1}' -f (U16 $r (9 + 4*$k)), (U16 $r (11 + 4*$k)) }
  $r = Invoke-Wlm 2 2 1 0x82 @($p); $activeStage = $r[8]
  $r = Invoke-Wlm 2 5 1 0x97 @($p); $special = if ($r[12]) { '{0}x{1}' -f (U16 $r 8), (U16 $r 10) } else { 'off' }

  $pollMap = @{ 8=125; 4=250; 2=500; 1=1000; 16=1000; 32=2000; 64=4000; 128=8000 }
  $r = Invoke-Wlm 2 2 1 0x80 @($p); $poll = $pollMap[[int]$r[8]]
  $r = Invoke-Wlm 2 2 0 0x88 @($p); $debounce = $r[8]
  $r = Invoke-Wlm 2 3 0 0x87 @($p); $sleep = U16 $r 8
  $r = Invoke-Wlm 2 3 1 0x88 @($p); $l = $r[8]
  $lod = if ($l -ge 128) { ($l - 128) / 10 } elseif ($l -eq 1 -or $l -eq 2) { $l } else { (($l -shr 4) * 10 + ($l -band 15)) / 10 }

  $flags = [ordered]@{}
  foreach ($f in @(@('Motion sync',0x89),@('Ripple control',0x8A),@('High-speed mode',0x8B),@('Separate X/Y DPI',0x8D),@('Angle snapping',0x84),@('Tracking mode',0x93))) {
    $r = Invoke-Wlm 2 2 1 $f[1] @($p); $flags[$f[0]] = if ($r[8] -eq 1) { 'on' } else { 'off' }
  }
  $r = Invoke-Wlm 2 2 1 0x94 @($p); $angleTune = if ($r[8] -gt 127) { $r[8] - 256 } else { $r[8] }

  [ordered]@{
    'Firmware'          = $fw
    'Serial'            = $sn
    'Battery'           = "$bat%" + $(if ($charging) { ' (charging)' } else { '' })
    'Active profile'    = $p
    'Sensor max DPI'    = $dpiMax
    'DPI stages'        = ($stages -join ', ')
    'Active DPI stage'  = $activeStage
    'Custom active DPI' = $special
    'Polling rate (Hz)' = $poll
    'Debounce (ms)'     = $debounce
    'Sleep after (s)'   = $(if ($sleep -in 0,65280,65535) { 'disabled' } else { $sleep })
    'Lift-off (mm)'     = $lod
    'Angle tune (deg)'  = $angleTune
  } + $flags | Format-Table -HideTableHeaders -AutoSize | Out-String
} finally { $h.Close() }
