from PIL import Image, ImageDraw

PALS = {
 'bogdan': dict(o='#3f3533', b='#a99d95', d='#8e827b', s='#7d706a', l='#cfc4ba', f='#cdc2b9', x='#9b8f88',
                m='#ece5dc', p='#a99d95', q='#a99d95', e='#c98533', n='#cf9f98', c='#7d706a'),
 'tabby': dict(o='#3d2516', b='#f2a452', d='#d9853a', s='#c4682a', l='#fff1dc', f='#fff1dc', x='#c4682a',
               m='#fff1dc', p='#f2a452', q='#f2a452', e='#8fc24a', n='#f59aa5', c='#c4682a'),
 'black': dict(o='#08080c', b='#3b3b46', d='#2d2d36', s='#3b3b46', l='#464652', f='#464652', x='#3b3b46',
               m='#4a4a57', p='#3b3b46', q='#3b3b46', e='#f0d23c', n='#d98c9a', c='#464652'),
 'calico': dict(o='#3a3030', b='#fbf7f0', d='#e3dccf', s='#fbf7f0', l='#ffffff', f='#ffffff', x='#3e3838',
                m='#ffffff', p='#eb9a4a', q='#4a4242', e='#9cc54a', n='#f59aa5', c='#ffffff'),
 'siam': dict(o='#3b2a20', b='#f3e6cf', d='#dcc7a4', s='#f3e6cf', l='#fbf4e6', f='#5b4032', x='#5b4032',
              m='#6a4c3b', p='#f3e6cf', q='#f3e6cf', e='#5fa8e8', n='#e88c98', c='#fbf4e6'),
 'tux': dict(o='#08080c', b='#363640', d='#292931', s='#363640', l='#f7f7f7', f='#f7f7f7', x='#363640',
             m='#f7f7f7', p='#363640', q='#363640', e='#a8d050', n='#f2a0ac', c='#f7f7f7'),
 'white': dict(o='#4c4a5a', b='#fbfbfb', d='#e2e2ea', s='#fbfbfb', l='#ffffff', f='#ffffff', x='#f3dfe2',
               m='#ffffff', p='#fbfbfb', q='#fbfbfb', e='#69b2f2', n='#f5a3b0', c='#ffffff'),
 'debug': dict(o='#202020', b='#c0c0c0', d='#909090', s='#4060ff', l='#ffffff', f='#ffe060', x='#ff40ff',
               m='#80ffff', p='#ff8000', q='#008000', e='#40c040', n='#ff8090', c='#00c0c0'),
}
FIXED = dict(k='#141414', h='#ffffff')
FILL = set('bdslfxmpqenkhc')
BACKGROUNDS = {'grey': ('#9a9a9a', '#8a8a8a'), 'dark': ('#1d2230', '#232838'), 'light': ('#f4f4f0', '#e8e8e4'),
               'blue': ('#3c6eaa', '#3c6eaa')}


def color(pal, c):
    return FIXED.get(c) or pal.get(c) or '#ff00ff'


def render(items, path, scale=6, pal='bogdan', cols=6, bg='grey', marks=None):
    pal = PALS[pal]
    cw = max(len(g[0]) for n, g in items) * scale + 8
    ch = max(len(g) for n, g in items) * scale + 22
    cols = min(cols, len(items))
    rows = (len(items) + cols - 1) // cols
    c1, c2 = BACKGROUNDS[bg]
    img = Image.new('RGB', (cols * cw, rows * ch), '#505050')
    dr = ImageDraw.Draw(img)
    for i, (name, g) in enumerate(items):
        ox, oy = (i % cols) * cw + 4, (i // cols) * ch + 18
        dr.text((ox, oy - 15), name, fill='#ffffff')
        for y, row in enumerate(g):
            for x, c in enumerate(row):
                col = (c1 if (x + y) % 2 == 0 else c2) if c == '.' else color(pal, c)
                dr.rectangle([ox + x * scale, oy + y * scale, ox + (x + 1) * scale - 1, oy + (y + 1) * scale - 1], fill=col)
        for mx, my, mc in (marks or {}).get(name, ()):
            px_, py_ = ox + mx * scale + scale // 2, oy + my * scale + scale // 2
            dr.ellipse([px_ - 3, py_ - 3, px_ + 3, py_ + 3], outline=mc)
    img.save(path)


def bbox(g):
    xs = [x for r in g for x, c in enumerate(r) if c != '.']
    ys = [y for y, r in enumerate(g) if any(c != '.' for c in r)]
    return min(xs), max(xs) + 1, min(ys), max(ys) + 1


def showcase(rows, path, scale=5, pal='bogdan', bg='#f3f1ec', ground='#e2ddd4', gap=10, margin=10):
    pal = PALS[pal]
    boxes = [[bbox(g) for g in row] for row in rows]
    cols = [max(bb[j][1] - bb[j][0] for bb in boxes if j < len(bb)) for j in range(max(len(r) for r in rows))]
    heights = [max(b[3] for b in bb) - min(b[2] for b in bb) for bb in boxes]
    iw = sum(cols) + gap * (len(cols) - 1) + 2 * margin
    ih = sum(heights) + gap * (len(rows) - 1) + 2 * margin
    img = Image.new('RGB', (iw * scale, ih * scale), bg)
    dr = ImageDraw.Draw(img)

    def pixel(x, y, col):
        dr.rectangle([x * scale, y * scale, (x + 1) * scale - 1, (y + 1) * scale - 1], fill=col)

    oy = margin
    for row, bb, h in zip(rows, boxes, heights):
        top = min(b[2] for b in bb)
        cx = margin
        for g, (x0, x1, y0, y1), cw in zip(row, bb, cols):
            ox = cx + (cw - (x1 - x0)) // 2 - x0
            for x in range(cx - 1, cx + cw + 1):
                pixel(x, oy + h - 1, ground)
            for y in range(y0, y1):
                for x in range(x0, x1):
                    if g[y][x] != '.': pixel(ox + x, oy + y - top, color(pal, g[y][x]))
            cx += cw + gap
        oy += h + gap
    img.save(path)
