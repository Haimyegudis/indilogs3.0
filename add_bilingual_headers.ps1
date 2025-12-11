# Script: add_bilingual_headers.ps1
# EN: Prepends a bilingual (English + Hebrew) file header to all .cs and .xaml files
# HE: מוסיף כותרת דו-לשונית (אנגלית + עברית) לכל קבצי .cs ו-.xaml

$excludeDirs = @('.git', 'packages', '.vs', 'bin', 'obj')
Get-ChildItem -Path . -Include *.cs,*.xaml -Recurse | ForEach-Object {
    $path = $_.FullName
    # Skip excluded directories
    if ($excludeDirs | Where-Object { $path -like "*$_*" }) { return }
    try {
        $content = Get-Content -Path $path -Raw -ErrorAction Stop
    } catch {
        return
    }
    # If header already exists, skip
    if ($content -match 'BILINGUAL-HEADER-START') { return }

    if ($_.Extension -ieq '.cs') {
        $header = "// BILINGUAL-HEADER-START`r`n// EN: File: $($_.Name) - Auto-added bilingual header.`r`n// HE: קובץ: $($_.Name) - כותרת דו-לשונית שנוספה אוטומטית.`r`n`r`n"
        Set-Content -Path $path -Value ($header + $content)
    } else {
        $header = "<!-- BILINGUAL-HEADER-START`r`n     EN: File: $($_.Name) - Auto-added bilingual header.`r`n     HE: קובץ: $($_.Name) - כותרת דו-לשונית שנוספה אוטומטית.`r`n-->`r`n"
        Set-Content -Path $path -Value ($header + $content)
    }
}

# Usage:
# 1. Open an elevated PowerShell or Developer PowerShell in the repository root.
# 2. Run: powershell -ExecutionPolicy Bypass -File .\add_bilingual_headers.ps1
# 3. After running, verify changes and commit to your branch.

# NOTES:
# - This script only adds a file-level bilingual header as a safe first step.
# - Adding full inline comments will be done per-file afterwards to avoid large risky edits.
# - בהפעלה, הסקריפט מוסיף תחילית בכותרת בכל הקבצים; הערות פנימיות מלאות יתווספו מאוחר יותר בקבצים ספציפיים.
