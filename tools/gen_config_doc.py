# Writes package/CONFIGURATION.md from the settings in src/Companions/Core/ModConfig.cs.
# Usage: python3 tools/gen_config_doc.py src/Companions/Core/ModConfig.cs package/CONFIGURATION.md
import re,sys
src=open(sys.argv[1]).read()
out=sys.argv[2]
init=src[src.index('internal static void Init(ConfigFile cfg)'):]
init=init[:init.index('MigrateAndClean(cfg);')]
rows=[]
for m in re.finditer(r'^\t\t\w+ = Bind\(cfg, "([^"]+)", "([^"]+)", (.*)\);$',init,re.M):
    sec,key,rest=m.groups()
    dm=re.match(r'(.*?), "((?:[^"\\]|\\.)*)"(?:, (.*))?$',rest)
    d,desc,rng=dm.group(1),dm.group(2).replace('\\"','"').replace('\\\\','\\'),dm.group(3)
    rows.append((sec,key,d,desc,rng))
def fmt_num(x):
    x=x.strip().rstrip('f')
    try:
        v=float(x)
        return ('%g'%v)
    except: return x
def fmt_default(d):
    d=d.replace('defaultValue:','').strip()
    if d=='true': return 'On'
    if d=='false': return 'Off'
    if d.startswith('KeyCode.'): return '`'+d[8:]+'`'
    if d.startswith('"'):
        v=d[1:-1]
        if len(v)>60: return None
        return '`'+v+'`'
    return fmt_num(d)
def fmt_range(r):
    if not r: return ''
    if 'AcceptableValueList' in r:
        return 'Options: '+', '.join('`%s`'%v for v in re.findall(r'"([^"]*)"',r))
    m=re.search(r'\(([^,]+), ([^)]+)\)',r)
    return '%s – %s'%(fmt_num(m.group(1)),fmt_num(m.group(2))) if m else ''
NAMES={'GearRepair':'Gear Repair','RepairAndRestock':'Repair and Restock'}
INTRO={
'General':'The basics: what a companion costs, its health, stamina and carrying limits, and how far "home" reaches.',
'Controls':'Keys and how far your commands reach.',
'Follow':'How companions follow you around.',
'Movement':'How companions move: speeds, how far they see, and how they get around (doors, steps, water, jumps).',
'Combat':'When companions fight and how they pick weapons and shields.',
'Food':'When companions drink meads.',
'Stamina':'How fast companions use and regain stamina.',
'Rest':'Sleeping in beds, sitting by fires and the Rested buff.',
'Death':'What happens when a companion dies, and how it comes back.',
'Looting':'Picking up what a kill dropped.',
'GearRepair':'Repairing a companion\'s own weapons, armor and tools at a crafting station.',
'Gathering':'The Wood, Stone, Ore and Forage jobs.',
'Hunting':'The Hunt job.',
'Fishing':'The Fish job.',
'Farming':'The Farm job.',
'Smelting':'The Smelt job.',
'Cooking':'The Cook job.',
'RepairAndRestock':'The Repair and Restock job (looking after your base).',
'Idle':'What a companion does when its job has nothing to do.',
'Speech':'How often companions talk and how long their words stay on screen.',
'Advanced':'Fine-tuning for timings and distances. Most players never need to touch these.',
}
order=[]
for r in rows:
    if r[0] not in order: order.append(r[0])
L=[]
L.append('# Offline Companions Reborn — Settings Reference\n')
L.append('Every setting in the mod, what it does and what it starts at.\n')
L.append('**Where to change them:** in game, press `F8` to open the settings panel. You can also edit `BepInEx/config/Maddhattr.OfflineCompanionsReborn.cfg` with any text editor while the game is closed.\n')
L.append('**Tips**\n')
L.append('- A setting shown as a share (like `0.5`) means 50%. `0.25` is 25%, `1` is 100%.')
L.append('- Distances are in meters. For scale, a player is about 2 m tall and a workbench\'s build range is 20 m.')
L.append('- **List settings** all use the same format, separated by commas:')
L.append('  - `*` = everything')
L.append('  - `Name` = this one')
L.append('  - `-Name` = never this one')
L.append('  - `Name:Number` = this one, with its own amount (`0` = no limit)')
L.append('  - Names can be the item\'s or creature\'s in-game name or its internal (prefab) name. The names you can use for cooking, hunting and fishing are written to `OfflineCompanionsReborn.Cooking.txt`, `.Hunting.txt` and `.Fishing.txt` in the config folder each time the game starts.')
L.append('- Updating from an older version? Settings you had changed are moved to their new names automatically the first time you load in.\n')
L.append('## Sections\n')
for s in order:
    L.append('- [%s](#%s)'%(NAMES.get(s,s), NAMES.get(s,s).lower().replace(' ','-')))
L.append('')
for s in order:
    L.append('## %s\n'%NAMES.get(s,s))
    L.append(INTRO.get(s,'')+'\n')
    for sec,key,d,desc,rng in rows:
        if sec!=s: continue
        dv=fmt_default(d)
        meta=[]
        meta.append('Default: '+(dv if dv is not None else 'see below'))
        rr=fmt_range(rng)
        if rr: meta.append(rr if rr.startswith('Options') else 'Range: '+rr)
        L.append('**%s** — %s  '%(key,' · '.join(meta)))
        L.append(desc)
        if dv is None:
            v=d.strip()[1:-1]
            if key=='MeadFocus': v=v.replace('; ',';\n')
            L.append('\nDefault value:\n\n```\n%s\n```'%v)
        L.append('')
open(out,'w').write('\n'.join(L).rstrip()+'\n')
print(len(rows),'settings',len(order),'sections')
