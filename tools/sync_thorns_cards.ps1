$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SourcePath = Join-Path $ProjectRoot "src\ThornsCards.cs"
$SheetPath = (Get-ChildItem -LiteralPath $ProjectRoot -Filter "*v4*.csv" |
	Sort-Object LastWriteTime -Descending |
	Select-Object -First 1 -ExpandProperty FullName)
if (-not $SheetPath) {
	throw "Could not find v4 card sheet CSV."
}

$code = Get-Content -LiteralPath $SourcePath -Raw -Encoding UTF8
$csvText = [System.IO.File]::ReadAllText($SheetPath, [System.Text.Encoding]::UTF8)
$rows = $csvText | ConvertFrom-Csv

function ConvertTo-CSharpString([string]$value) {
	$value = $value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "").Replace("`n", "\n")
	return '"' + $value + '"'
}

function ConvertTarget([string]$target) {
	if ($target -eq "RandomEnemy") {
		return "AllEnemies"
	}

	return $target
}

$changedLocalization = 0

foreach ($row in $rows) {
	$className = [regex]::Escape($row.ClassName)
	$type = $row.Type
	$rarity = $row.Rarity
	$target = ConvertTarget $row.Target
	$cost = [int]$row.Cost

	$ctorPattern = "public $className\(\) : base\([^\)]*\) \{ \}"
	$ctorReplacement = "public $($row.ClassName)() : base($cost, CardType.$type, CardRarity.$rarity, TargetType.$target) { }"
	$code = [regex]::Replace($code, $ctorPattern, $ctorReplacement, 1)

	$classPattern = "(?s)(public sealed class $className : CustomCardModel\s*\{)(?<body>.*?)(?=\r?\n\[Pool\(|\z)"
	$match = [regex]::Match($code, $classPattern)
	if (-not $match.Success) {
		Write-Warning "Missing card class $($row.ClassName)"
		continue
	}

	$body = $match.Groups["body"].Value
	$newBody = [regex]::Replace($body, '\("title",\s*"(?:\\.|[^"\\])*"\)', '("title", ' + (ConvertTo-CSharpString $row.Name) + ')', 1)
	$newBody = [regex]::Replace($newBody, '\("description",\s*"(?:\\.|[^"\\])*"\)', '("description", ' + (ConvertTo-CSharpString $row.Description) + ')', 1)

	if ([int]$row.Damage -gt 0) {
		$newBody = [regex]::Replace($newBody, 'new DamageVar\([^\)]*?ValueProp\.Move\)', "new DamageVar($($row.Damage)m, ValueProp.Move)", 1)
	}

	if ([int]$row.Block -gt 0) {
		$newBody = [regex]::Replace($newBody, 'new BlockVar\([^\)]*?ValueProp\.Move\)', "new BlockVar($($row.Block)m, ValueProp.Move)", 1)
	}

	if ([int]$row.Poison -gt 0) {
		$newBody = [regex]::Replace($newBody, 'new PowerVar<PoisonPower>\([^\)]*?\)', "new PowerVar<PoisonPower>($($row.Poison)m)", 1)
	}

	if ($newBody -ne $body) {
		$changedLocalization += 1
	}

	$newClass = $match.Groups[1].Value + $newBody
	$code = $code.Substring(0, $match.Index) + $newClass + $code.Substring($match.Index + $match.Length)
}

[System.IO.File]::WriteAllText($SourcePath, $code, [System.Text.UTF8Encoding]::new($false))
Write-Host "Synced $changedLocalization card blocks from $SheetPath"
