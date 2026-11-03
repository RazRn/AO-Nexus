param(
    [Parameter(Mandatory=$true)][string]$CachePath,
    [int[]]$CharacterId = @()
)
$ErrorActionPreference = 'Stop'
# Read-only inspection of AO's own character-selection appearance cache.
# This does not load game DLLs, attach to processes, or change preferences.
$item = Get-Item -LiteralPath $CachePath
if ($item.Length -gt 4MB) { throw 'Character viewer cache exceeds the inspection size limit.' }
$options = New-Object System.Xml.XmlReaderSettings
$options.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$options.XmlResolver = $null
$options.MaxCharactersInDocument = 4MB
$reader = [System.Xml.XmlReader]::Create($item.FullName, $options)
try {
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $document.Load($reader)
} finally { $reader.Dispose() }
function Values($node) {
    $result = [ordered]@{}
    foreach ($field in $node.SelectNodes('Int8|Int32')) {
        $result[$field.GetAttribute('name')] = [int]$field.GetAttribute('value')
    }
    return $result
}
$characters = @(foreach ($character in $document.SelectNodes('/Archive/Array[@name="Character"]/Archive')) {
    $fields = Values $character
    if ($CharacterId.Count -gt 0 -and $fields.ID -notin $CharacterId) { continue }
    [pscustomobject]@{
        CharacterId = $fields.ID
        CachedUtc = if ($fields.Time) { [DateTimeOffset]::FromUnixTimeSeconds($fields.Time).UtcDateTime.ToString('o') } else { $null }
        MeshId = $fields.MeshID
        HeadId = $fields.HeadID
        Breed = $fields.Breed
        Sex = $fields.Sex
        Fatness = $fields.Fatness
        Side = $fields.Side
        Cloth = @(foreach ($layer in $character.SelectNodes('Array[@name="Cloth"]/Archive')) { Values $layer })
        Meshes = @(foreach ($mesh in $character.SelectNodes('Array[@name="Mesh"]/Archive')) { Values $mesh })
    }
})
[pscustomobject]@{
    SchemaVersion = 1
    Source = 'AO CharacterViewer.xml; cached appearance, not guaranteed current equipment'
    CacheWriteUtc = $item.LastWriteTimeUtc.ToString('o')
    Count = $characters.Count
    Characters = $characters
} | ConvertTo-Json -Depth 8
