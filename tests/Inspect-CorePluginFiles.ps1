param([string]$PackDirectory = (Join-Path $PSScriptRoot '../runtime/Local13Pack'))
$ErrorActionPreference='Stop'
if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSEdition -eq 'Core') { throw 'Use 32-bit Windows PowerShell. Reads metadata only; no plugin code runs.' }
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pack=[IO.Path]::GetFullPath($PackDirectory)
$catalog=(Get-Content -LiteralPath (Join-Path $pack 'plugins.json') -Raw|ConvertFrom-Json).Plugins
$policy=Get-Content -LiteralPath (Join-Path $root 'src/Launcher/Assets/PluginTestPolicy.json') -Raw|ConvertFrom-Json
$manifest=Get-Content -LiteralPath (Join-Path $root 'src/Launcher/obj/EmbeddedAOSharp/AOSharpPack.manifest.json') -Raw|ConvertFrom-Json
$rows=@()
foreach($entry in @($policy | Where-Object {$_.Class -in @('Candidate','Instrument')})) {
    $plugin=@($catalog | Where-Object {('pack:'+$_.Id) -eq $entry.Id})[0]
    if(!$plugin){throw "Missing catalog entry: $($entry.Id)"}
    $path=Join-Path $pack $plugin.Path
    if((Get-FileHash -LiteralPath $path).Hash -ne $entry.Sha256){throw "Primary hash mismatch: $($entry.Id)"}
    # Reflection-only context reads CLR metadata and cannot execute plugin entry points.
    $assembly=[Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
    $references=@()
    foreach($reference in $assembly.GetReferencedAssemblies()) {
        $framework=$reference.Name -match '^(mscorlib|System($|\.)|Microsoft\.CSharp$|WindowsBase$|Presentation(Core|Framework)$)'
        $resolved=$null
        if(!$framework) {
            foreach($folder in @((Split-Path $path),(Join-Path $pack 'AOSharp'))) {
                $candidate=Join-Path $folder ($reference.Name+'.dll')
                if(Test-Path -LiteralPath $candidate){$resolved=$candidate;break}
            }
            if(!$resolved){throw "Dependency not found: $($plugin.Id) -> $($reference.Name)"}
            $relative=$resolved.Substring($pack.TrimEnd('\').Length+1).Replace('\','/')
            $expected=@($manifest.Files|Where-Object {$_.Path -eq $relative})[0]
            if(!$expected -or (Get-FileHash -LiteralPath $resolved).Hash -ne $expected.Sha256){throw "Dependency manifest mismatch: $relative"}
        }
        $references += [ordered]@{Name=$reference.Name;RequestedVersion=$reference.Version.ToString();FrameworkReference=$framework;ResolvedFile=$resolved;ResolvedVersion=if($resolved){[Reflection.AssemblyName]::GetAssemblyName($resolved).Version.ToString()}else{$null}}
    }
    $rows += [ordered]@{PluginId=$entry.Id;Sha256=$entry.Sha256;Assembly=$assembly.GetName().Name;AssemblyVersion=$assembly.GetName().Version.ToString();FileCheck='Passed';StartupCheck='Not run';GameplayCheck='Not run';References=$references}
}
if($rows.Count -ne 16){throw 'Expected sixteen core candidates including instrumentation.'}
$output=Join-Path $root 'docs/development/log/plugin-inventory/core-file-checks.json'
$result=[ordered]@{Date='2026-10-30';Method='Read-only hashes and CLR reflection-only metadata; no game or plugin entry point executed.';Limit='Direct packaged references are located and hash-checked. Framework references are recorded, not runtime-tested. This does not validate binding redirects, indirect/dynamic dependencies, UI assets or functional compatibility.';Plugins=$rows}
[IO.File]::WriteAllText($output,($result|ConvertTo-Json -Depth 7)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Host "PASS: $($rows.Count) core plugin files and their direct packaged references; no plugin code executed. $output"
