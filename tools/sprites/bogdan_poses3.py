def limb(g, x0, y0, x1, y1, th, ch):
    n = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(n + 1):
        t = i / float(n)
        x = int(round(x0 + (x1 - x0) * t))
        y = int(round(y0 + (y1 - y0) * t))
        for k in range(th):
            yy = y + k
            if 0 <= x < W and 0 <= yy < H: g[yy][x] = ch


def front_paw(g, xr, claws):
    for x in range(xr - 3, xr + 1):
        if 0 <= x < W:
            g[GROUND - 1][x] = 'f'
            g[GROUND][x] = 'f'
    if claws and xr + 1 < W:
        g[GROUND][xr + 1] = 'h'     # the eye shine is the only white that no coat recolours


SCRATCH_REAR = """
...xxx|10
..xxxx
..sss
..bbb
..bbb
...sss
...bbbb
....bbbbbbbb|17
....bbbsbbbsbbb
....bqqqbbbsbbbbbb
....bqqqqbbbsbbbbbbbb
....bbqqqbbbbsbbbbbbbbb
....bbbbbbbbbbbsbbbbbbbbb
....bbobbbbbbbbbbbbbbbbbbbb
....bbbobbbbbbbbbbbbbbbbbbbbb
....bbbbobbbbbbbbbbbbbbbbbbbbbl
....bbbbbobbbbbbbbbbbbbbbbbbbbll
.....bbbbobbbbbbbbbbbbbbbbbbbblll
......bbbobdddddddbbbbbbbbbbblll
.......bbbodd....ddddbbbbbbbbbll
.......bbbodd........bbbbbbbbbb
.......sssodd.........bbbbbbbb
.......bbbodd
.......bbbodd
.......sssodd
.......bbbofff
.......ffffffff
.......fffffff|38
"""
SCRATCH_PAWS = [(42, 38), (40, 41), (38, 43), (40, 41)]


def scratch(name, k):
    g = canvas()
    lit(g, SCRATCH_REAR)
    near_x, far_x = SCRATCH_PAWS[k]
    limb(g, 22, 29, far_x - 2, GROUND - 2, 3, 'd')
    front_paw(g, far_x, far_x >= 42)
    limb(g, 25, 30, near_x - 2, GROUND - 2, 3, 'b')
    front_paw(g, near_x, near_x >= 42)
    outline(g)
    hx, hy = 24, 16
    stamp(g, head('tight' if k in (0, 2) else 'happy', 'norm', eyes_r='tight' if k in (0, 2) else None), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))


for k in range(4):
    scratch('scratch%d' % k, k)
anim('Scratch', True, *[('scratch%d' % k, 0.11) for k in range(4)])


stand_like('knead0', 'up_a', four(Z, Z, Z, (1, 2)), eyes='happy', mouth='smile', hdy=1)
stand_like('knead1', 'up_a', four(Z, Z, Z, Z), eyes='happy', mouth='smile', hdy=1)
stand_like('knead2', 'up_b', four(Z, Z, (1, 2), Z), eyes='happy', mouth='smile', hdy=1)
stand_like('knead3', 'up_b', four(Z, Z, Z, Z), eyes='happy', mouth='smile', hdy=1)
anim('Knead', True, ('knead0', 0.24), ('knead1', 0.2), ('knead2', 0.24), ('knead3', 0.2))


def sithalf(name):
    g = canvas()
    lit(g, TAIL['low'], 0, 2)
    lit(g, STAND_BODY)
    shear(g, 6, 27, 5, 0, y1=33)
    legs(g, [(HIP['FH'] + 1, 1, 0, False, 35, GROUND), (HIP['FF'], 0, 0, False),
             (HIP['NH'] + 2, 1, 0, True, 35, GROUND), (HIP['NF'], 0, 0, True)], 31)
    outline(g)
    hx, hy = 21, 9
    stamp(g, head(), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))


sithalf('sithalf')
anim('SitDown', False, ('crouch0', 0.07), ('sithalf', 0.13))
anim('StandUp', False, ('sithalf', 0.12), ('crouch0', 0.08))


sit('blinksoft', 'soft', tail='a')
anim('SlowBlink', False, ('blinksoft', 0.35), ('blink1', 0.9), ('blinksoft', 0.35))
