#!/usr/bin/env python3
"""Render the README's "## Glossary" section as Nexus BBCode.

    python3 tools/gen_nexus_glossary.py README.md docs/nexus-glossary.bbcode

Markdown tables become [list] items ("Term (value): meaning"), headings become
bold sized lines, anchor links are dropped (Nexus has no in-page anchors).
Paste the output into the mod page description after editing the README glossary.
"""
import re
import sys


def inline(s):
    s = re.sub(r'\[([^\]]+)\]\(#[^)]+\)', r'\1', s)
    s = re.sub(r'\*\*(.+?)\*\*', r'[b]\1[/b]', s)
    s = re.sub(r'\*(.+?)\*', r'[i]\1[/i]', s)
    return s.replace('`', '')


def render(markdown):
    start = markdown.index('## Glossary')
    end = markdown.index('\n## ', start + 1)
    section = markdown[start:end]
    out = ['[b][size=5]Glossary[/size][/b]', '']
    in_list = False
    for line in section.splitlines():
        if line.startswith('## '):
            continue
        if line.startswith('### '):
            if in_list:
                out.append('[/list]')
                in_list = False
            out += ['', f'[b][size=4]{inline(line[4:])}[/size][/b]']
            continue
        if line.startswith('|'):
            cells = [c.strip() for c in line.strip().strip('|').split('|')]
            if set(''.join(cells)) <= set('-: ') or cells[0] in ('Subject', 'Property', 'Action', 'Target'):
                continue
            if not in_list:
                out.append('[list]')
                in_list = True
            if len(cells) == 3:
                out.append(f'[*]{inline(cells[0])} ({inline(cells[1])}): {inline(cells[2])}')
            else:
                out.append(f'[*]{inline(cells[0])}: {inline(cells[1])}')
            continue
        if line.startswith('- '):
            if not in_list:
                out.append('[list]')
                in_list = True
            out.append('[*]' + inline(line[2:]))
            continue
        if in_list:
            out.append('[/list]')
            in_list = False
        out.append(inline(line))
    if in_list:
        out.append('[/list]')
    text = '\n'.join(out).strip() + '\n'
    return re.sub(r'\n{3,}', '\n\n', text)


if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    with open(src, encoding='utf-8') as f:
        text = render(f.read())
    with open(dst, 'w', encoding='utf-8') as f:
        f.write(text)
    print(f'{dst}: {text.count(chr(10))} lines')
