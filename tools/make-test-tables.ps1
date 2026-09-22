Add-Type -AssemblyName System.Drawing
$out = Split-Path $MyInvocation.MyCommand.Path
$dir = Join-Path (Split-Path $out) "test-tables"
New-Item -ItemType Directory -Force $dir | Out-Null

function Render($name, $width, $height, $font, $rows, $cols, $grid, $title) {
    # $cols: array of @{ x = px; align = 'L'|'R'|'C' }
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $brush = [System.Drawing.Brushes]::Black
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 200, 200)), 1
    $y = 14
    if ($title) {
        $tf = New-Object System.Drawing.Font $font.Name, ($font.Size + 2), ([System.Drawing.FontStyle]::Bold)
        $g.DrawString($title, $tf, $brush, 12, $y)
        $y += [int]($tf.GetHeight($g) * 1.6)
    }
    $mult = 1.55
    if ($script:spacing) { $mult = $script:spacing }
    $rowH = [int]($font.GetHeight($g) * $mult)
    $ri = 0
    foreach ($row in $rows) {
        $f = $font
        if ($ri -eq 0 -and -not $script:noBold) { $f = New-Object System.Drawing.Font $font.Name, $font.Size, ([System.Drawing.FontStyle]::Bold) }
        for ($c = 0; $c -lt $row.Count; $c++) {
            $text = $row[$c]
            if ($text -eq $null -or $text -eq '') { continue }
            $col = $cols[$c]
            $sf = New-Object System.Drawing.StringFormat
            if ($col.align -eq 'R') { $sf.Alignment = [System.Drawing.StringAlignment]::Far }
            elseif ($col.align -eq 'C') { $sf.Alignment = [System.Drawing.StringAlignment]::Center }
            else { $sf.Alignment = [System.Drawing.StringAlignment]::Near }
            $rect = New-Object System.Drawing.RectangleF $col.x, $y, $col.w, $rowH
            $g.DrawString($text, $f, $brush, $rect, $sf)
        }
        if ($grid) {
            $g.DrawLine($pen, 6, ($y + $rowH), ($width - 6), ($y + $rowH))
        }
        $y += $rowH
        $ri++
    }
    if ($grid) {
        $top = $y - $rowH * $rows.Count
        $g.DrawLine($pen, 6, $top, ($width - 6), $top)
        foreach ($col in $cols) { $g.DrawLine($pen, ($col.x - 4), $top, ($col.x - 4), $y) }
        $g.DrawLine($pen, ($width - 6), $top, ($width - 6), $y)
    }
    $path = Join-Path $dir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "wrote $path"
}

# 1. basic, small screen-size text (12px Segoe UI)
$f1 = New-Object System.Drawing.Font "Segoe UI", 9
Render "basic" 420 150 $f1 @(
    @("Item", "Qty", "Unit price", "Total"),
    @("Apples", "12", "1.20", "14.40"),
    @("Bananas", "5", "0.55", "2.75"),
    @("Cherries", "100", "0.10", "10.00"),
    @("Dragon fruit", "2", "4.99", "9.98")
) @(
    @{ x = 12; w = 120; align = 'L' },
    @{ x = 140; w = 60; align = 'R' },
    @{ x = 210; w = 90; align = 'R' },
    @{ x = 310; w = 90; align = 'R' }
) $false $null

# 2. financial with title and short headers over wide numbers
$f2 = New-Object System.Drawing.Font "Arial", 11
Render "financial" 560 220 $f2 @(
    @("Metric", "Q1", "Q2", "Q3"),
    @("Revenue", "1,200,000", "1,350,500", "1,410,250"),
    @("Cost of goods", "800,000", "905,300", "950,000"),
    @("Net income", "400,000", "445,200", "460,250")
) @(
    @{ x = 12; w = 160; align = 'L' },
    @{ x = 180; w = 110; align = 'R' },
    @{ x = 300; w = 110; align = 'R' },
    @{ x = 420; w = 110; align = 'R' }
) $false "Quarterly results (USD)"

# 3. spreadsheet-like with grid lines and tight columns
$f3 = New-Object System.Drawing.Font "Calibri", 11
Render "grid" 520 200 $f3 @(
    @("ID", "Product", "Category", "Stock", "Price"),
    @("1", "Notebook", "Stationery", "240", "2.50"),
    @("2", "Desk lamp", "Furniture", "18", "34.00"),
    @("3", "USB-C cable", "Electronics", "560", "7.99"),
    @("4", "Chair", "Furniture", "7", "129.00")
) @(
    @{ x = 12; w = 40; align = 'L' },
    @{ x = 60; w = 120; align = 'L' },
    @{ x = 190; w = 120; align = 'L' },
    @{ x = 320; w = 70; align = 'R' },
    @{ x = 400; w = 90; align = 'R' }
) $true $null

# 4. sparse cells
$f4 = New-Object System.Drawing.Font "Segoe UI", 10
Render "sparse" 520 150 $f4 @(
    @("Name", "Role", "Location", "Note"),
    @("Alice", "Engineer", "Berlin", "remote"),
    @("Bob", "Designer", "", ""),
    @("Carol", "Product manager", "Lisbon", "part-time")
) @(
    @{ x = 12; w = 100; align = 'L' },
    @{ x = 120; w = 160; align = 'L' },
    @{ x = 290; w = 100; align = 'L' },
    @{ x = 400; w = 110; align = 'L' }
) $false $null

# 5. multi-word cells
$f5 = New-Object System.Drawing.Font "Georgia", 11
Render "words" 620 150 $f5 @(
    @("Country", "Capital city", "Population (millions)"),
    @("United States", "Washington, D.C.", "331.9"),
    @("United Kingdom", "London", "67.3"),
    @("New Zealand", "Wellington", "5.1")
) @(
    @{ x = 12; w = 170; align = 'L' },
    @{ x = 200; w = 200; align = 'L' },
    @{ x = 420; w = 190; align = 'R' }
) $false $null

# --- variants of the financial table to isolate the missed-row problem ---
$finRows = @(
    @("Metric", "Q1", "Q2", "Q3"),
    @("Revenue", "1,200,000", "1,350,500", "1,410,250"),
    @("Cost of goods", "800,000", "905,300", "950,000"),
    @("Net income", "400,000", "445,200", "460,250")
)
$finCols = @(
    @{ x = 12; w = 160; align = 'L' },
    @{ x = 180; w = 110; align = 'R' },
    @{ x = 300; w = 110; align = 'R' },
    @{ x = 420; w = 110; align = 'R' }
)
$script:spacing = 2.0
Render "fin_loose" 560 240 $f2 $finRows $finCols $false "Quarterly results (USD)"
$script:spacing = 1.25
Render "fin_tight" 560 200 $f2 $finRows $finCols $false "Quarterly results (USD)"
$script:spacing = 1.55
$script:noBold = $true
Render "fin_nobold" 560 220 $f2 $finRows $finCols $false "Quarterly results (USD)"
$script:noBold = $false
$script:spacing = $null
