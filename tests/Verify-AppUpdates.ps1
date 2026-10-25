param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\dist\AONexus.exe'), [switch]$CheckGitHub)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.IO.Compression, System.IO.Compression.FileSystem, System.Web.Extensions
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$updates = $assembly.GetType('AOLauncher.AppUpdates'); $installer = $assembly.GetType('AOLauncher.UpdateInstaller')
$checks = 0
function Assert($value, $message) { if (!$value) { throw $message }; $script:checks++ }
function Call($type, $method, [object[]]$arguments) { for ($i=0; $i -lt $arguments.Count; $i++) { if ($null -ne $arguments[$i]) { $arguments[$i] = $arguments[$i].psobject.BaseObject } }; $type.GetMethod($method,$flags).Invoke($null,$arguments) }
function Must-Fail([scriptblock]$action, $message) { $failed=$false; try { & $action | Out-Null } catch { $failed=$true }; Assert $failed $message }
$serializer = New-Object Web.Script.Serialization.JavaScriptSerializer
$notes=[Activator]::CreateInstance($assembly.GetType('AOLauncher.ReleaseNotesBox'),$true)
try {
    $markdown="# Release heading`n`n- **First change**`n- Second change with ``/quit```n`nA separate paragraph."
    $notes.GetType().GetMethod('SetMarkdown',$flags).Invoke($notes,[object[]]@($markdown)) | Out-Null
    Assert ($notes.Text.Contains("`n`n") -and $notes.Text.Contains('• First change') -and !$notes.Text.Contains('**')) 'Update notes preserve paragraphs and display clean bullets'
    $notes.Select(0,'Release heading'.Length)
    Assert ($notes.SelectionFont.Bold) 'Release headings are visibly emphasized'
    Assert ($notes.ReadOnly -and !$notes.DetectUrls) 'Remote notes remain read-only text without automatic URL activation'
    $notes.GetType().GetMethod('SetMarkdown',$flags).Invoke($notes,[object[]]@('{\rtf1 <script>text</script>}')) | Out-Null
    Assert ($notes.Text.Contains('<script>text</script>') -and $notes.Text.Contains('rtf1')) 'Remote HTML and RTF syntax are never interpreted'
} finally { $notes.Dispose() }
$settings = [Activator]::CreateInstance($assembly.GetType('AOLauncher.LauncherSettings'), $true)
Assert (!$settings.HasChosenUpdatePreference -and !$settings.CheckForUpdatesOnStartup) 'New and migrated profiles require an explicit initial update choice'
$settings.HasChosenUpdatePreference = $true; $settings.CheckForUpdatesOnStartup = $true
$restored = $serializer.Deserialize($serializer.Serialize($settings),$settings.GetType())
Assert ($restored.HasChosenUpdatePreference -and $restored.CheckForUpdatesOnStartup) 'The initial answer and startup toggle persist together'
$settings.CheckForUpdatesOnStartup = $false
$restored = $serializer.Deserialize($serializer.Serialize($settings),$settings.GetType())
Assert ($restored.HasChosenUpdatePreference -and !$restored.CheckForUpdatesOnStartup) 'Turning updates off preserves the answered state'
$metadata = @{ tag_name='v1.0.19'; draft=$false; prerelease=$false; body='Fixture notes'; assets=@(@{name='AO-Nexus-Luckyspawn.zip'; size=1024; digest=('sha256:' + ('a'*64)); browser_download_url='https://github.com/RazRn/AO-Nexus/releases/download/v1.0.19/AO-Nexus-Luckyspawn.zip'}) }
function Parse($data) { Call $updates 'ParseRelease' @($serializer.Serialize($data)) }
$release = Parse $metadata
Assert ($release.GetType().GetField('Version',$flags).GetValue($release) -eq [version]'1.0.19.0') 'Three-part release versions normalize correctly'
foreach ($key in @('draft','prerelease')) { $metadata[$key]=$true; Must-Fail { Parse $metadata } 'Non-stable releases are rejected'; $metadata[$key]=$false }
$asset = $metadata.assets[0]
$asset.digest = $null; Must-Fail { Parse $metadata } 'Missing GitHub digest rejected'; $asset.digest='sha256:' + ('a'*64)
$asset.browser_download_url='https://example.com/app.zip'; Must-Fail { Parse $metadata } 'An external repository URL is rejected'
$asset.browser_download_url='https://github.com/RazRn/AO-Nexus/releases/download/v1.0.19/AO-Nexus-Luckyspawn.zip'
$asset.size=200MB; Must-Fail { Parse $metadata } 'Oversized package rejected'; $asset.size=1024
$metadata.tag_name='v1.0.19-preview'; Must-Fail { Parse $metadata } 'Non-stable version tags rejected'; $metadata.tag_name='v1.0.19'
$metadata.assets=@($asset,$asset); Must-Fail { Parse $metadata } 'Duplicate assets rejected'; $metadata.assets=@($asset)
Assert (Call $updates 'AllowedDownload' @([uri]'https://release-assets.githubusercontent.com/example')) 'Official asset redirect accepted'
foreach ($url in @('http://github.com/file','https://github.com.evil.test/file','https://user@github.com/file','https://github.com:444/file')) {
    Assert (!(Call $updates 'AllowedDownload' @([uri]$url))) 'Unsafe redirect rejected'
}
$root = Join-Path $PSScriptRoot ('../logs/update-fixture-' + [guid]::NewGuid().ToString('N'))
$root = [IO.Path]::GetFullPath($root)
[IO.Directory]::CreateDirectory((Join-Path $root 'build')) | Out-Null
$fixture = Join-Path $root 'build/AONexus.exe'
Add-Type -OutputAssembly $fixture -OutputType ConsoleApplication -TypeDefinition @'
using System.Reflection;
[assembly: AssemblyVersion("1.0.19.0")]
public class Fixture { public static void Main() {} }
'@
function Make-Zip([string]$name, [string]$hash, [bool]$duplicate=$false) {
    $path = Join-Path $root $name
    $zip = [IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$fixture,'AO-Nexus/AONexus.exe') | Out-Null
        if ($duplicate) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$fixture,'ao-nexus/aonexus.exe') | Out-Null }
        $entry=$zip.CreateEntry('AO-Nexus/SHA256SUMS.txt'); $writer=[IO.StreamWriter]::new($entry.Open())
        try { $writer.WriteLine($hash + '  AONexus.exe') } finally { $writer.Dispose() }
        $entry=$zip.CreateEntry('../must-not-extract.txt'); $writer=[IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('untrusted entry') } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    return $path
}
function Release-For($zip) { $asset.size=(Get-Item -LiteralPath $zip).Length; $asset.digest='sha256:' + (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant(); Parse $metadata }
function Extract($zip,$release,$name) { $folder=Join-Path $root $name; [IO.Directory]::CreateDirectory($folder)|Out-Null; Call $updates 'VerifyAndExtract' @($zip,$folder,$release) }
$fixtureHash = (Get-FileHash -LiteralPath $fixture).Hash.ToLowerInvariant()
$zip = Make-Zip 'good.zip' $fixtureHash
$release = Release-For $zip
$staged = Extract $zip $release 'good'
Assert ((Get-FileHash -LiteralPath $staged).Hash.ToLowerInvariant() -eq $fixtureHash) 'Verified executable extracted correctly'
Assert (!(Test-Path -LiteralPath (Join-Path $root 'must-not-extract.txt'))) 'Unselected ZIP paths are never extracted'
$release.GetType().GetField('Sha256',$flags).SetValue($release,('b'*64))
Must-Fail { Extract $zip $release 'wrong-archive' } 'Archive checksum mismatch rejected'
$zipBad = Make-Zip 'bad-executable.zip' ('c'*64)
Must-Fail { Extract $zipBad (Release-For $zipBad) 'bad-executable' } 'Executable checksum mismatch rejected'
$zipDuplicate = Make-Zip 'duplicate.zip' $fixtureHash $true
Must-Fail { Extract $zipDuplicate (Release-For $zipDuplicate) 'duplicate' } 'Duplicate executable entry rejected'
$release = Release-For $zip; $release.GetType().GetField('Version',$flags).SetValue($release,[version]'1.0.20.0')
Must-Fail { Extract $zip $release 'wrong-version' } 'Executable version mismatch rejected'
$target=Join-Path $root 'installed.exe'; [IO.File]::WriteAllText($target,'old fixture')
$oldHash=(Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant()
Must-Fail { Call $installer 'ReplaceVerified' @($staged,$target,('d'*64),$fixtureHash) } 'Changed installation aborts before replacement'
$lock=[IO.File]::Open($target,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { Must-Fail { Call $installer 'ReplaceVerified' @($staged,$target,$oldHash,$fixtureHash) } 'Locked installation cannot be replaced' } finally { $lock.Dispose() }
Assert ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -eq $oldHash) 'Failed update keeps installed file unchanged'
$backup=Call $installer 'ReplaceVerified' @($staged,$target,$oldHash,$fixtureHash)
Assert ((Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant() -eq $fixtureHash -and (Get-FileHash -LiteralPath $backup).Hash.ToLowerInvariant() -eq $oldHash) 'Successful atomic replacement retains the old executable as backup'
$journal = $assembly.GetType('AOLauncher.UpdateJournal')
$marker = Join-Path $root 'installed-version.txt'
Assert (!(Call $journal 'ConsumeInstalledNotice' @($root))) 'Ordinary startup without an installation marker shows no release notice'
[IO.File]::WriteAllText($marker,'0.1.0.0')
Assert (!(Call $journal 'ConsumeInstalledNotice' @($root))) 'Another version cannot consume this installation notice'
[IO.File]::WriteAllText($marker,$assembly.GetName().Version.ToString())
Assert (Call $journal 'ConsumeInstalledNotice' @($root)) 'The installed version consumes its successful-update marker'
Assert (!(Call $journal 'ConsumeInstalledNotice' @($root))) 'Release summary is shown once, not every startup'
[IO.File]::WriteAllText($marker,('x'*129))
Assert (!(Call $journal 'ConsumeInstalledNotice' @($root))) 'Oversized marker is ignored'
if ($CheckGitHub) {
    $task=Call $updates 'CheckAsync' @([Threading.CancellationToken]::None)
    $live=$task.GetAwaiter().GetResult()
    Assert ($null -ne $live) 'Public GitHub check works without login or credentials'
}
Write-Output "PASS: $checks release metadata, host, digest, ZIP, version, locked-file and backup checks. Only isolated fixtures were replaced."
