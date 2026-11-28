import re, sys, os

def strings(path, minlen=5):
    data = open(path, 'rb').read()
    out = []
    # ascii
    for m in re.finditer(rb'[\x20-\x7e]{%d,}' % minlen, data):
        out.append(('a', m.start(), m.group().decode('latin-1')))
    # utf-16le
    for m in re.finditer(rb'(?:[\x20-\x7e]\x00){%d,}' % minlen, data):
        out.append(('w', m.start(), m.group().decode('utf-16-le', 'ignore')))
    out.sort(key=lambda x: x[1])
    return out

path = sys.argv[1]
pats = [p.lower() for p in sys.argv[2:]]
s = strings(path)
print(f'# {path}: {len(s)} strings')
for kind, off, text in s:
    low = text.lower()
    if any(p in low for p in pats):
        print(f'[{kind}] 0x{off:X}: {text}')
