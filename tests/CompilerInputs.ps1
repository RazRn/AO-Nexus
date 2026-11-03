# Compatibility inputs for older direct-csc integration harnesses. The MSTest
# suite references the built Core library instead of compiling these sources.
function Get-NexusCompilerInputs {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repository 'src/Core') -Filter '*.cs' -File) {
        $source.FullName
    }
    foreach ($language in @('en', 'da')) {
        '/resource:' + (Join-Path $repository "src/Common/Resources/Strings.$language.xml") + ",AOLauncher.Strings.$language.xml"
    }
}
