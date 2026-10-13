$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path -Parent $PSScriptRoot
$assetPath = Join-Path $taskRoot 'src\MyWords\Assets'
New-Item -ItemType Directory -Force -Path $assetPath | Out-Null
$taskImages = [System.Collections.Generic.List[byte[]]]::new()
foreach ($taskSize in @(16, 32, 48, 256)) {
    $taskBitmap = [System.Drawing.Bitmap]::new($taskSize, $taskSize)
    $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $taskBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#176D65'))
    $taskShape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $taskRadius = [single]($taskSize * 0.4)
    $taskEdge = [single]($taskSize - 1)
    $taskShape.AddArc(0, 0, $taskRadius, $taskRadius, 180, 90)
    $taskShape.AddArc(($taskEdge - $taskRadius), 0, $taskRadius, $taskRadius, 270, 90)
    $taskShape.AddArc(($taskEdge - $taskRadius), ($taskEdge - $taskRadius), $taskRadius, $taskRadius, 0, 90)
    $taskShape.AddArc(0, ($taskEdge - $taskRadius), $taskRadius, $taskRadius, 90, 90)
    $taskShape.CloseFigure()
    $taskGraphics.FillPath($taskBrush, $taskShape)
    $taskFont = [System.Drawing.Font]::new('Georgia', [single]($taskSize * 0.78), [System.Drawing.FontStyle]::Italic, [System.Drawing.GraphicsUnit]::Pixel)
    $taskFormat = [System.Drawing.StringFormat]::new()
    $taskFormat.Alignment = [System.Drawing.StringAlignment]::Center
    $taskFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
    $taskGraphics.DrawString('w', $taskFont, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, [single](-$taskSize * 0.06), $taskSize, $taskSize), $taskFormat)
    $taskStream = [System.IO.MemoryStream]::new()
    $taskBitmap.Save($taskStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $taskImages.Add($taskStream.ToArray())
    if ($taskSize -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $taskRoot 'manggo-plugin\icon.png'), $taskStream.ToArray()) }
    $taskStream.Dispose(); $taskFont.Dispose(); $taskFormat.Dispose(); $taskShape.Dispose(); $taskBrush.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
}
$taskOutput = [System.IO.File]::Create((Join-Path $assetPath 'app.ico'))
$taskWriter = [System.IO.BinaryWriter]::new($taskOutput)
$taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]4)
$taskOffset = 6 + 16 * 4
for ($taskI = 0; $taskI -lt 4; $taskI++) {
    $taskDimension = @(16, 32, 48, 0)[$taskI]
    $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
    $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
    $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
    $taskWriter.Write([uint32]$taskImages[$taskI].Length); $taskWriter.Write([uint32]$taskOffset)
    $taskOffset += $taskImages[$taskI].Length
}
foreach ($taskBytes in $taskImages) { $taskWriter.Write($taskBytes) }
$taskWriter.Dispose()
Write-Output 'Created application and plugin icons.'
