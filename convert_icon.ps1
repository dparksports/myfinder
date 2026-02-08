Add-Type -AssemblyName System.Drawing
$pngPath = "C:\Users\k2\myfinder\MyFinder\Assets\app_icon.png"
$icoPath = "C:\Users\k2\myfinder\MyFinder\Assets\app_icon.ico"

$bmp = [System.Drawing.Bitmap]::FromFile($pngPath)
$iconHandle = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)

$fileStream = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fileStream)
$fileStream.Close()

$icon.Dispose()
$bmp.Dispose()
Write-Host "Converted $pngPath to $icoPath"
