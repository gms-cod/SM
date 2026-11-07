# StartupMonitor www — локальный просмотр без python/node (чистый PowerShell).
# Запуск: правый клик -> Run with PowerShell, затем открыть http://localhost:8099/
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$l = New-Object Net.HttpListener
$l.Prefixes.Add('http://localhost:8099/')
$l.Start()
Write-Host "Serving $root at http://localhost:8099/ (Ctrl+C to stop)"
$mime = @{'.html'='text/html; charset=utf-8'; '.css'='text/css; charset=utf-8'; '.js'='text/javascript; charset=utf-8'; '.svg'='image/svg+xml'; '.ico'='image/x-icon'; '.png'='image/png'}
while ($l.IsListening) {
  $c = $l.GetContext()
  $p = $c.Request.Url.LocalPath.TrimStart('/')
  if ([string]::IsNullOrEmpty($p)) { $p = 'index.html' }
  $f = Join-Path $root $p
  if (Test-Path $f -PathType Leaf) {
    $b = [IO.File]::ReadAllBytes($f)
    $ct = $mime[[IO.Path]::GetExtension($f).ToLower()]
    if (-not $ct) { $ct = 'application/octet-stream' }
    $c.Response.ContentType = $ct
    $c.Response.OutputStream.Write($b, 0, $b.Length)
  } else { $c.Response.StatusCode = 404 }
  $c.Response.Close()
}
