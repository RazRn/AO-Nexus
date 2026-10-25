param(
    [string]$BackendPath = (Join-Path $PSScriptRoot '..\runtime\AOQuickLauncher\AOQuickLauncher.exe')
)
$ErrorActionPreference = 'Stop'
$backend = (Resolve-Path -LiteralPath $BackendPath).Path
if (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $backend) 'LoginHandler.dll') -PathType Leaf)) {
    throw 'Publish the backend with LoginHandler.dll before running this test.'
}

# Validation reads only these inert game placeholders. It never starts a game,
# accesses personal settings, injects a DLL, or connects to a login server.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AOLauncher-AccountLimits-' + [Guid]::NewGuid().ToString('N'))
$gameFolders = @((Join-Path $testRoot 'game one'), (Join-Path $testRoot 'game two'))
$secret = 'OFFLINE_TEST_PASSWORD_DO_NOT_LOG'
$checks = 0
try {
    foreach ($gameFolder in $gameFolders) {
        [IO.Directory]::CreateDirectory($gameFolder) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $gameFolder 'AnarchyOnline.exe'), [byte[]]@())
    }

    function Character([string]$Account, [int]$Id, [string]$Server = '5.161.243.214', [int]$Port = 7000, [int]$GameIndex = 0, [Nullable[int]]$MaxAccounts = $null, [Nullable[int]]$MaxCharactersPerAccount = $null, [string]$ServerName = '') {
        $entry = @{
            AccountId = 'fixture-' + $Id; Account = $Account; Password = $secret
            CharacterId = $Id; CharacterName = 'Fixture' + $Id
            GamePath = $gameFolders[$GameIndex]; Server = $Server; Port = $Port
        }
        if ($null -ne $MaxAccounts) { $entry.MaxAccounts = [int]$MaxAccounts }
        if ($null -ne $MaxCharactersPerAccount) { $entry.MaxCharactersPerAccount = [int]$MaxCharactersPerAccount }
        if ($ServerName) { $entry.ServerName = $ServerName }
        return $entry
    }

    function Check([string]$Name, [object[]]$Characters, [string]$ExpectedError = '', [string]$Language = 'en') {
        $request = @{
            Language = $Language; GamePath = $gameFolders[0]; Server = '5.161.243.214'; Port = 7000
            Characters = @($Characters); ReadyTimeoutSeconds = 10
        }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $backend
        $start.Arguments = '--validate-stdin'
        $start.WorkingDirectory = $testRoot
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        # StandardInputEncoding exists only on .NET Core; Windows PowerShell writes UTF-8 through the base stream below.
        if ($start.PSObject.Properties['StandardInputEncoding']) { $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false) }
        $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            if (!$process.Start()) { throw "Could not start backend validation: $Name" }
            $outputTask = $process.StandardOutput.ReadToEndAsync()
            $errorTask = $process.StandardError.ReadToEndAsync()
            $stdin = [IO.StreamWriter]::new($process.StandardInput.BaseStream, [Text.UTF8Encoding]::new($false))
            $stdin.WriteLine(($request | ConvertTo-Json -Depth 10 -Compress))
            $stdin.Close()
            if (!$process.WaitForExit(15000)) {
                $process.Kill()
                $process.WaitForExit()
                throw "Backend validation timed out: $Name"
            }
            $output = $outputTask.GetAwaiter().GetResult()
            $errorOutput = $errorTask.GetAwaiter().GetResult()
            if (($output + $errorOutput).Contains($secret)) { throw "Credential appeared in validation output: $Name" }
            if ($ExpectedError) {
                # No target summary is printed until all entries have passed validation.
                if ($process.ExitCode -ne 1 -or !$errorOutput.Contains($ExpectedError) -or $output.Trim()) {
                    throw "Expected early rejection failed: $Name (exit $($process.ExitCode)). $errorOutput $output"
                }
            } elseif ($process.ExitCode -ne 0 -or !$output.Contains('Validation OK. No game clients were started.') -or $errorOutput.Trim()) {
                throw "Expected successful offline validation failed: $Name (exit $($process.ExitCode)). $errorOutput $output"
            }
            $script:checks++
            "PASS: $Name"
        } finally { $process.Dispose() }
    }

    $prkError = 'Account on server 5.161.243.214:7000 allows at most 1 simultaneous character(s).'
    $liveError = 'Account on server 37.18.192.41:7505, 37.18.192.41:7506 allows at most 1 simultaneous character(s).'
    Check 'one PRK char' @((Character 'alpha' 1))
    Check 'different PRK accounts' @((Character 'alpha' 1), (Character 'beta' 2))
    Check 'same PRK account rejected' @((Character 'alpha' 1), (Character 'alpha' 2)) $prkError
    Check 'PRK usernames trimmed and compared case-insensitively' @((Character '  Alpha ' 1), (Character 'aLPHA' 2)) $prkError
    Check 'different folders and saved account IDs do not bypass PRK limit' @((Character 'alpha' 1), (Character 'alpha' 2 '5.161.243.214' 7000 1)) $prkError
    Check 'PRK rejection translated to Danish' @((Character 'alpha' 1), (Character 'alpha' 2)) 'En konto på server 5.161.243.214:7000 tillader højst 1 samtidige chars.' 'da'

    $legacy = Character 'alpha' 1
    foreach ($key in @('GamePath', 'Server', 'Port')) { $legacy.Remove($key) }
    Check 'legacy and explicit PRK targets share the account limit' @($legacy, (Character 'ALPHA' 2)) $prkError
    $secondLegacy = Character 'alpha' 2
    foreach ($key in @('GamePath', 'Server', 'Port')) { $secondLegacy.Remove($key) }
    Check 'legacy PRK request rejects two chars on one account' @($legacy, $secondLegacy) $prkError

    Check 'Rebirth retains multiple chars per account' @((Character 'alpha' 1 '2.24.96.30' 7500), (Character 'ALPHA' 2 '2.24.96.30' 7500))
    Check 'PRK and Live account scopes remain separate' @((Character 'alpha' 1), (Character 'ALPHA' 2 '37.18.192.41' 7505))
    Check 'PRK and Rebirth account scopes remain separate' @((Character 'alpha' 1), (Character 'ALPHA' 2 '2.24.96.30' 7500), (Character 'alpha' 3 '2.24.96.30' 7500))
    Check 'Live cross-dimension account limit retained' @((Character 'alpha' 1 '37.18.192.41' 7505), (Character '  ALPHA ' 2 '37.18.192.41' 7506)) $liveError
    Check 'different Live accounts still allowed' @((Character 'alpha' 1 '37.18.192.41' 7505), (Character 'beta' 2 '37.18.192.41' 7506))
    Check 'other port is a distinct custom endpoint' @((Character 'alpha' 1 '5.161.243.214' 7001), (Character 'alpha' 2 '5.161.243.214' 7001))
    Check 'other host is a distinct custom endpoint' @((Character 'alpha' 1 '192.0.2.10' 7000), (Character 'alpha' 2 '192.0.2.10' 7000))
    Check 'same character ID remains allowed across servers' @((Character 'alpha' 1), (Character 'alpha' 1 '2.24.96.30' 7500))
    Check 'duplicate character ID on one server remains rejected' @((Character 'alpha' 1), (Character 'beta' 1)) 'Each character must have a unique, positive character ID.'

    Check 'PRK rejects a third distinct account by default' @((Character 'alpha' 1), (Character 'beta' 2), (Character 'gamma' 3)) 'allows at most 2 simultaneous account(s)'
    Check 'Live allows multiple accounts on one endpoint by default' @((Character 'alpha' 1 '37.18.192.41' 7505), (Character 'beta' 2 '37.18.192.41' 7505))
    Check 'Rebirth has no default account limit' @((Character 'alpha' 1 '2.24.96.30' 7500), (Character 'beta' 2 '2.24.96.30' 7500), (Character 'gamma' 3 '2.24.96.30' 7500))
    Check 'PRK explicit unlimited allows three separate accounts' @((Character 'alpha' 1 -MaxAccounts 0), (Character 'beta' 2 -MaxAccounts 0), (Character 'gamma' 3 -MaxAccounts 0))
    Check 'PRK explicit unlimited preserves one char per account' @((Character 'alpha' 1 -MaxAccounts 0), (Character 'alpha' 2 -MaxAccounts 0)) $prkError
    Check 'custom server limit rejects extra accounts' @((Character 'alpha' 1 '192.0.2.1' 7000 -MaxAccounts 1), (Character 'beta' 2 '192.0.2.1' 7000 -MaxAccounts 1)) 'allows at most 1 simultaneous account(s)'
    Check 'custom limit counts usernames across saved IDs and game folders' @((Character ' Alpha ' 1 '192.0.2.1' 7000 0 1), (Character 'alpha' 2 '192.0.2.1' 7000 1 1))
    Check 'conflicting endpoint limits use strictest positive value' @((Character 'alpha' 1 -MaxAccounts 5), (Character 'beta' 2 -MaxAccounts 1)) 'allows at most 1 simultaneous account(s)'
    Check 'unlimited entry cannot bypass another entry endpoint limit' @((Character 'alpha' 1 -MaxAccounts 0), (Character 'beta' 2 -MaxAccounts 1)) 'allows at most 1 simultaneous account(s)'
    Check 'missing limit retains default alongside explicit unlimited' @((Character 'alpha' 1 -MaxAccounts 0), (Character 'beta' 2), (Character 'gamma' 3 -MaxAccounts 0)) 'allows at most 2 simultaneous account(s)'
    Check 'negative limit rejected before target summary' @((Character 'alpha' 1 -MaxAccounts -1)) 'The account limit must be between 0 and 100; 0 means unlimited.'
    Check 'limit above supported range rejected' @((Character 'alpha' 1 -MaxAccounts 101)) 'The account limit must be between 0 and 100; 0 means unlimited.'
    Check 'same username on different endpoints counts separately' @((Character 'alpha' 1 '192.0.2.1' 7000 -MaxAccounts 1), (Character 'alpha' 2 '192.0.2.1' 7001 -MaxAccounts 1))
    $aliasedA = Character 'alpha' 1 '192.0.2.1' 7000 -MaxAccounts 1
    $aliasedB = Character 'beta' 2 '192.0.2.1' 7000 -MaxAccounts 1
    $aliasedB.AccountId = $aliasedA.AccountId
    Check 'duplicate saved account IDs cannot collapse distinct usernames' @($aliasedA, $aliasedB) 'allows at most 1 simultaneous account(s)'
    Check 'explicit PRK char limit allows two chars on one account' @((Character 'alpha' 1 -MaxCharactersPerAccount 2), (Character 'alpha' 2 -MaxCharactersPerAccount 2))
    Check 'explicit PRK unlimited char limit overrides default one' @((Character 'alpha' 1 -MaxCharactersPerAccount 0), (Character 'alpha' 2 -MaxCharactersPerAccount 0))
    Check 'Live explicit unlimited char cap allows one account across dimensions' @((Character 'alpha' 1 '37.18.192.41' 7505 -MaxCharactersPerAccount 0), (Character 'ALPHA' 2 '37.18.192.41' 7506 -MaxCharactersPerAccount 0))
    Check 'Live mixed char limits retain strictest positive cap' @((Character 'alpha' 1 '37.18.192.41' 7505 -MaxCharactersPerAccount 0), (Character 'ALPHA' 2 '37.18.192.41' 7506 -MaxCharactersPerAccount 1)) $liveError
    Check 'custom char limit counts usernames across IDs and folders' @((Character 'alpha' 1 '192.0.2.1' 7000 0 -MaxCharactersPerAccount 1), (Character 'ALPHA' 2 '192.0.2.1' 7000 1 -MaxCharactersPerAccount 1)) 'allows at most 1 simultaneous character(s)'
    Check 'negative char limit is rejected' @((Character 'alpha' 1 -MaxCharactersPerAccount -1)) 'The account limit must be between 0 and 100; 0 means unlimited.'
    Check 'char limit above supported range is rejected' @((Character 'alpha' 1 -MaxCharactersPerAccount 101)) 'The account limit must be between 0 and 100; 0 means unlimited.'
    Check 'renamed server appears in account-limit rejection' @((Character 'alpha' 1 -ServerName 'Our realm'), (Character 'beta' 2 -ServerName 'Our realm'), (Character 'gamma' 3 -ServerName 'Our realm')) 'Server Our realm (5.161.243.214:7000) allows at most 2 simultaneous account(s).'
    Check 'renamed server appears in character-limit rejection' @((Character 'alpha' 1 -ServerName '  Our realm  '), (Character 'alpha' 2 -ServerName 'Our realm')) 'Account on server Our realm (5.161.243.214:7000) allows at most 1 simultaneous character(s).'
    Check 'renamed Live dimensions both appear in shared-limit rejection' @((Character 'alpha' 1 '37.18.192.41' 7505 -ServerName 'Original realm'), (Character 'alpha' 2 '37.18.192.41' 7506 -ServerName 'New realm')) 'Account on server Original realm (37.18.192.41:7505), New realm (37.18.192.41:7506) allows at most 1 simultaneous character(s).'
    Check 'unrelated Live dimension omitted from shared-limit rejection' @((Character 'alpha' 1 '37.18.192.41' 7505 -ServerName 'Original realm'), (Character 'alpha' 2 '37.18.192.41' 7505 -ServerName 'Original realm'), (Character 'beta' 3 '37.18.192.41' 7506 -ServerName 'Unrelated realm')) 'Account on server Original realm (37.18.192.41:7505) allows at most 1 simultaneous character(s).'
    Check 'renamed server preserved in Danish rejection' @((Character 'alpha' 1 -ServerName 'Vores server'), (Character 'alpha' 2 -ServerName 'Vores server')) 'En konto på server Vores server (5.161.243.214:7000) tillader højst 1 samtidige chars.' 'da'
    Check 'blank server names fall back to endpoint' @((Character 'alpha' 1 -ServerName '   '), (Character 'alpha' 2 -ServerName '   ')) $prkError
    Check 'control characters in server names fall back to endpoint' @((Character 'alpha' 1 -ServerName "Unsafe`r`nName"), (Character 'alpha' 2 -ServerName "Unsafe`r`nName")) $prkError
    $longName = 'Realm ' + ('x' * 300)
    Check 'long valid configured names remain visible' @((Character 'alpha' 1 -ServerName $longName), (Character 'alpha' 2 -ServerName $longName)) "Account on server $longName (5.161.243.214:7000) allows"

    $creation = Character 'alpha' 0
    $creation.CreateCharacter = $true
    Check 'explicit character creation with ID zero is accepted' @($creation)
    Check 'normal login still rejects ID zero' @((Character 'alpha' 0)) 'Each character must have a unique, positive character ID.'
    Check 'creation and character login cannot be combined' @($creation, (Character 'beta' 2)) 'Open character creation for one account at a time, separately from character login.'
    $anotherCreation = Character 'beta' 0
    $anotherCreation.CreateCharacter = $true
    Check 'two creation requests cannot be batched' @($creation, $anotherCreation) 'Open character creation for one account at a time, separately from character login.'
    $invalidCreation = Character 'alpha' 123
    $invalidCreation.CreateCharacter = $true
    Check 'creation mode rejects existing positive IDs' @($invalidCreation) 'A character-creation request must use character ID 0.'

    "PASS: $checks account-limit checks using --validate-stdin only; no game, personal settings or network access."
} finally {
    # Remove only exact files created by this run, then their empty directories.
    foreach ($gameFolder in $gameFolders) {
        $placeholder = Join-Path $gameFolder 'AnarchyOnline.exe'
        if (Test-Path -LiteralPath $placeholder -PathType Leaf) { Remove-Item -LiteralPath $placeholder -Force }
        if (Test-Path -LiteralPath $gameFolder -PathType Container) { [IO.Directory]::Delete($gameFolder) }
    }
    if (Test-Path -LiteralPath $testRoot -PathType Container) { [IO.Directory]::Delete($testRoot) }
}
