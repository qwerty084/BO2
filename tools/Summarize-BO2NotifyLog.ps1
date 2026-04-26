param(
    [string]$Path,
    [long]$FromTick = -1,
    [long]$ToTick = -1,
    [long]$BaselineFromTick = -1,
    [long]$BaselineToTick = -1,
    [long]$CompareFromTick = -1,
    [long]$CompareToTick = -1,
    [int]$Top = 40
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $logDirectory = Join-Path $env:TEMP 'BO2Monitor'
    $latestLog = Get-ChildItem -LiteralPath $logDirectory -Filter 'notify-log-*.jsonl' -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $latestLog) {
        throw "No notify logs found in $logDirectory"
    }

    $Path = $latestLog.FullName
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Notify log not found: $Path"
}

function Get-NotifyName {
    param($Event)

    if (-not [string]::IsNullOrWhiteSpace($Event.name)) {
        return $Event.name
    }

    return "<unresolved:$($Event.stringValue)>"
}

function Select-NotifyWindow {
    param(
        [object[]]$Events,
        [long]$From,
        [long]$To
    )

    $selected = $Events
    if ($From -ge 0) {
        $selected = $selected | Where-Object { [uint32]$_.tick -ge [uint32]$From }
    }

    if ($To -ge 0) {
        $selected = $selected | Where-Object { [uint32]$_.tick -le [uint32]$To }
    }

    return @($selected)
}

function Show-NameSummary {
    param(
        [object[]]$Events,
        [int]$Limit
    )

    $Events |
        Group-Object { Get-NotifyName $_ } |
        Sort-Object Count -Descending |
        Select-Object -First $Limit @{ Name = 'Name'; Expression = { $_.Name } }, Count
}

function Show-OwnerSummary {
    param(
        [object[]]$Events,
        [int]$Limit
    )

    $Events |
        Group-Object { "owner=$($_.ownerId) name=$(Get-NotifyName $_)" } |
        Sort-Object Count -Descending |
        Select-Object -First $Limit @{ Name = 'OwnerName'; Expression = { $_.Name } }, Count
}

function Show-TimeSummary {
    param([object[]]$Events)

    if ($Events.Count -eq 0) {
        return @()
    }

    $firstTick = ($Events | Sort-Object tick | Select-Object -First 1).tick
    $Events |
        Group-Object { [int][Math]::Floor(([uint32]$_.tick - [uint32]$firstTick) / 1000) } |
        Sort-Object { [int]$_.Name } |
        Select-Object @{ Name = 'Second'; Expression = { [int]$_.Name } }, Count
}

function Show-WindowDiff {
    param(
        [object[]]$BaselineEvents,
        [object[]]$CompareEvents,
        [int]$Limit
    )

    $counts = @{}
    foreach ($event in $BaselineEvents) {
        $name = Get-NotifyName $event
        if (-not $counts.ContainsKey($name)) {
            $counts[$name] = [pscustomobject]@{ Name = $name; Baseline = 0; Compare = 0; Delta = 0 }
        }

        $counts[$name].Baseline++
    }

    foreach ($event in $CompareEvents) {
        $name = Get-NotifyName $event
        if (-not $counts.ContainsKey($name)) {
            $counts[$name] = [pscustomobject]@{ Name = $name; Baseline = 0; Compare = 0; Delta = 0 }
        }

        $counts[$name].Compare++
    }

    foreach ($entry in $counts.Values) {
        $entry.Delta = $entry.Compare - $entry.Baseline
    }

    $counts.Values |
        Sort-Object Delta, Compare -Descending |
        Select-Object -First $Limit
}

$events = @(
    Get-Content -LiteralPath $Path |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json }
)

$window = Select-NotifyWindow -Events $events -From $FromTick -To $ToTick

"Log: $Path"
"Total records: $($events.Count)"
"Selected records: $($window.Count)"
""
"By name:"
Show-NameSummary -Events $window -Limit $Top | Format-Table -AutoSize
""
"By owner/name:"
Show-OwnerSummary -Events $window -Limit $Top | Format-Table -AutoSize
""
"By second in selected window:"
Show-TimeSummary -Events $window | Format-Table -AutoSize

if ($BaselineFromTick -ge 0 -and $BaselineToTick -ge 0 -and $CompareFromTick -ge 0 -and $CompareToTick -ge 0) {
    $baseline = Select-NotifyWindow -Events $events -From $BaselineFromTick -To $BaselineToTick
    $compare = Select-NotifyWindow -Events $events -From $CompareFromTick -To $CompareToTick

    ""
    "Compare window minus baseline:"
    "Baseline records: $($baseline.Count)"
    "Compare records: $($compare.Count)"
    Show-WindowDiff -BaselineEvents $baseline -CompareEvents $compare -Limit $Top | Format-Table -AutoSize
}
