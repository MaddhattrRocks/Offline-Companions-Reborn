# Converts package/README.md into Nexus Mods BBCode (mod page description).
# Usage: python3 tools/readme_to_nexus.py package/README.md out.txt
# <details> sections become a bold title + [spoiler]...[/spoiler] (Nexus' collapsible block), tables become lists,
# `code` becomes bold. Nexus needs tags properly nested, so every tag opened on a line is closed on it.
import re, sys

src = open(sys.argv[1], encoding='utf-8').read()

# Nexus-specific wording ([Section] in text would read as an unknown BBCode tag)
src = src.replace("`[Combat] EngageOnNotice`", "the `EngageOnNotice` setting (Combat section)")
src = src.replace("Click a section below for the details.", "Click Show under a section below for the details.")
src = src.replace("included with the mod and on [GitHub]", "included in the download (in the mod's folder) and on [GitHub]")
src = re.sub(r"## Installation\n.*?(?=\n## )", """## Installation

Install with Vortex, or manually: extract the zip into your Valheim folder, so `Companions.dll` ends up in `BepInEx/plugins/OfflineCompanionsReborn/`.

**Remove ProfMags' original Offline Companions first.** It doesn't work on Valheim 1.0, and Reborn won't start while it's installed (the BepInEx log says so).

**Requires:** BepInExPack for Valheim (BepInEx and Harmony).
""", src, flags=re.S)

def inline(t):
    t = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'[url=\2]\1[/url]', t)
    t = re.sub(r'\*\*(.+?)\*\*', r'[b]\1[/b]', t)
    t = re.sub(r'`([^`]+)`', r'[b]\1[/b]', t)
    t = t.replace('[b][b]', '[b]').replace('[/b][/b]', '[/b]')
    return t

out = []
lines = src.split('\n')
i = 0
list_depth = 0
def close_lists(to=0):
    global list_depth
    while list_depth > to:
        out.append('[/list]'); list_depth -= 1
table = []
def flush_table():
    global table
    if not table: return
    rows = [r for r in table if not re.match(r'^\|\s*:?-', r)]
    out.append('[list]')
    for r in rows[1:]:
        cells = [c.strip() for c in r.strip().strip('|').split('|')]
        out.append('[*]' + inline(cells[0]) + ' — ' + inline(' | '.join(cells[1:])))
    out.append('[/list]')
    table = []
while i < len(lines):
    l = lines[i]
    if l.startswith('|'):
        close_lists(); table.append(l); i += 1; continue
    flush_table()
    m = re.match(r'^(\s*)- (.*)$', l)
    if m:
        depth = len(m.group(1)) // 2 + 1
        while list_depth < depth:
            out.append('[list]'); list_depth += 1
        close_lists(depth)
        out.append('[*]' + inline(m.group(2)))
        i += 1; continue
    close_lists()
    if l.strip() == '<details>':
        i += 1; continue
    m = re.match(r'^<summary>(?:<b>)?(.*?)(?:</b>)?</summary>$', l.strip())
    if m:
        out.append('[size=4][b]' + m.group(1) + '[/b][/size]')
        out.append('[spoiler]')
        i += 1; continue
    if l.strip() == '</details>':
        while out and out[-1] == '': out.pop()
        out.append('[/spoiler]')
        i += 1; continue
    if l.startswith('# '):
        out.append('[size=6][b]' + l[2:] + '[/b][/size]'); i += 1; continue
    if l.startswith('## '):
        out.append(''); out.append('[size=5][b]' + l[3:] + '[/b][/size]'); i += 1; continue
    if l.startswith('> '):
        out.append('[quote]' + inline(l[2:]) + '[/quote]'); i += 1; continue
    out.append(inline(l))
    i += 1
close_lists(); flush_table()
text = '\n'.join(out)
text = re.sub(r'\n{3,}', '\n\n', text).strip() + '\n'
text = re.sub(r'\[spoiler\]\n+', '[spoiler]\n', text)
open(sys.argv[2], 'w', encoding='utf-8').write(text)
# sanity: tag balance
for tag in ['b', 'list', 'spoiler', 'quote', 'size', 'url']:
    o = len(re.findall(r'\[' + tag + r'[\]=]', text)); c = text.count('[/' + tag + ']')
    print(tag, o, c, 'OK' if o == c else 'MISMATCH')
