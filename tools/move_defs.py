#!/usr/bin/env python3
"""Move defs, with the comment lines above them, from data/defs files into a mod's defs file.

    python3 tools/move_defs.py OUT.json FILE:KIND:NAME [FILE:KIND:NAME ...]

The text is moved as it stands (comments and layout kept), so a trade can be lifted out of the
core into a mod (docs/MODDING.md) without losing what the defs say about themselves.
"""
import json, re, sys


def elements(text):
    """(kind, start, end) of every element object of every top-level array, by scanning the text."""
    out, stack, i, n, key, last_string, elem = [], [], 0, len(text), None, None, None
    while i < n:
        c = text[i]
        if text.startswith('//', i):
            i = text.index('\n', i) if '\n' in text[i:] else n
            continue
        if c == '"':
            j = i + 1
            while text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            last_string = text[i + 1:j]
            i = j + 1
            continue
        if c in '{[':
            if c == '[' and len(stack) == 1:
                key = last_string
            if c == '{' and len(stack) == 2 and stack[-1] == '[':
                elem = i
            stack.append(c)
        elif c in '}]':
            stack.pop()
            if c == '}' and len(stack) == 2 and elem is not None:
                out.append((key, elem, i + 1))
                elem = None
        i += 1
    return out


def name_of(obj_text):
    m = re.search(r'"name"\s*:\s*"([^"]+)"', obj_text)
    return m.group(1) if m else None


def main():
    out_path, specs = sys.argv[1], sys.argv[2:]
    wanted = {}
    for s in specs:
        f, kind, name = s.split(':')
        wanted.setdefault(f, set()).add((kind, name))
    moved = {}
    for f, items in wanted.items():
        text = open(f).read()
        cuts = []
        for kind, start, end in elements(text):
            if (kind, name_of(text[start:end])) not in items:
                continue
            # The comment lines right above it go with it.
            line_start = text.rfind('\n', 0, start) + 1
            head = line_start
            while True:
                prev = text.rfind('\n', 0, head - 1) + 1
                if head == 0 or not text[prev:head].strip().startswith('//'):
                    break
                head = prev
            # Its comma: the one after it, else the one before it.
            tail = end
            m = re.match(r'\s*,', text[end:])
            if m:
                tail = end + m.end()
            else:
                k = text.rfind(',', 0, head)
                if k >= 0 and text[k + 1:head].strip() == '':
                    head = k
            # The rest of its last line (a trailing comment) goes too.
            nl = text.find('\n', tail)
            if nl >= 0 and text[tail:nl].strip().startswith('//') or (nl >= 0 and text[tail:nl].strip() == ''):
                tail = nl
            body = text[line_start:end]
            comments = text[head if text[head] != ',' else head + 1:line_start]
            moved.setdefault(kind, []).append((comments + body).strip('\n'))
            cuts.append((head, tail))
            items.discard((kind, name_of(text[start:end])))
        for head, tail in sorted(cuts, reverse=True):
            text = text[:head] + text[tail:]
        open(f, 'w').write(text)
        if items:
            sys.exit(f'not found in {f}: {sorted(items)}')
    parts = []
    for kind, bodies in moved.items():
        parts.append(f'  "{kind}": [\n' + ',\n'.join('    ' + b.strip() if not b.startswith(' ') else b for b in bodies) + '\n  ]')
    open(out_path, 'w').write('{\n' + ',\n'.join(parts) + '\n}\n')


if __name__ == '__main__':
    main()
