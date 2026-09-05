$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scene = [IO.File]::ReadAllText((Join-Path $root 'Assets/Scenes/SampleScene.unity'))
$blocks = @{}
foreach ($match in [regex]::Matches($scene, '(?ms)^--- !u!\d+ &(\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)')) {
    $id = $match.Groups[1].Value
    if ($blocks.ContainsKey($id)) { throw "Duplicate scene fileID: $id" }
    $blocks[$id] = $match.Value
}
foreach ($match in [regex]::Matches($scene, '\{fileID: (\d+)\}')) {
    $id = $match.Groups[1].Value
    if ($id -ne '0' -and !$blocks.ContainsKey($id)) { throw "Missing local scene reference: $id" }
}
$healthGuid = 'ada7027cce7199342a9717419d7f28c9'
$healthOwners = @{}
foreach ($block in $blocks.Values) {
    if ($block -notmatch $healthGuid) { continue }
    $owner = [regex]::Match($block, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    if ($healthOwners.ContainsKey($owner)) { throw "Duplicate health on $owner" }
    $healthOwners[$owner] = $block
}
foreach ($block in $blocks.Values) {
    if ($block -notmatch '54199b29e3369da4a982b87ff0b21ac5|80c11472a59dfb941bb0a8f380163bb7|c67071ed8c436f74e8b700b6e4d2335a') { continue }
    $owner = [regex]::Match($block, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    if (!$healthOwners.ContainsKey($owner)) { throw "Combat component without CharacterHealth on $owner" }
}
if ($healthOwners['1743753184'] -notmatch 'team: 0') { throw 'Player must be Allies' }
$overrides = [regex]::Matches($scene, 'guid: e52a9ba211731aa4ba4ba466514bfd45, type: 3}\r?\n      propertyPath: (enemyTeam|team)\r?\n      value: (\d+)')
foreach ($override in $overrides) {
    $expected = if ($override.Groups[1].Value -eq 'team') { '1' } else { '0' }
    if ($override.Groups[2].Value -ne $expected) { throw "Wrong enemy team override: $override" }
}
$removed = 'cec9f30aecbed6146881f72001825b01|6e47803acc0ae354ea311c47b84b8280|bfba79e2c513de84f8b59ad850b15656'
foreach ($asset in Get-ChildItem (Join-Path $root 'Assets') -Recurse -File | Where-Object { $_.Extension -in '.unity','.prefab','.asset' }) {
    if ([IO.File]::ReadAllText($asset.FullName) -match $removed) { throw "Reference to removed component: $($asset.FullName)" }
}
$buildSettings = [IO.File]::ReadAllText((Join-Path $root 'ProjectSettings/EditorBuildSettings.asset'))
if ($buildSettings -notmatch 'enabled: 1\r?\n    path: Assets/Scenes/SampleScene.unity\r?\n    guid: 99c9720ab356a0642a771bea13969a05') { throw 'Build scene is not configured' }
Write-Output "Project structure passed: $($blocks.Count) scene objects/components; local references, combat health, teams, removed script references and build settings checked."
