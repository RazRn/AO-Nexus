#!/usr/bin/env python3
"""Builds Z's AOSharp plugin suite (gitlab.com/Zzzzzzzzzzzzzzzzzzzzzz: buddies, handlers,
managers, utilities) from source with Roslyn and the .NET Framework 4.8 reference
assemblies, for embedding in the local AOSharp pack.

usage: build_z_suite.py <repos dir> <nuget dir> <aosharp dir> <nav dir> <out dir>

  repos dir   folder holding the four cloned repositories
  nuget dir   extracted NuGet packages as <id>.<version>/ (lower case) plus ref48/
  aosharp dir AOSharp.Core.dll / AOSharp.Common.dll to compile against (ours, d55eb12 + fixes;
              the API matches AOSharpSDK 1.0.106 that the projects reference)
  nav dir     AOSharp.SharpNav / Recast / NavmeshMovementController / cai-* to use
  out dir     receives Buddies/, Handlers/, Managers/, Utilities/ like the author's own releases

On Windows set NEXUS_CSC to csc.exe and NEXUS_REF48 to the v4.8 reference folder.
The NuGet argument also accepts the global id/version cache; NEXUS_NUGET_EXTRA
can name a second global cache holding packages not restored in the first.

Each repository builds into one folder, as in the author's releases: the plugins load their
windows from <plugin folder>\\UI\\... and their dependencies from the same folder.
"""
import glob, os, re, shutil, subprocess, sys, xml.etree.ElementTree as ET

repos, nuget, aosharp, nav, out = sys.argv[1:6]
CSC = [os.environ['NEXUS_CSC']] if os.environ.get('NEXUS_CSC') else ['dotnet', '/usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll']
REF = os.environ.get('NEXUS_REF48') or glob.glob(os.path.join(nuget, 'ref48', 'build', '.NETFramework', 'v4.8'))[0]
NS = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
FOLDERS = {'buddies': 'Buddies', 'handlers': 'Handlers', 'managers': 'Managers', 'utilities': 'Utilities'}
OURS = {'AOSharp.Core': aosharp, 'AOSharp.Common': aosharp}
NAV = {'AOSharp.SharpNav', 'AOSharp.Recast', 'NavmeshMovementController', 'cai-util', 'cai-nav', 'cai-nav-rcn',
       'cai-nmgen', 'cai-nmgen-rcn', 'cai-nmbuild'}


def win(path):
    return path.replace('\\', '/')


def package_dll(hint):
    """packages\\Id.Version\\lib\\tfm\\X.dll -> <nuget>/<id>.<version>/lib/tfm/X.dll"""
    m = re.search(r'packages[\\/]+([^\\/]+)[\\/]+(.*)$', hint)
    if not m:
        return None
    pkg, rest = m.group(1).lower(), win(m.group(2))
    # Windows uses NuGet's global <id>/<version> cache rather than a flat extraction.
    for cache in filter(None, [nuget, os.environ.get('NEXUS_NUGET_EXTRA')]):
        for match in re.finditer(r'\.(?=\d)', pkg):
            candidate = os.path.join(cache, pkg[:match.start()], pkg[match.end():], rest)
            if os.path.isfile(candidate):
                return candidate
    candidates = [d for d in os.listdir(nuget) if os.path.isdir(os.path.join(nuget, d))
                  and (d == pkg or pkg.startswith(d + '.') or d.startswith(pkg.rsplit('.', 1)[0]))]
    for d in sorted(candidates, key=lambda d: (d != pkg, -len(d))):
        p = os.path.join(nuget, d, rest)
        if os.path.exists(p):
            return p
    return None


def build_project(csproj, outdir, built):
    root = ET.parse(csproj).getroot()
    pdir = os.path.dirname(csproj)
    name = root.find('.//m:AssemblyName', NS).text
    sources = []
    for c in root.findall('.//m:Compile', NS):
        sources += sorted(glob.glob(os.path.join(pdir, win(c.get('Include')))))
    refs = []
    for r in root.findall('.//m:Reference', NS):
        ref = r.get('Include').split(',')[0].strip()
        hint = r.find('m:HintPath', NS)
        if ref in OURS:
            refs.append(os.path.join(OURS[ref], ref + '.dll'))
        elif ref in NAV and os.path.exists(os.path.join(nav, ref + '.dll')):
            refs.append(os.path.join(nav, ref + '.dll'))
        elif ref in built:
            refs.append(built[ref])
        elif hint is not None and 'packages' in hint.text:
            p = package_dll(hint.text)
            if not p:
                raise SystemExit(f'{name}: package for {ref} not found ({hint.text})')
            refs.append(p)
        elif os.path.exists(os.path.join(REF, ref + '.dll')):
            refs.append(os.path.join(REF, ref + '.dll'))
        else:
            raise SystemExit(f'{name}: reference {ref} not found')
    unsafe = any((e.text or '').strip().lower() == 'true' for e in root.findall('.//m:AllowUnsafeBlocks', NS))
    dll = os.path.join(outdir, name + '.dll')
    args = CSC + ['-nologo', '-noconfig', '-nostdlib+', '-target:library', '-langversion:latest', '-optimize+',
                  '-deterministic+', '-define:TRACE', '-nowarn:CS0162,CS0168,CS0169,CS0219,CS0414,CS0618,CS0649,CS1998,CS4014,CS8321',
                  f'-out:{dll}', f'-r:{REF}/mscorlib.dll', f'-lib:{REF}', f'-lib:{REF}/Facades']
    # MSBuild adds System.Core to every .NET 4 project implicitly; some projects rely on that.
    refs += [os.path.join(REF, 'System.Core.dll'), os.path.join(REF, 'System.dll')]
    args += [f'-r:{REF}/Facades/netstandard.dll'] + [f'-r:{r}' for r in sorted(set(refs))]
    if unsafe:
        args.append('-unsafe+')
    res = subprocess.run(args + sources, capture_output=True, text=True)
    errors = [l for l in res.stdout.splitlines() if ': error ' in l]
    if res.returncode != 0 or errors:
        raise SystemExit(f'{name} failed:\n' + '\n'.join(errors[:20] or res.stdout.splitlines()[-20:]))
    # App.config becomes <name>.dll.config (binding redirects), content files are copied as-is.
    cfg = os.path.join(pdir, 'App.config')
    if os.path.exists(cfg):
        shutil.copy(cfg, dll + '.config')
    content = 0
    for kind in ('Content', 'None'):
        for c in root.findall(f'.//m:{kind}', NS):
            copy = c.find('m:CopyToOutputDirectory', NS)
            if copy is None or copy.text == 'Never':
                continue
            for src in glob.glob(os.path.join(pdir, win(c.get('Include')))):
                rel = os.path.relpath(src, pdir)
                if rel.lower() == 'app.config':
                    continue
                dst = os.path.join(outdir, rel)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy(src, dst)
                content += 1
    # Every package/nav dependency travels with the plugin, like the author's bin\Release.
    for r in refs:
        if r.startswith(REF) or r.startswith(aosharp):
            continue
        dst = os.path.join(outdir, os.path.basename(r))
        if not os.path.exists(dst):
            shutil.copy(r, dst)
            if os.path.exists(r + '.config'):
                shutil.copy(r + '.config', dst + '.config')
    built[name] = dll
    return name, len(sources), content


def project_order(paths):
    # Handler.Generic first: every profession handler references it.
    return sorted(paths, key=lambda p: (not p.endswith('Handler.Generic.csproj'), p.lower()))


summary = []
for repo, folder in FOLDERS.items():
    outdir = os.path.join(out, folder)
    # Only clean this build's named child directory, including on Windows.
    resolved = os.path.realpath(outdir)
    output_root = os.path.realpath(out)
    if os.path.commonpath([resolved, output_root]) != output_root or resolved == output_root or os.path.islink(outdir):
        raise SystemExit(f'Unsafe build output directory: {outdir}')
    shutil.rmtree(outdir, ignore_errors=True)
    os.makedirs(outdir)
    built = {}
    for csproj in project_order(glob.glob(os.path.join(repos, repo, '*', '*.csproj'))):
        name, n, content = build_project(csproj, outdir, built)
        summary.append((folder, name, n, content))
        print(f'Built {folder}/{name}', flush=True)
for folder, name, n, content in summary:
    print(f'{folder:10} {name:30} {n:3} sources {content:3} content files')
print(len(summary), 'plugins built')
