# 基准趋势脚本自检 / Self-check for the benchmark trend script.
#
# 覆盖基线测试脚本 `test-compare-benchmark-results.ps1`（20485 B）的关键行为，
# 但保持单文件、可直接运行、失败即退出非零。
# Covers the key behaviours of the baseline's `test-compare-benchmark-results.ps1` (20485 B) in a single runnable file that exits non-zero on failure.
#
# 用法 / Usage: pwsh -File scripts/test-compare-benchmark-results.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "compare-benchmark-results.ps1"
$failures = 0
$checks = 0

function Assert-Equal {
    param($Expected, $Actual, [string] $Label)

    $script:checks++
    if ("$Expected" -ne "$Actual") {
        $script:failures++
        Write-Host ("FAIL {0}: expected [{1}] got [{2}]" -f $Label, $Expected, $Actual)
    }
}

function Assert-Throws {
    param([string] $ExpectedFragment, [scriptblock] $Action, [string] $Label)

    $script:checks++
    try {
        & $Action | Out-Null
    } catch {
        if ($_.Exception.Message -notlike "*$ExpectedFragment*") {
            $script:failures++
            Write-Host ("FAIL {0}: expected a message containing [{1}] got [{2}]" -f $Label, $ExpectedFragment, $_.Exception.Message)
        }
        return
    }
    $script:failures++
    Write-Host ("FAIL {0}: expected a throw, nothing was thrown" -f $Label)
}

function New-Entry {
    param(
        [string] $Benchmark,
        [double] $Score,
        [double] $Error = 1.0,
        [string] $Unit = "ns",
        [hashtable] $Params = $null,
        [int] $Samples = 3
    )

    # rawData 是"每个 fork 一组测量"的数组；这里每组只放一个测量，样本数就等于 fork 数。
    # rawData is an array of per-fork measurement batches; one measurement per batch makes the sample count equal the fork count.
    $rawData = @()
    for ($index = 0; $index -lt $Samples; $index++) {
        $rawData += , @($Score)
    }
    $entry = [ordered]@{
        benchmark     = $Benchmark
        params        = $Params
        primaryMetric = [ordered]@{
            score      = $Score
            scoreError = $Error
            scoreUnit  = $Unit
            rawData    = $rawData
        }
    }
    return $entry
}

function Write-Json {
    param([string] $Path, $Entries)

    $json = ConvertTo-Json -InputObject @($Entries) -Depth 8
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("ospf-compare-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    # 1) 显式文件模式：delta、样本数、单位 / explicit files: delta, sample counts, units.
    $baselinePath = Join-Path $work "baseline-small.json"
    $currentPath = Join-Path $work "current-small.json"
    Write-Json -Path $baselinePath -Entries @(
        (New-Entry -Benchmark "Alpha" -Score 100.0 -Samples 3),
        (New-Entry -Benchmark "Beta" -Score 0.0 -Samples 2),
        (New-Entry -Benchmark "Gamma" -Score 10.0 -Unit "ns" -Samples 1)
    )
    Write-Json -Path $currentPath -Entries @(
        (New-Entry -Benchmark "Alpha" -Score 110.0 -Samples 2),
        (New-Entry -Benchmark "Beta" -Score 5.0 -Samples 2),
        (New-Entry -Benchmark "Gamma" -Score 10.0 -Unit "us" -Samples 1),
        (New-Entry -Benchmark "Delta" -Score 7.0 -Samples 1)
    )

    $report = (& $scriptPath -Baseline $baselinePath -Current $currentPath) -join "`n"
    Assert-Equal $true ($report -match "\+10\.00%") "delta is computed as a percentage"
    Assert-Equal $true ($report -match "new benchmark in current") "a benchmark only present in current is marked new"
    Assert-Equal $true ($report -match "unit changed") "a unit change is noted"
    Assert-Equal $true ($report -match "ns -> us") "both units are shown on a change"
    Assert-Equal $true ($report -match "\| n/a \| ") "a zero baseline score reports n/a instead of dividing"
    Assert-Equal $true ($report -match "3/2") "sample counts are reported per side"
    Assert-Equal $true ($report -match "gate policy: report only|Gate policy: report only") "the report states the gate policy"
    Assert-Equal $true ($report -match "small") "the dataset tag is detected from the file names"

    # 1b) 反向缺口与零基线：只出现在 baseline 的必须报 missing，零基线的 delta 必须是 n/a。
    #     reverse gap and zero baseline: a benchmark only in baseline must be reported missing, and a zero baseline must yield n/a.
    $onlyLeft = Join-Path $work "only-left.json"
    $onlyRight = Join-Path $work "only-right.json"
    Write-Json -Path $onlyLeft -Entries @(
        (New-Entry -Benchmark "Vanishing" -Score 1.0 -Samples 1),
        (New-Entry -Benchmark "ZeroBase" -Score 0.0 -Samples 1)
    )
    Write-Json -Path $onlyRight -Entries @(
        (New-Entry -Benchmark "ZeroBase" -Score 5.0 -Samples 1)
    )
    $gapReport = (& $scriptPath -Baseline $onlyLeft -Current $onlyRight) -join "`n"
    Assert-Equal $true ($gapReport -match "missing in current") "a benchmark absent from current is marked missing"
    # **两处都必须钉住**：delta 列与备注列各写一次"missing"，只断言其中一处会把另一处的写错漏掉。
    # **Both places need pinning**: "missing" appears once in the delta column and once in the notes, so asserting only one hides a mistake in the other.
    $vanishingRow = @($gapReport -split "`n" | Where-Object { $_ -match "^\| Vanishing \|" })[0]
    Assert-Equal $true ($vanishingRow -match "\| missing \|") "the delta column says missing, not new"
    $zeroRow = @($gapReport -split "`n" | Where-Object { $_ -match "^\| ZeroBase \|" })[0]
    Assert-Equal $true ($zeroRow -match "\| n/a \|") "a zero baseline reports n/a in the delta column"
    Assert-Equal $false ($zeroRow -match "∞|Infinity") "a zero baseline does not emit an infinity delta"

    # 2) 默认输出路径：baseline-<d>.json / current-<d>.json → trend-<d>.md
    #    default output path: baseline-<d>.json / current-<d>.json → trend-<d>.md
    $global:LASTEXITCODE = 0
    $output = & $scriptPath -Baseline $baselinePath -Current $currentPath
    $trendPath = Join-Path $work "trend-small.md"
    Assert-Equal $true (Test-Path -LiteralPath $trendPath) "the trend report is written next to the current file"
    Assert-Equal $true ((Get-Content -LiteralPath $trendPath -Raw) -match "Benchmark Trend Comparison") "the trend report has a heading"

    # 3) 目录模式：自动配对，多个数据集时必须挑一个 / results-dir mode: auto pairing, -Dataset needed when ambiguous.
    $resultsDir = Join-Path $work "results"
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    Copy-Item -LiteralPath $baselinePath -Destination (Join-Path $resultsDir "baseline-small.json")
    Copy-Item -LiteralPath $currentPath -Destination (Join-Path $resultsDir "current-small.json")
    $dirReport = (& $scriptPath -ResultsDir $resultsDir) -join "`n"
    Assert-Equal $true ($dirReport -match "results-dir") "the input mode is results-dir"

    Copy-Item -LiteralPath $baselinePath -Destination (Join-Path $resultsDir "baseline-large.json")
    Copy-Item -LiteralPath $currentPath -Destination (Join-Path $resultsDir "current-large.json")
    Assert-Throws -ExpectedFragment "Multiple matched datasets" -Label "ambiguous dataset" -Action {
        & $scriptPath -ResultsDir $resultsDir
    }
    $pickedReport = (& $scriptPath -ResultsDir $resultsDir -Dataset "large") -join "`n"
    Assert-Equal $true ($pickedReport -match "``large``") "the explicitly chosen dataset is reported"

    # 4) 输入校验：缺一个、文件不存在、目录不存在 / input validation: half a pair, missing file, missing dir.
    Assert-Throws -ExpectedFragment "must be provided together" -Label "only one explicit file" -Action {
        & $scriptPath -Baseline $baselinePath
    }
    Assert-Throws -ExpectedFragment "Benchmark result file not found" -Label "missing file" -Action {
        & $scriptPath -Baseline (Join-Path $work "nope.json") -Current $currentPath
    }
    Assert-Throws -ExpectedFragment "Benchmark result directory not found" -Label "missing dir" -Action {
        & $scriptPath -ResultsDir (Join-Path $work "nope")
    }
    Assert-Throws -ExpectedFragment "Either provide" -Label "no input at all" -Action {
        & $scriptPath
    }

    # 5) JSON 校验：损坏、空负载、缺字段、重复键、非数值 / JSON validation: broken, empty, missing fields, duplicates, non-numeric.
    $badJson = Join-Path $work "bad.json"
    Set-Content -LiteralPath $badJson -Value "{ not json" -Encoding UTF8
    Assert-Throws -ExpectedFragment "Invalid benchmark JSON" -Label "broken json" -Action {
        & $scriptPath -Baseline $badJson -Current $currentPath
    }

    $emptyJson = Join-Path $work "empty.json"
    Set-Content -LiteralPath $emptyJson -Value "[]" -Encoding UTF8
    Assert-Throws -ExpectedFragment "empty payload" -Label "empty payload" -Action {
        & $scriptPath -Baseline $emptyJson -Current $currentPath
    }

    $noBenchmark = Join-Path $work "no-benchmark.json"
    Write-Json -Path $noBenchmark -Entries @([ordered]@{ primaryMetric = [ordered]@{ score = 1.0; scoreError = 0.1; scoreUnit = "ns" } })
    Assert-Throws -ExpectedFragment "missing 'benchmark'" -Label "missing benchmark name" -Action {
        & $scriptPath -Baseline $noBenchmark -Current $currentPath
    }

    $noUnit = Join-Path $work "no-unit.json"
    Write-Json -Path $noUnit -Entries @([ordered]@{ benchmark = "X"; primaryMetric = [ordered]@{ score = 1.0; scoreError = 0.1 } })
    Assert-Throws -ExpectedFragment "missing 'primaryMetric.scoreUnit'" -Label "missing unit" -Action {
        & $scriptPath -Baseline $noUnit -Current $currentPath
    }

    $nonNumeric = Join-Path $work "non-numeric.json"
    Write-Json -Path $nonNumeric -Entries @([ordered]@{ benchmark = "X"; primaryMetric = [ordered]@{ score = "abc"; scoreError = 0.1; scoreUnit = "ns" } })
    Assert-Throws -ExpectedFragment "expected numeric" -Label "non-numeric score" -Action {
        & $scriptPath -Baseline $nonNumeric -Current $currentPath
    }

    $duplicate = Join-Path $work "duplicate.json"
    Write-Json -Path $duplicate -Entries @(
        (New-Entry -Benchmark "Same" -Score 1.0 -Params @{ n = 1 }),
        (New-Entry -Benchmark "Same" -Score 2.0 -Params @{ n = 1 })
    )
    Assert-Throws -ExpectedFragment "duplicate benchmark key" -Label "duplicate key" -Action {
        & $scriptPath -Baseline $duplicate -Current $currentPath
    }

    # 6) 参数归一化：params 参与键，且按键名排序 / params take part in the key and are sorted by name.
    $paramBaseline = Join-Path $work "param-a.json"
    $paramCurrent = Join-Path $work "param-b.json"
    Write-Json -Path $paramBaseline -Entries @(
        (New-Entry -Benchmark "P" -Score 1.0 -Params ([ordered]@{ z = 2; a = 1 }))
    )
    Write-Json -Path $paramCurrent -Entries @(
        (New-Entry -Benchmark "P" -Score 1.0 -Params ([ordered]@{ a = 1; z = 2 }))
    )
    $paramReport = (& $scriptPath -Baseline $paramBaseline -Current $paramCurrent) -join "`n"
    Assert-Equal $true ($paramReport -match "P \[a=1, z=2\]") "params are normalised and sorted"
    Assert-Equal $true ($paramReport -match "0\.00%") "identical scores report a zero delta"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ("checks={0} failures={1}" -f $checks, $failures)
if ($failures -ne 0) {
    exit 1
}
Write-Host "PASS"
