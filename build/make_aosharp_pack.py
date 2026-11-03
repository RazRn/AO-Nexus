#!/usr/bin/env python3
"""Builds AOSharpPack.zip + manifest for embedding in AONexus.exe (Linux build helper).

Input: a folder laid out like AO#\\dist (AOSharp\\ and Plugins\\). Only allowlisted AOSharp
runtime files and plugin files (no .pdb, no AOSharp.exe desktop app, no config.json) are
packed. Plugin windows are .xml files (for example AOSP/UI/HandlerMainWindow/*.xml for
/handler); the game crashes when a plugin opens a window whose file is missing, so every .xml
is packed except library documentation (an .xml next to a .dll of the same name), and the
build fails if a plugin refers to a window file that is not in the pack.
Output mirrors build/Build-AOSharpPack.ps1.
"""
import glob, hashlib, json, os, sys, zipfile, subprocess
src, out, catalog = sys.argv[1], sys.argv[2], sys.argv[3]
revisions = sys.argv[4] if len(sys.argv) > 4 else None
CORE = ['AOSharp.Bootstrap.dll','AOSharp.Bootstrap.dll.config','AOSharp.Common.dll','AOSharp.Core.dll','AOSharp.Core.dll.config',
        'EasyHook.dll','EasyHook32.dll','EasyHook32Svc.exe','EasyLoad32.dll','HtmlAgilityPack.dll','Newtonsoft.Json.dll',
        'Serilog.dll','Serilog.Sinks.Console.dll','Serilog.Sinks.Debug.dll','Serilog.Sinks.File.dll','System.Buffers.dll',
        'System.Diagnostics.DiagnosticSource.dll','System.Memory.dll','System.Numerics.Vectors.dll',
        'System.Runtime.CompilerServices.Unsafe.dll','System.Threading.Channels.dll','System.Threading.Tasks.Extensions.dll']
ALLOWED = ('.dll', '.config', '.json', '.nav', '.navmesh', '.exe', '.xml')
import re
files = []
for name in CORE:
    p = os.path.join(src, 'AOSharp', name)
    if not os.path.isfile(p): sys.exit('Missing AOSharp file ' + name)
    files.append(('AOSharp/' + name, p))
for root, _, names in os.walk(os.path.join(src, 'Plugins')):
    for name in sorted(names):
        if not name.lower().endswith(ALLOWED) or name.lower().endswith('.exe'): continue
        if name.lower().endswith('.xml') and os.path.splitext(name)[0].lower() + '.dll' in {n.lower() for n in names}: continue
        p = os.path.join(root, name)
        files.append(('Plugins/' + os.path.relpath(p, os.path.join(src, 'Plugins')).replace(os.sep, '/'), p))
# Every window file a plugin names (UTF-16 strings such as "\\UI\\DocSettingsView.xml") must be packed.
packed = {f[0].lower() for f in files}
missing_views = set()
for rel, p in files:
    if not rel.lower().endswith('.dll') or not rel.startswith('Plugins/'): continue
    folder = os.path.dirname(rel)
    data = open(p, 'rb').read()
    text = data.decode('utf-16-le', 'ignore') + '\n' + data[1:].decode('utf-16-le', 'ignore')  # both byte alignments
    for ref in set(re.findall(r'(?:[A-Za-z]:)?[\\/]?[A-Za-z0-9_\- \\/]+\.xml', text)):
        if re.match(r'[A-Za-z]:', ref): continue  # absolute paths are the author's own test files
        ref = ref.replace('\\', '/').lstrip('/')
        if '/' not in ref: continue  # bare names are usually documentation, not windows
        if (folder + '/' + ref).lower() not in packed: missing_views.add(rel + ' -> ' + ref)
if missing_views: sys.exit('Plugin window files missing from pack:\n  ' + '\n  '.join(sorted(missing_views)))
cat = json.load(open(catalog))
plugins = []
for group, ids in cat['groups'].items():
    for pid in ids:
        rel = 'Plugins/' + ('AOSP/' if '.' in pid else pid + '/') + pid + '.dll'
        if not any(f[0] == rel for f in files): sys.exit('Catalog plugin missing from pack: ' + rel)
        plugins.append({'Id': pid, 'Group': group, 'Path': rel, 'Suite': 'AOSP' if '.' in pid else 'Classic'})
# Extra catalogs next to plugins.json (Z suite, Nexus Shortcuts) list full entries with their paths.
for extra in sorted(glob.glob(os.path.join(os.path.dirname(catalog), '*.json'))):
    if os.path.abspath(extra) == os.path.abspath(catalog): continue
    for entry in json.load(open(extra))['Plugins']:
        if not any(f[0] == entry['Path'] for f in files):
            sys.exit('Catalog plugin missing from pack: ' + entry['Path'])
        plugins.append(entry)
os.makedirs(out, exist_ok=True)
meta = os.path.join(out, 'plugins.json')
json.dump({'Plugins': plugins}, open(meta, 'w'), indent=1)
files.append(('plugins.json', meta))
if revisions: files.append(('SOURCE-REVISIONS.json', revisions))
files.sort()
entries = []
zpath = os.path.join(out, 'AOSharpPack.zip')
with zipfile.ZipFile(zpath, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for rel, p in files:
        data = open(p, 'rb').read()
        info = zipfile.ZipInfo(rel, date_time=(1980, 1, 1, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        z.writestr(info, data)
        entries.append({'Path': rel, 'Length': len(data), 'Sha256': hashlib.sha256(data).hexdigest()})
manifest = {'PayloadSha256': hashlib.sha256(open(zpath, 'rb').read()).hexdigest(), 'Files': entries}
json.dump(manifest, open(os.path.join(out, 'AOSharpPack.manifest.json'), 'w'), indent=1)
print('AOSharp pack:', len(entries), 'files,', len(plugins), 'plugins,', os.path.getsize(zpath) // 1024, 'KB compressed')
