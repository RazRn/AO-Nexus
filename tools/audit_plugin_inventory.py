"""Read-only plugin inventory: hashes and source metadata, never loads a DLL or game.

Run from merged-source with Python 3. Outputs a Danish HTML report and JSON evidence.
The source scan is a triage aid, not a full correctness/security audit.
"""
import argparse
import collections
import hashlib
import html
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROFESSIONS = dict(zip(
    'Adv Agent Crat Doc Enf Engi Fixer Keeper MA MP NT Shade Soldier Trader'.split(),
    ['Adventurer','Agent','Bureaucrat','Doctor','Enforcer','Engineer','Fixer','Keeper',
     'Martial Artist','Meta-Physicist','Nano-Technician','Shade','Soldier','Trader']))
ROLE = {
    'Backstabber': 'Backstab-hjælper; kontrollér profession og positionering.',
    'GraceBot9000': 'Ældre kredit-/GMI-værktøj med transaktions- og klientnær kode.',
    'GraceBot9001': 'Ældre kredit-/GMI-bot med bank-, handels- og IPC-tilstande.',
    'MissionHelper': 'Missionshjælper; præcis funktion og klientkrav skal gennemgås.',
    'NavGen': 'Navigationsværktøj; udviklerfunktion, ikke et normalt standardplugin.',
    'Neko': 'Eksperimentelle klientpatches og bevægelses-/kampfunktioner.',
    'Qubit': 'Åbner et vindue til brugbare genstande og reagerer på zoning.',
    'StackDetector': 'Registrering af stacks; kontrollér hvilke hændelser og genstande der dækkes.',
    'TradeskillLeveler': 'Tradeskill-/levelværktøj; materialeforbrug kræver særskilt vurdering.',
    'Manager.Attack': 'Angrebsstyring.', 'Manager.Inventory': 'Inventory-styring.',
    'Manager.OST': 'OST-/pulling-styring.', 'Manager.Pet': 'Pet-styring.',
    'Manager.PetHunt': 'Pet-jagt.', 'Manager.POH': 'PoH-specifik styring.',
    'Manager.Raid': 'Raidstyring.', 'Manager.SocialPet': 'Social-pet-styring.',
    'Utility.AOIABankManager': 'Bank-/inventory-integration med AOIA.',
    'Utility.AutoItemLevel': 'Automatisk genstandsopgradering.',
    'Utility.BossLogger': 'Logning af bosses.', 'Utility.CombineNSplit': 'Kombinering/opdeling af genstande.',
    'Utility.CrystalFilledByTheSource': 'Hjælp til Crystal Filled by the Source.',
    'Utility.DefendTheGrove': 'Aktivitetsspecifik hjælp til Defend the Grove.',
    'Utility.ICE': 'ICE-værktøj; funktion kræver nærmere kodegennemgang.',
    'Utility.InventoryPrinter': 'Udskrift af inventory.',
    'Utility.MonsterTracker': 'Registrering af monstre.', 'Utility.Relay': 'Videresendelse/relay.',
    'Utility.SimpleAutoSeller': 'Automatisk salg.', 'Utility.TradeskillAndSell': 'Tradeskills efterfulgt af salg.',
    'Z.GMIManager': 'GMI-/markedsstyring.', 'Z.MailManager': 'Mail-/kreditstyring.',
    'Z.NCUManager': 'NCU-/bufftimerinformation.',
    'Z.BattleStationUtility': 'Battlestation-tilmelding.',
    'Z.BloodPlasmaUtility': 'Konvertering af monsterdele til plasma.',
    'Z.CloakUtility': 'Cloak-værktøj; kontrol af genstandsbrug og cooldown.',
    'Z.CoffeeUtility': 'Kaffe-værktøj; kontrol af genstandsbrug.',
    'Z.CookiesUtility': 'Cookie-værktøj; kontrol af genstandsbrug.',
    'Z.LookingForTeamInviteUtility': 'Invitationer via Looking for Team.',
    'Z.MinersUtility': 'Miners-værktøj; aktivitet og handlinger skal afklares.',
    'Z.PearlsUtility': 'Bearbejdning af pearls.', 'Z.SellUtility': 'Salg af genstande til butik.',
    'Z.TeamAcceptUtility': 'Automatisk accept af holdinvitationer.',
}
HOLD = {
    'GraceBot9000': 'Kredit-/markedshandlinger; udeluk fra almindelig automatisk spiltest.',
    'GraceBot9001': 'Kredit-/bank-/markedshandlinger; udeluk fra almindelig automatisk spiltest.',
    'Neko': 'Direkte klientpatches og aktiveret bevægelsesadfærd; kræver særskilt vurdering.',
    'Z.TemplateBuddy': 'Udviklingsskabelon med tom testkommando og arbejdsfase, der stopper sig selv.',
}
COMMON = [
    'Indlæs alene med nødvendig testinstrumentering i en frisk testklient; registrér startfejl og hændelser.',
    'Unload/genindlæs og kontrollér, at kommandoer, events, vinduer og IPC ikke bliver tilbage eller dubleres.',
    'Afprøv zoning, F10, død/genlogin hvor relevant og tab af forbindelse; registrér fejl og ydelse.',
    'Afprøv længere session og det planlagte pluginsæt; et opstartsresultat er ikke en funktionel godkendelse.',
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def text(path):
    return path.read_text(encoding='utf-8-sig', errors='replace')


def suite_map():
    source = text(ROOT / 'src/Launcher/PluginSets.cs')
    source = source.split('static Dictionary<string, string> BuildSuiteMap()')[1].split('return map;')[0]
    pairs = {'ProfessionHandler.' + key: 'Z.Handler.' + key for key in PROFESSIONS}
    for body, prefix in re.findall(r'foreach \(var pair in new\[\] \{(.*?)\}\)\s*map\["pack:([^"]+)"', source, re.S):
        for left, right in re.findall(r'"([^":]+):([^":]+)"', body):
            pairs[prefix + left] = 'Z.' + right
    return pairs


def projects(base):
    result = {}
    for project in base.rglob('*.csproj'):
        if {'bin', 'obj', 'packages'} & set(project.parts):
            continue
        try:
            tree = ET.parse(project)
        except ET.ParseError:
            continue
        nodes = list(tree.iter())
        assembly = next((n.text for n in nodes if n.tag.split('}')[-1] == 'AssemblyName'), project.stem)
        files = []
        for node in nodes:
            if node.tag.split('}')[-1] == 'Compile' and node.get('Include'):
                include = node.get('Include').replace('\\', '/')
                if '$(' not in include:
                    files.extend(p.resolve() for p in project.parent.glob(include) if p.is_file())
        result[assembly] = (project, sorted(set(files)), [n.get('Include').split(',')[0] for n in nodes
                              if n.tag.split('}')[-1] == 'Reference' and n.get('Include')])
    return result


def source_info(entry, indexes):
    assembly = Path(entry['Path']).stem
    item = indexes[entry['Suite']].get(assembly)
    if entry['Id'] == 'Nexus.Shortcuts':
        item = (ROOT / 'src/Shortcuts/NexusShortcutsPlugin.cs', sorted((ROOT / 'src/Shortcuts').glob('*.cs')), [])
    if item is None:
        return {'project': None, 'files': [], 'commands': [], 'references': [], 'indicators': {}}
    project, files, references = item
    commands, evidence = set(), []
    patterns = {
        'native_or_patch': r'DllImport\(|VirtualProtect\(|WriteProcessMemory\(|\b\w*Patcher\.(?:Patch|Unpatch)\(',
        'trade_or_mail': r'\b(?:Trade|GMI|Mail)\.\w+|SendMail\(|MarketBuy|MarketSell',
        'movement': r'MovementController\.Set\(|SetMovement\(',
        'event_subscribe': r'\+=', 'event_unsubscribe': r'-=',
    }
    indicators = collections.Counter()
    for path in files:
        data = text(path)
        commands.update(re.findall(r'Chat\.RegisterCommand\(\s*"([^"\r\n]+)"', data))
        hits = []
        for number, line in enumerate(data.splitlines(), 1):
            if line.lstrip().startswith('//'):
                continue
            for key, pattern in patterns.items():
                if re.search(pattern, line):
                    indicators[key] += 1
                    if key in ('native_or_patch', 'trade_or_mail', 'movement'):
                        hits.append({'line': number, 'kind': key})
        evidence.append({'path': str(path), 'sha256': digest(path), 'indicators': hits})
    return {'project': str(project), 'files': evidence, 'commands': sorted(commands),
            'references': sorted(set(references)), 'indicators': dict(indicators)}


def role(entry, catalog, pairs):
    pid = entry['Id']
    if entry['Group'] == 'Profession handlers':
        return 'Professionshandler: ' + PROFESSIONS[pid.rsplit('.', 1)[-1]] + '.', 'kategori og profession'
    if entry.get('About'):
        return entry['About'], 'pakkens katalogbeskrivelse (ikke funktionstestet)'
    if pid in ROLE:
        return ROLE[pid], 'inventarbeskrivelse; navn/kategori og udvalgt kildekode'
    if pid in pairs:
        counterpart = catalog[pairs[pid]]
        return (counterpart.get('About') or ROLE.get(pairs[pid], counterpart['Id'])) + ' (AOSP-alternativ).', 'Nexus-mapping af funktionelt overlap; ikke bevist identisk'
    return 'Aktivitet: ' + pid.split('.', 1)[-1] + '; undersøg opstart, stop og aktivitetens forudsætninger.', 'navn/kategori; kræver funktionsafklaring'


def cases(entry):
    group, pid = entry['Group'], entry['Id']
    if pid in HOLD:
        return ['Kun statisk gennemgang nu: ' + HOLD[pid], 'Afklar produktbehov og eventuel fjernelse fra standardkataloget før en særskilt testplan.']
    if group == 'Profession handlers':
        return ['Brug ' + PROFESSIONS[pid.rsplit('.', 1)[-1]] + ' med kendte nanos, perks og udstyr.',
                'Kontrollér buffs, kamp, healing/pets hvor relevant, prioriteringer og pause; prøv også afvisning på forkert profession.'] + COMMON
    if group in ('Bots', 'Automatons'):
        return ['Brug den konkrete aktivitet ' + (entry.get('Name') or pid) + ' og en testgruppe med de krævede roller.',
                'Kontrollér start/stop, navigation, målvalg, ledertab, reset og at deaktivering stopper handlinger.'] + COMMON
    if pid == 'Nexus.Shortcuts':
        return ['Kontrollér status/kommandoliste, PID + starttid, timeout og forsvundet klient.',
                'Kontrollér observation af en bevidst startfejl og en hændelsesfejl; tomt svar må ikke give grønt resultat.'] + COMMON
    return ['Afprøv den beskrevne funktion med et dokumenteret før/efter-resultat.',
            'Brug kontrollerede testgenstande, modtagere og hold ved handel, inventory eller invitationer; kontrollér fravalg og stop.'] + COMMON


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--date', required=True)
    parser.add_argument('--pack', type=Path, default=ROOT / 'runtime/Local13Pack')
    args = parser.parse_args()
    pack = args.pack.resolve()
    raw = json.loads(text(pack / 'plugins.json'))['Plugins']
    catalog = {p['Id']: p for p in raw}
    assert len(catalog) == len(raw), 'Duplicate catalog IDs'
    pairs = suite_map()
    assert all(a in catalog and b in catalog for a, b in pairs.items()), 'Mapping references missing plugins'
    reverse = {v: k for k, v in pairs.items()}
    indexes = {'Classic': projects(ROOT.parent.parent / 'aosharp.utils'),
               'AOSP': projects(ROOT.parent.parent / 'aosp-bots'),
               'Z': projects(ROOT / 'runtime/ZSources'), 'Nexus': {}}
    manifest_path = ROOT / 'src/Launcher/obj/EmbeddedAOSharp/AOSharpPack.manifest.json'
    manifest = json.loads(text(manifest_path))
    files = {p['Path'].lower(): p for p in manifest['Files']}
    policy_path = ROOT / 'src/Launcher/Assets/PluginTestPolicy.json'
    policy = {p['Id'].removeprefix('pack:'): p for p in json.loads(text(policy_path))}
    entries = []
    for p in sorted(raw, key=lambda x: (x['Suite'], x['Group'], x['Id'])):
        pid = p['Id']
        dll = (pack / p['Path']).resolve()
        assert dll.is_relative_to(pack) and dll.is_file(), 'Missing or escaping plugin: ' + pid
        sha = digest(dll)
        expected = files.get(p['Path'].lower())
        assert expected and expected['Sha256'].upper() == sha and expected['Length'] == dll.stat().st_size, 'Manifest mismatch: ' + pid
        source = source_info(p, indexes)
        purpose, purpose_basis = role(p, catalog, pairs)
        classification = policy.get(pid, {})
        excluded = classification.get('Class') != 'Candidate' or classification.get('Sha256') != sha
        category = ('Afvent manuel vurdering' if pid in HOLD else
                    'Kandidat til grundpakke' if pid == 'Nexus.Shortcuts' or pid.startswith('Z.Handler.') or pid == 'Z.SyncManager' else
                    'Alternativ med overlap' if pid in pairs else 'Valgfrit / specialiseret')
        entries.append({'id': pid, 'name': p.get('Name') or pid, 'suite': p['Suite'], 'group': p['Group'],
                        'purpose': purpose, 'purpose_basis': purpose_basis, 'proposal': category,
                        'overlap': pairs.get(pid) or reverse.get(pid), 'overlap_basis': 'PluginSets.AospToZ' if pid in pairs or pid in reverse else None,
                        'catalog_note': p.get('Note'), 'current_test_excluded': excluded, 'test_class': classification.get('Class', 'Unknown'),
                        'manual_hold_reason': HOLD.get(pid), 'sha256': sha, 'path': p['Path'], 'manifest_match': True,
                        'source': source, 'catalog_commands': p.get('Commands', []), 'tests': cases(p),
                        'functional_status': 'Ikke dokumenteret i denne gennemgang', 'gameplay_executed': False})
    command_users = collections.defaultdict(list)
    hashes = collections.defaultdict(list)
    for e in entries:
        for command in set(e['source']['commands']) | set(e['catalog_commands']):
            command_users[command.lower()].append(e['id'])
        hashes[e['sha256']].append(e['id'])
    collisions = {k: v for k, v in sorted(command_users.items()) if len(v) > 1}
    for e in entries:
        e['shared_commands'] = [k for k, users in collisions.items() if e['id'] in users]
    report = {'date': args.date, 'launcher_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'catalog_sha256': digest(pack / 'plugins.json'), 'manifest_sha256': digest(manifest_path), 'test_policy_sha256': digest(policy_path),
              'pack_payload_sha256': manifest['PayloadSha256'], 'source_revisions': json.loads(text(pack / 'SOURCE-REVISIONS.json')),
              'scope': 'Read-only inventory, primary-DLL manifest verification and source text triage. No DLL execution, game test or complete source audit.',
              'source_caveat': 'Current local source is not proven byte-for-byte identical to the source used for each DLL; file hashes and packaged provenance are retained separately. Regex indicators are candidates, not confirmed bugs.',
              'count': len(entries), 'mapped_pairs': pairs, 'shared_command_candidates': collisions,
              'identical_primary_dlls': [v for v in hashes.values() if len(v) > 1], 'plugins': entries}
    out = ROOT / 'docs/plugin-inventory'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'inventory.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    render(report, out)
    counts = collections.Counter(e['proposal'] for e in entries)
    print(json.dumps({'plugins':len(entries), 'verified_primary_dlls':len(entries), 'mapped_pairs':len(pairs),
                      'source_projects_found':sum(bool(e['source']['project']) for e in entries),
                      'current_test_excluded':sum(e['current_test_excluded'] for e in entries),
                      'shared_command_names':len(collisions), 'proposals':counts,
                      'identical_primary_dll_groups':len(report['identical_primary_dlls'])}, ensure_ascii=False))


def render(report, out):
    esc = lambda value: html.escape(str(value or '—'))
    rows = []
    for p in report['plugins']:
        commands = sorted(set(p['source']['commands']) | set(p['catalog_commands']))
        indicator = ', '.join(f'{k}: {v}' for k, v in p['source']['indicators'].items()) or 'Ingen mønstre fundet'
        detail = '<ol>' + ''.join('<li>' + esc(s) + '</li>' for s in p['tests']) + '</ol>'
        detail += '<p><b>Beskrivelsens grundlag:</b> ' + esc(p['purpose_basis']) + '</p>'
        detail += '<p><b>Katalognote:</b> ' + esc(p['catalog_note']) + '</p>'
        detail += '<p><b>Udeladt som standardtestmål:</b> ' + ('Ja' if p['current_test_excluded'] else 'Nej; profession skal også matche') + ' · ' + esc(p['test_class']) + '</p>'
        detail += '<p><b>Manuel vurdering:</b> ' + esc(p['manual_hold_reason']) + '</p>'
        detail += '<p><b>Fundne kommandoer:</b> ' + esc(', '.join(commands)) + '</p>'
        detail += '<p><b>Delte kommandonavne (muligt overlap):</b> ' + esc(', '.join(p['shared_commands'])) + '</p>'
        detail += '<p><b>Statiske indikatorer, ikke fejlbevis:</b> ' + esc(indicator) + '</p>'
        detail += '<p><b>Kildeprojekt:</b> ' + esc(p['source']['project']) + '</p>'
        detail += '<p><b>SHA-256 / manifest matcher:</b> <code>' + p['sha256'] + '</code></p>'
        rows.append('<tr><td><b>' + esc(p['id']) + '</b><br><small>' + esc(p['suite'] + ' · ' + p['group']) + '</small></td><td>' +
                    esc(p['purpose']) + '</td><td>' + esc(p['proposal']) + '<br><small>Overlap: ' + esc(p['overlap']) +
                    '</small></td><td><details><summary>Testplan og evidens</summary>' + detail + '</details></td></tr>')
    counts = collections.Counter(e['proposal'] for e in report['plugins'])
    counts_text = ' · '.join(f'{value} {key.lower()}' for key, value in counts.items())
    commands = ''.join('<li><code>/' + esc(k) + '</code>: ' + esc(', '.join(v)) + '</li>' for k, v in report['shared_command_candidates'].items())
    page = '''<!doctype html><html lang="da"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Nexus · Pluginoversigt</title><style>
body{font:16px/1.55 system-ui,sans-serif;background:#0b1621;color:#e1edf4;margin:0}main{max-width:1450px;margin:auto;padding:32px}
h1{font-size:32px;margin-bottom:6px}h2{font-size:21px;color:#62d9ed}p{max-width:1100px}small{color:#a6b8c5}
.box{border:1px solid #355063;padding:18px;margin:22px 0;background:#142532}.warning{border-left:4px solid #e8b453}
input{background:#192e3e;color:white;border:1px solid #6d91a8;padding:13px;font:inherit;width:min(650px,85%)}
.table{overflow-x:auto}table{width:100%;border-collapse:collapse;min-width:900px}th,td{text-align:left;vertical-align:top;padding:14px;border-bottom:1px solid #355063}th{background:#192e3e}td:first-child{width:22%}td:nth-child(2){width:28%}td:nth-child(3){width:22%}summary{cursor:pointer;color:#62d9ed}details p{font-size:14px}code{overflow-wrap:anywhere}a{color:#62d9ed}li{margin:6px 0}
</style><main><h1>113 plugins — hvad har vi, og hvad mangler vi at teste?</h1>
<small>Inventar og forslag · DATE · ingen spil eller plugin-DLL'er startet</small>
<div class="box"><b>113 primære DLL'er matcher buildmanifestet. 33 par har dokumenteret funktionelt overlap i Nexus-mappingen.</b><br>COUNTS</div>
<div class="box warning"><b>Dette er ikke en godkendelsesliste.</b> Ingen af de 113 får status “funktionstestet” på baggrund af denne gennemgang.
Den nuværende test ser efter opstart og registrerede kommandoer/events i 8–18 sekunder. Det er ikke bevis for korrekt gameplay.
Ingen ens primære DLL-hashes blev fundet: overlap er ikke det samme som identiske filer.</div>
<h2>Testudvælgelsen er rettet i lokal test 6</h2><p>GraceBot9000/9001, Neko og TemplateBuddy er nu blokeret i opstartstesten. Ukendte eller ændrede filer kræver ny klassificering. Ét mål indlæses ad gangen sammen med Nexus Shortcuts; professionshandlere skal matche karakteren. Specialiserede plugins kræver særskilt tilvalg. Almindelige pluginsæt og pluginfiler er bevaret.</p>
<p>Opstartsresultater knyttes til DLL-hash, profession og server og vises som opstart, ikke som funktionel godkendelse. De 16 grundpakkefiler og deres direkte pakkede referencer har bestået filkontrol; se <a href="core-file-checks.json">filkontrollens evidens og begrænsninger</a>. Ingen spiltest er kørt som led i denne kontrol.</p>
<h2>Videre kvalifikation</h2><ol>
<li>De fire blokerede plugins kræver særskilt manuel vurdering. TemplateBuddy er en udviklingsskabelon, der fortsat bør vurderes til skjul i standardkataloget.</li>
<li>Brug Z's 14 professionshandlere, Z.SyncManager og Nexus Shortcuts som <em>kandidater</em> til første kvalifikationsrunde, fordi Nexus allerede bruger dem som grundlag. Én relevant handler pr. karakter; ikke alle 16 samtidig. Valget er ikke en dokumenteret kvalitetsrangering over AOSP.</li>
<li>Behold de 33 AOSP-alternativer i en avanceret visning, indtil funktionelle forskelle er undersøgt. Specialiserede plugins tilbydes efter behov.</li>
<li>Test ét plugin ad gangen i en frisk klient, derefter udvalgte understøttede sæt. Brug korrekte professioner, kontrollerede testgenstande og separate server-/rendererresultater.</li>
</ol>
<h2>Testniveauer og resultatkrav</h2><p>Ikke testet → filkontrol bestået → opstart bestået → funktion afprøvet → stabilitet afprøvet.
Gem DLL- og afhængighedshashes, klientversion, server, renderer, profession, pluginindstillinger, tidspunkt, scenarie, varighed og faktisk resultat.
En opdateret DLL skal ikke arve en gammel grøn markering. Ikke anvendelig, sprunget over og afbrudt er særskilte resultater.</p>
<p>Fælles prøveforløb: indlæsning → målrettet funktion → unload/genindlæsning → zoning/F10/genlogin → længere session → godkendt kombination.
Stop ved fejl, hæng eller uventede handlinger. Frisk klient mellem plugins med ukendt oprydning. En automatisk test kan ikke alene bevise alle funktioner eller kombinationer.</p>
<h2>Alle plugins</h2><label for="filter">Filtrér efter navn, suite, funktion eller forslag</label><p><input id="filter" placeholder="Fx Doctor, Mail, AOSP eller manuel"><span id="count"></span></p>
<div class="table"><table><thead><tr><th>Plugin</th><th>Funktion</th><th>Foreslået placering</th><th>Testbehov</th></tr></thead><tbody>ROWS</tbody></table></div>
<h2>Delte kommandonavne — kandidater til konfliktprøver</h2><p>Navnesammenfald kan være tilsigtet, især mellem alternative suiter. Listen beviser ikke en konflikt og er ikke komplet: dynamisk registrerede kommandoer kan mangle.</p><details><summary>Vis kommandolisten</summary><ul>COMMANDS</ul></details>
<h2>Metode og begrænsninger</h2><p>Katalog, buildmanifest, Nexus' AOSP/Z-mapping og lokale C#-projekter er læst. Alle primære DLL'er er hashed uden at blive indlæst.
Kildetekst er scannet for kommandoregistrering, native/patch-kald, handel, bevægelse og event-tilkobling; udvalgte fund er læst manuelt.
Det er ikke en fuld gennemgang af hver funktion. Antal +=/-= beviser ikke manglende oprydning. Biblioteker og AOSharp kan håndtere oprydning andetsteds.
Lokale kildefiler er ikke bevist identiske med byggekilderne; deres hashes og pakkens revisionsoplysninger er gemt hver for sig.</p>
<p>Health Monitor tilføjes særskilt af Nexus og er ikke en af katalogets 113 poster; det skal også med i den senere integrationstest.
Der blev fundet 0 gemte testposter i den aktuelle Nexus-profil ved den foregående gennemgang. Det udelukker ikke tidligere manuelle tests.</p>
<p><a href="inventory.json">Maskinlæsbar evidens med hashes, kildehenvisninger og alle testplaner</a></p></main>
<script>const input=document.querySelector('#filter'),rows=[...document.querySelectorAll('tbody tr')];function filter(){let n=0;const q=input.value.toLocaleLowerCase();for(const r of rows){r.hidden=!r.textContent.toLocaleLowerCase().includes(q);if(!r.hidden)n++;}document.querySelector('#count').textContent=' '+n+' / '+rows.length;}input.addEventListener('input',filter);filter();</script></html>'''
    page = page.replace('DATE', esc(report['date'])).replace('COUNTS', esc(counts_text)).replace('ROWS', ''.join(rows)).replace('COMMANDS', commands)
    (out / 'index.html').write_text(page, encoding='utf-8')


if __name__ == '__main__':
    main()
