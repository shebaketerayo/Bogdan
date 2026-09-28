import argparse
import os
import re

from px import BACKGROUNDS, FILL, PALS, render, showcase

HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.join(HERE, 'build')
DOCS = os.path.join(HERE, '..', '..', 'docs')

W, H = 48, 40
GROUND = 38            # lowest fill row; the outline takes row 39
FRAMES = []            # (name, grid, head point)
ANIMS = []


def canvas():
    return [['.'] * W for _ in range(H)]

def copy(g):
    return [r[:] for r in g]

# a literal row may end in '|NN' = its canvas row, the following rows continue from it;
# a literal without tags stands on the ground
def parse(text):
    lines = text.split('\n')
    while lines and lines[0].strip() == '': lines.pop(0)
    while lines and lines[-1].strip() == '': lines.pop()
    out, row, tagged = [], None, False
    for l in lines:
        body, tag = (l.split('|', 1) + [''])[:2]
        tag = tag.strip()
        if tag[:2].isdigit() or tag[:1].isdigit():
            num = ''
            for ch in tag:
                if ch.isdigit(): num += ch
                else: break
            row = int(num); tagged = True
        elif row is not None:
            row += 1
        out.append([row, body.rstrip()])
    if not tagged:
        y0 = GROUND - len(out) + 1
        for i, o in enumerate(out): o[0] = y0 + i
    return out

# '.' and ' ' keep the pixel below, '_' erases it
def lit(g, text, dx=0, dy=0):
    for row, s in parse(text):
        for x, ch in enumerate(s):
            if ch in '. ': continue
            xx, yy = x + dx, row + dy
            if 0 <= xx < W and 0 <= yy < H: g[yy][xx] = '.' if ch == '_' else ch
    return g

def stamp(g, part, x0, y0):
    y0 += getattr(part, 'dy', 0)
    for y, r in enumerate(part):
        for x, ch in enumerate(r):
            if ch == '.': continue
            xx, yy = x0 + x, y0 + y
            if 0 <= xx < W and 0 <= yy < H: g[yy][xx] = ch

def outline(g):
    h, w = len(g), len(g[0])
    add = []
    for y in range(h):
        for x in range(w):
            if g[y][x] != '.': continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                xx, yy = x + dx, y + dy
                if 0 <= xx < w and 0 <= yy < h and g[yy][xx] in FILL:
                    add.append((x, y)); break
    for x, y in add: g[y][x] = 'o'
    return g

def clean(g):
    rm = []
    for y in range(H):
        for x in range(W):
            if g[y][x] != 'o': continue
            ok = False
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < W and 0 <= yy < H and g[yy][xx] in FILL: ok = True
            if not ok: rm.append((x, y))
    for x, y in rm: g[y][x] = '.'

def outlined(rows):
    w = max(len(r) for r in rows) + 2
    g = [['.'] * w for _ in range(len(rows) + 2)]
    for y, r in enumerate(rows):
        for x, ch in enumerate(r):
            if ch not in '. ': g[y + 1][x + 1] = ch
    return outline(g)

# a literal drawn in front with its own outline; from row open_from down the outline is not drawn
# over existing fill, so the part grows out of the body without a seam
def part(g, text, dx=0, dy=0, open_from=None):
    tmp = canvas()
    lit(tmp, text, dx, dy)
    outline(tmp)
    for y in range(H):
        for x in range(W):
            if tmp[y][x] == '.': continue
            if tmp[y][x] == 'o' and open_from is not None and y >= open_from and g[y][x] in FILL: continue
            g[y][x] = tmp[y][x]

def shear(g, xa, xb, da, db, y1=H):
    src = copy(g)
    for y in range(y1):
        for x in range(W): g[y][x] = '.'
    for x in range(W):
        t = 0.0 if x <= xa else (1.0 if x >= xb else (x - xa) / float(xb - xa))
        d = int(round(da + (db - da) * t))
        for y in range(y1):
            ch = src[y][x]
            if ch == '.': continue
            if 0 <= y + d < H: g[y + d][x] = ch

def breathe(g, ycut, x0=0, x1=W):
    for y in range(0, ycut):
        for x in range(x0, x1):
            g[y][x] = g[y + 1][x]


HEAD = [
    "..xx..........xx....",
    ".xnnx........xnnx...",
    ".xnnnx......xnnnx...",
    ".qnnnqbbsbsbbnnnbb..",
    "qqqnqqbsbbsbsbbnbbb.",
    "qqqqqbbbbbbbbbbbbbbb",
    "qqqqbbbbbbbbbbbbbbbb",
    "qqbbbeeebbbbbeeebbbb",
    "bsbbekkhebbbekkhebbb",
    "bbbbekkkebbbekkkebbb",
    "bssbekkkebbbekkkebsb",
    "bbbbbeeebbmbbeeebbbb",
    "bbbbbbbbmnnnmbbbbbbb",
    ".bbbbbmmmmnmmmmbbbb.",
    "..bbbbmmmdmdmmmbbb..",
    "...bbbbbmmmmmbbbb...",
    ".....bbbbbbbbbb.....",
]
EYE_ROW, EYE_L, EYE_R, MOUTH_ROW = 7, 4, 12, 12
EYES = {
    'open':  ["beeeb", "ekkhe", "ekkke", "ekkke", "beeeb"],
    'half':  ["bbbbb", "ooooo", "ekkhe", "ekkke", "beeeb"],
    'shut':  ["bbbbb", "bbbbb", "obbbo", "booob", "bbbbb"],
    'happy': ["bbbbb", "booob", "obbbo", "bbbbb", "bbbbb"],
    'up':    ["ekkhe", "ekkke", "ekkke", "beeeb", "bbbbb"],
    'tight': ["bbbbb", "obbbb", "boooo", "obbbb", "bbbbb"],
    'soft':  ["bbbbb", "bbbbb", "booob", "ekkke", "beeeb"],
}
EYES_R = {'tight': ["bbbbb", "bbbbo", "oooob", "bbbbo", "bbbbb"]}
MOUTH = {
    'norm':  None,
    'o':     ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmmmmnmmmmbbbb.", "..bbbbmmmokommmbbb..", "...bbbbbmmommbbbb...", ".....bbbbbbbbbb....."],
    'meow':  ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmmmmnmmmmbbbb.", "..bbbbmmmkkkmmmbbb..", "...bbbbbmnnnmbbbb...", ".....bbbbbbbbbb....."],
    'meow2': ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmmmmnmmmmbbbb.", "..bbbbmmkkkkkmmbbb..", "...bbbbmknnnkmbbb...", ".....bbbbkkkbbb....."],
    'yawn':  ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmmmmnmmmmbbbb.", "..bbbbmkkkkkkkmbbb..", "...bbbbknnnnnkbbb...", "....bbbbmknkmbbb....", ".....bbbbbbbbbb....."],
    'lick':  ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmmmmnmmmmbbbb.", "..bbbbmmmdndmmmbbb..", "...bbbbbmmnmmbbbb...", ".....bbbbbbbbbb....."],
    'smile': ["bbbbbbbbmnnnmbbbbbbb", ".bbbbbmdmmnmmdmbbbb.", "..bbbbmmdmdmdmmbbb..", "...bbbbbmmmmmbbbb...", ".....bbbbbbbbbb....."],
}


# the head grew a row at the top; stamping it a row higher keeps the head points of all poses
class HeadGrid(list):
    dy = -1


def head(eyes='open', mouth='norm', eyes_r=None):
    rows = [list(r) for r in HEAD]
    m = MOUTH[mouth]
    if m:
        while len(rows) < MOUTH_ROW + len(m): rows.append(list('.' * 20))
        for i, r in enumerate(m): rows[MOUTH_ROW + i] = list(r)
    el = EYES[eyes]
    er = EYES_R.get(eyes_r or eyes, EYES[eyes_r or eyes])
    for i in range(5):
        for j in range(5):
            rows[EYE_ROW + i][EYE_L + j] = el[i][j]
            rows[EYE_ROW + i][EYE_R + j] = er[i][j]
    return HeadGrid(outlined([''.join(r) for r in rows]))

HEAD_CX, HEAD_CY = 11, 9     # between the eyes


# specs: (hip x, paw dx, lift, near[, top, bot]); far legs are drawn first, in the shade colour, so the near
# ones cover them, and a near leg gets an outline where it overlaps a far one so the two never merge
def legs(g, specs, top, bot=GROUND):
    owner = {}
    for sp in sorted(specs, key=lambda s: 1 if s[3] else 0):
        hx, dx, lift, near = sp[0], sp[1], sp[2], sp[3]
        w = 4 if near else 3
        x0 = hx if near else hx + 1
        t0 = (sp[4] if len(sp) > 4 else top) - (1 if near else 0)
        b0 = (sp[5] if len(sp) > 5 else bot) - lift
        n = max(1, b0 - t0)
        cells = []
        for r in range(t0, b0 + 1):
            t = (r - t0) / float(n)
            off = int(round(dx * t + (0.01 if dx > 0 else -0.01)))
            if lift >= 2 and r >= b0 - 1: off -= 1 if dx >= 0 else -1
            cells.append((r, x0 + off))
        ch = 'b' if near else 'd'
        for r, cx in cells:
            if not (0 <= r < H): continue
            paw = r >= b0 - 1
            bar = near and not paw and (b0 - r) in (3, 5)
            for k in range(w):
                x = cx + k
                if not (0 <= x < W): continue
                if paw and r == b0 and k in (0, w - 1) and w >= 4: continue
                g[r][x] = 'f' if paw else ('s' if bar else ch)
                owner[(x, r)] = near
        if near:
            for r, cx in cells:
                for ox in (cx - 1, cx + w):
                    if owner.get((ox, r)) is False: g[r][ox] = 'o'
    return g


def add(name, g, hp):
    outline(g)
    clean(g)
    edge = [(x, y) for y in range(H) for x in range(W) if g[y][x] in FILL and (x in (0, W - 1) or y in (0, H - 1))]
    if edge: print('EDGE', name, edge[:6])
    FRAMES.append((name, g, hp))
    return g

def anim(name, loop, *frames):
    ANIMS.append('%-10s %-4s %s' % (name, 'loop' if loop else 'once', ' '.join('%s:%s' % (n, d) for n, d in frames)))


SHOWCASE = [['sit0', 'happy0', 'meow1', 'loaf0', 'sleep0'], ['walk1', 'walk3', 'walk5', 'knead0', 'scratch1']]

ap = argparse.ArgumentParser(description='builds the cat frames into build/frames.txt for gen_cs.py')
ap.add_argument('--preview', metavar='REGEX', help='render the matching frames to build/preview.png')
ap.add_argument('--palette', default='bogdan', choices=sorted(PALS))
ap.add_argument('--scale', type=int, default=6)
ap.add_argument('--bg', default='grey', choices=sorted(BACKGROUNDS))
ap.add_argument('--marks', action='store_true', help='mark the head point (cyan) and the anchor (red) in the preview')
ap.add_argument('--showcase', action='store_true', help='render docs/preview.png')
args = ap.parse_args()

# the pose files run in this namespace, they only split the art into readable chunks
for poses in ('bogdan_poses.py', 'bogdan_poses2.py', 'bogdan_poses3.py'):
    exec(open(os.path.join(HERE, poses), encoding='utf-8').read())


def emit(path):
    names = set(n for n, g, hp in FRAMES)
    for a in ANIMS:
        for tok in a.split()[2:]:
            if tok.split(':')[0] not in names: print('WARN anim frame missing:', a.split()[0], tok)
    with open(path, 'w', encoding='utf-8') as f:
        for a in ANIMS: f.write('!' + a + '\n')
        for name, g, hp in FRAMES:
            f.write('@%s head=%d,%d\n' % (name, hp[0], hp[1]))
            for r in g: f.write(''.join(r) + '\n')
            f.write('\n')

os.makedirs(BUILD, exist_ok=True)
emit(os.path.join(BUILD, 'frames.txt'))
print(len(FRAMES), 'frames,', len(ANIMS), 'anims')

if args.preview:
    rx = re.compile(args.preview)
    items = [(n, g) for n, g, hp in FRAMES if rx.search(n)]
    marks = None
    if args.marks:
        marks = dict((n, [(hp[0], hp[1], '#00ffff'), (W // 2, H - 1, '#ff0000')]) for n, g, hp in FRAMES)
    render(items, os.path.join(BUILD, 'preview.png'), args.scale, args.palette, 6, args.bg, marks)

if args.showcase:
    grids = dict((n, g) for n, g, hp in FRAMES)
    showcase([[grids[n] for n in row] for row in SHOWCASE], os.path.join(DOCS, 'preview.png'))
