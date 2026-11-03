PLUGINOVERSIGT — 30. oktober 2026

Åbn index.html i en browser. Rapporten kan søges på navn, funktion, suite,
overlap og foreslået placering. Hver af de 113 poster har en testplan.
inventory.json indeholder evidensen: DLL-hash, match mod pakkens buildmanifest,
kildefiler med hashes/linjehenvisninger, kommandoer og de 33 overlap-par.

RESULTAT
113 primære plugin-DLL'er findes og matcher buildmanifestets hash og størrelse.
113 kildeprojekter/entrypoints er fundet. Ingen identiske primære DLL'er.
55 AOSP, 48 Z, 9 Classic og Nexus Shortcuts. Health Monitor tilføjes separat.
33 funktionelle AOSP/Z-par er allerede defineret i Nexus; parrene er ikke
certificeret som funktionsmæssigt identiske.
24 delte kommandonavne er kandidater til konfliktprøver, ikke bevis for fejl.

Foreslået produktinddeling (ingen konfiguration ændret):
16 kandidater til grundpakke: 14 Z-handlere, Z.SyncManager, Nexus Shortcuts.
33 AOSP-alternativer med kendt overlap.
60 valgfrie/specialiserede plugins.
4 til manuel vurdering: GraceBot9000, GraceBot9001, Neko, Z.TemplateBuddy.

RETTET I LOKAL TEST 6
Den tidligere noter/risikoliste-model overså de fire plugins til manuel
vurdering. En indlejret testpolitik klassificerer nu alle 113 filer ved ID
og SHA-256: 15 grundpakkemål, Nexus Shortcuts som instrumentering, 93
scenariekrævende plugins og 4 blokerede. Ukendte og ændrede filer udelades.
Standardvalget er grundpakkens kandidater; kun matchende professionshandlere
testes. Ét mål indlæses ad gangen, og en observeret opstartsfejl stopper
resten. Specialiserede plugins kræver eksplicit tilvalg og passende scenarie.
De 16 grundpakkefiler og deres direkte pakkede referencer har bestået hash-
og metadatakontrol; se core-file-checks.json. Ingen entrypoints blev kørt.
Opstartsresultater gemmer DLL-hash, profession og server. Ældre resultater
uden hash og resultater for ændrede DLL'er bruges ikke som aktuelle mærker.
Dette ændrer kun testværktøjet; almindelige pluginsæt er bevaret.

KONKRETE KILDER
- src/Launcher/PluginTestPolicy.cs og Assets/PluginTestPolicy.json: testudvælgelse.
- src/Launcher/PluginTestDialog.cs: Batches og RunAsync.
- src/Launcher/PluginHealth.cs: Evaluate og StatusText (opstart, ikke gameplay).
- src/Launcher/Models.cs: PluginTestRecord gemmer hash/profession/server.
- src/Launcher/PluginSets.cs: BuildSuiteMap og EnsureStarterSets.
- aosharp.utils/GraceBot9000/Main.cs og GraceBot9001/Main.cs.
- aosharp.utils/Neko/Main.cs: initialisering, OnUpdate og patchfunktioner.
- runtime/ZSources/buddies/TemplateBuddy/TemplateBuddy.cs og
  StateMachine/States/WorkState.cs.
De præcise lokale kildehenvisninger og deres hashes er i inventory.json.

NÆSTE PRØVER
1. Plugins > Test plugins > Grundpakkens kandidater på en testkarakter.
   Genstart klienten ved ukendt unload-oprydning; opstart er kun første trin.
2. Afprøv pluginets konkrete funktion og derefter længere sessioner og sæt.
3. Registrér også klientversion, renderer, afhængigheder, indstillinger,
   scenarie og varighed. De indgår endnu ikke i de gemte opstartsresultater.
4. Undersøg funktionelle forskelle før udfasning af AOSP-alternativer.
   Ingen plugins er fjernet alene på baggrund af overlappende navne.

AFGRÆNSNING
Dette er et inventar med statisk indledende sortering og udvalgt manuel
kodelæsning. Det er ikke en fuld kodegennemgang af alle 113 plugins.
Ingen spiltest er kørt, og brugerens launcher-/spilindstillinger eller
pluginpakker er ikke ændret. Filkontrollen læser CLR-metadata i reflection-
only-context; plugin-kode udføres ikke. Filmatch er ikke funktionsbevis.
Kildescanningen bruger tekstmønstre; dynamiske kommandoer kan mangle, og antal
eventtilkoblinger/-frakoblinger kan ikke alene bevise oprydningsfejl.
Lokale kilder kan afvige fra byggekilderne; rapporten gemmer begge slags
proveniens separat. Ingen kildekode er kopieret ind i rapporten.

GENKØR
Fra merged-source med Python 3.9 eller nyere:
python tools/audit_plugin_inventory.py --date 2026-10-30
Kræver runtime/Local13Pack, runtime/ZSources, den indlejrede pakkes manifest
under src/Launcher/obj/EmbeddedAOSharp og de lokale søsterrepositories
aosp-bots samt aosharp.utils. Ingen internetadgang eller gameplay kræves.

Filkontrol (32-bit Windows PowerShell):
%WINDIR%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Inspect-CorePluginFiles.ps1
Launcherens isolerede tests: tests\Verify-PluginsAndServerRules.ps1 og
tests\Verify-PluginStartupFlow.ps1. De injicerer ikke plugins i spillet.

VERIFIKATION AF TEST 6
436 policy-/server-/pluginkontroller, 266 simulerede opstartsforløbskontroller
og 45 tema-/scroll-/redigeringskontroller bestået. Dialogen er gengivet på
dansk og engelsk ved 100 og 150 procent skalering. Disse 747 kontroller
verificerer launcherens testværktøj, ikke 747 gameplay-scenarier.
