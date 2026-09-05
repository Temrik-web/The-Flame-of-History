param([string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $root 'Temp/CodeChecks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$encoding = [Text.UTF8Encoding]::new($false)

# Используем реальные ссылки из сгенерированного Unity проекта и его Roslyn.
# Это проверка C#, а не замена BuildPipeline (сцены/шейдеры/платформа).
foreach ($editorMode in @($false, $true)) {
    [xml]$project = Get-Content (Join-Path $root 'Assembly-CSharp.csproj') -Raw
    $defines = @($project.Project.PropertyGroup.DefineConstants | Where-Object { $_ })[0].Split(';')
    if (!$editorMode) {
        $defines = $defines | Where-Object { $_ -notmatch '^UNITY_EDITOR' -and $_ -ne 'UNITY_INCLUDE_TESTS' }
    }
    $suffix = if ($editorMode) { 'Editor' } else { 'Player' }
    $argsList = [Collections.Generic.List[string]]::new()
    $argsList.AddRange([string[]]@('-nologo', '-target:library', '-langversion:9', '-nostdlib+', '-nowarn:0649,0169', ('-define:' + ($defines -join ';')), ('-out:"' + (Join-Path $output "$suffix.dll") + '"')))
    $references = @($project.Project.ItemGroup.Reference.HintPath | Where-Object { $_ })
    if ($editorMode) {
        [xml]$editorProject = Get-Content (Join-Path $root 'Assembly-CSharp-Editor.csproj') -Raw
        $references += @($editorProject.Project.ItemGroup.Reference.HintPath | Where-Object { $_ })
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reference in $references) {
        if ($reference -match 'Assembly-CSharp') { continue }
        if (!$editorMode -and $reference -match '(?i)(UnityEditor|\.Editor\.|\.Editor\.dll|TestRunner|nunit)') { continue }
        if ((Test-Path -LiteralPath $reference) -and $seen.Add([IO.Path]::GetFileName($reference))) { $argsList.Add('-r:"' + $reference + '"') }
    }
    # Берём актуальные исходники, включая новые тесты; csproj может быть старым.
    foreach ($file in Get-ChildItem (Join-Path $root 'Assets') -Recurse -Filter '*.cs') {
        if (!$editorMode -and $file.FullName -match '[/\\]Editor[/\\]') { continue }
        $argsList.Add('"' + $file.FullName + '"')
    }
    $response = Join-Path $output "$suffix.rsp"
    [IO.File]::WriteAllLines($response, $argsList, $encoding)
    & (Join-Path $UnityEditor 'Data/NetCoreRuntime/dotnet.exe') (Join-Path $UnityEditor 'Data/DotNetSdkRoslyn/csc.dll') "@$response"
    if ($LASTEXITCODE -ne 0) { throw "$suffix compilation failed: $LASTEXITCODE" }
    Write-Output "$suffix compilation passed."
}
