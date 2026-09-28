SIT_BODY = """
....................bbbbbb|20
..................bbbbbbbbbb
................bbsbbbbbbbbbb
...............bbsbbsbbbbbbbbb
..............bbsbbsbbbbbbbbbll
.............bbsbbsbbbbbbpppllll
.............bsbbsbbbbbbpppllllll
............bbsbbsbbbbbbppplcccccl
............bsbbbbbbbbbbbpppllllll
...........bbsbbbbbooobbbbbbllllll
...........bsbbbbboqqqobbbbbblcccl
...........bbbbbboqqqqqobbbbbllll
...........bsbbbobqqqbbbodddd lll
...........bsbbbobbbbbbbodddosss
...........bbbbbobbbbbbbodddobbb
...........bsbbbobbbbbbbodddosss
...........bbbbbbobbbbbbodddobbb
............bbbbbbofffffofffoffff
.............bbbbbofffffofffoffff|38
"""
SIT_NO_LEG = """
...........bsbbbobqqqbbbodddblll|32
...........bsbbbobbbbbbbodddbll_
...........bbbbbobbbbbbbodddbb__
...........bsbbbobbbbbbbodddb___
...........bbbbbbobbbbbbodddo___
............bbbbbbofffffofff_____
.............bbbbbofffffofff_____|38
"""
SIT_TAIL = {
    'a': """
.xx|33
.xxx
.sbbb
..bbsbbsbbsb
..bbsbbsbbsbb
...bsbbsbbsbbb|38
""",
    'b': """
.xxx|35
.xxxsbbsbbsb
..xbbsbbsbbsbb
...bbsbbsbbsbbb|38
""",
    'c': """
..xx|31
.xxx|32
.xsb
..bb
..sbbb
..bbsbbsbbsb
..bbsbbsbbsbb
...bsbbsbbsbbb|38
""",
}
SIT_HEAD = (17, 8)

def sit(name, eyes='open', mouth='norm', tail='a', up=0, noleg=False, paw=None, hdy=0, eyes_r=None):
    g = canvas()
    lit(g, SIT_TAIL[tail])
    lit(g, SIT_BODY)
    if noleg: lit(g, SIT_NO_LEG)
    hx, hy = SIT_HEAD[0], SIT_HEAD[1] + hdy
    if up:
        for k in range(up): breathe(g, 30, 11, 35)
    outline(g)
    stamp(g, head(eyes, mouth, eyes_r), hx, hy - up)
    if paw:
        if isinstance(paw, tuple): part(g, paw[0], open_from=paw[1])
        else: part(g, paw)
    return add(name, g, (hx + HEAD_CX, hy - up + HEAD_CY))

sit('sit0', tail='a')
sit('sit1', tail='a', up=1)
sit('sit2', tail='c', up=1)
sit('sit3', tail='c')
sit('blink0', 'half', tail='a')
sit('blink1', 'shut', tail='a')
sit('lookup0', 'up', tail='a', up=1)
sit('lookup1', 'up', tail='c', up=1)
sit('happy0', 'happy', 'smile', tail='b')
sit('happy1', 'happy', 'smile', tail='c', up=1)
sit('meow0', 'open', 'meow', tail='a')
sit('meow1', 'happy', 'meow2', tail='c', up=1)
sit('yawn0', 'shut', 'o', tail='a')
sit('yawn1', 'tight', 'yawn', tail='c', up=1, eyes_r='tight')
sit('yawn2', 'shut', 'norm', tail='a')

SWAT_UP = """
..................................ffff|25
.................................bffff
...............................bbbbff
..............................bbbb
.............................bbbb
.............................bbb|30
"""
SWAT_OUT = """
.......................................ff|27
..............................bbbbbbbbffff
.............................bbbbbbbbbffff
.............................bbbbbbb.ff|30
"""
sit('swat0', 'open', 'norm', tail='a', noleg=True, paw=(SWAT_UP, 29))
sit('swat1', 'open', 'o', tail='c', noleg=True, paw=(SWAT_OUT, 29))
sit('swat2', 'open', 'norm', tail='c', noleg=True, paw=(SWAT_UP, 29))

GROOM_LOW = """
.............................ffff|22
............................fffff
.............................bbbb
..............................bbb
..............................bbb
.............................bbb
.............................bbb|28
"""
GROOM_FACE = """
..........................ffff|17
.........................fffff
..........................bbbbb
............................bbbb
.............................bbb
..............................bbb
..............................bbb
..............................bbb
.............................bbb
.............................bbb|26
"""
sit('groom0', 'shut', 'norm', tail='a', noleg=True, paw=(GROOM_LOW, 26))
sit('groom1', 'shut', 'lick', tail='a', noleg=True, paw=(GROOM_LOW, 26))
sit('groom2', 'shut', 'norm', tail='c', noleg=True, paw=(GROOM_FACE, 25), hdy=1)

anim('Sit', True, ('sit0', 0.6), ('sit1', 0.5), ('sit2', 0.6), ('sit3', 0.5))
anim('SitBlink', False, ('blink0', 0.06), ('blink1', 0.12), ('blink0', 0.06))
anim('SitLookUp', True, ('lookup0', 0.7), ('lookup1', 0.7))
anim('Happy', True, ('happy0', 0.35), ('happy1', 0.35))
anim('Meow', False, ('meow0', 0.15), ('meow1', 0.55), ('meow0', 0.12))
anim('Yawn', False, ('yawn0', 0.25), ('yawn1', 0.9), ('yawn2', 0.3))
anim('Swat', False, ('swat0', 0.14), ('swat1', 0.12), ('swat2', 0.14))
anim('Groom', False, ('groom0', 0.25), ('groom1', 0.3), ('groom0', 0.2), ('groom1', 0.3), ('groom2', 0.45))


STAND_BODY = """
..........bbbsbbbsbbbbbb|20
........bbbbsbbbsbbbsbbbbbbb
.......bbbbbsbbbsbbbsbbbbbbbb
......bbbbbbbbbbbbbbbbbbbbbbbb
.....bbqqqbbbbbbbbbbbbbbbbbbbbb
.....bqqoooqbbbbbbbbbbbbbbbbbbbl
.....bqobbbobbbsbbbbbbbbbbbbbbll
.....bobbbbbobbbbsbbbbbbbpppblll
.....bobbbbbbobbbbsbbbbbbpppplll
.....bobbbbbbobbbbbbbbbbbbpppbll
......bbbbbbbobbbbbbbbbbbbbbbbll
.......bbbbbbodddddddddbbbbbbl|31
"""
TAIL = {
    'up_a': """
...xxx|8
..xxxx
..sss
..bbb
..bbb
..sss
..bbb
...bbb
...sss
...bbb
....bbb
....sss
.....bbbb
.....bbbb|21
""",
    'up_b': """
....xxx|8
...xxxx
...sss
..bbb
..bbb
..sss
..bbb
..bbb
...sss
...bbb
....bbb
....sss
.....bbbb
.....bbbb|21
""",
    'run_a': """
.xx|18
.xxxsbb
...bbsbbsb
....bbsbbsbb|21
.........bbb
""",
    'run_b': """
.xxxsb|19
.xxbbsbbsb
....bbsbbsbb|21
.........bbb
""",
    'low': """
.......bbbb|21
.....sbbsb
....sbbsb
..xxbbs
.xxxb
.xx|26
""",
}
STAND_HEAD = (23, 10)
HIP = dict(NH=7, FH=10, FF=21, NF=24)

def four(nh, fh, ff, nf):
    return [(HIP['FH'], fh[0], fh[1], False), (HIP['FF'], ff[0], ff[1], False),
            (HIP['NH'], nh[0], nh[1], True), (HIP['NF'], nf[0], nf[1], True)]

def stand_like(name, tail, legspec, eyes='open', mouth='norm', bdy=0, hdy=0, top=31, pre=None, eyes_r=None):
    g = canvas()
    lit(g, TAIL[tail], 0, bdy)
    lit(g, STAND_BODY, 0, bdy)
    if pre: pre(g)
    legs(g, legspec, top + bdy)
    outline(g)
    hx, hy = STAND_HEAD[0], STAND_HEAD[1] + bdy + hdy
    stamp(g, head(eyes, mouth, eyes_r), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))

Z = (0, 0)
stand_like('stand0', 'up_a', four(Z, Z, Z, Z))
stand_like('stand1', 'up_b', four(Z, Z, Z, Z))

# a lateral-sequence walk, (dx from hip, lift) per phase: a planted paw moves back 2 px per 0.09 s frame,
# i.e. 22 px/s = BrainTune.WalkSpeed, so the paws stay glued to the ground while the body walks
WALK_K = [(3, 0), (1, 0), (-1, 0), (-3, 0), (-2, 1), (0, 2), (2, 2), (3, 1)]
WALK_OFF = (('NH', 0), ('NF', 2), ('FH', 4), ('FF', 6))
WALK_HDY = [0, 0, 1, 0, 0, 0, 1, 0]
for f in range(8):
    k = dict((leg, WALK_K[(f - off) % 8]) for leg, off in WALK_OFF)
    stand_like('walk%d' % f, 'up_a' if f < 4 else 'up_b', four(k['NH'], k['FH'], k['FF'], k['NF']), hdy=WALK_HDY[f])

RUN = [  # near hind, far hind, far front, near front, body dy, head dy, tail, (rear, front) rock
    ((-4, 1), (-3, 0), (3, 0), (4, 1), 0, 0, 'run_a', (1, -1)),
    ((-2, 2), (-3, 1), (1, 0), (2, 0), 0, 0, 'run_a', (0, 1)),
    ((1, 1), (0, 2), (-2, 0), (-1, 0), 1, 0, 'run_b', (-1, 1)),
    ((3, 0), (2, 0), (-3, 1), (-2, 2), 1, 0, 'run_b', (-1, 0)),
    ((2, 0), (3, 0), (-1, 2), (1, 2), 0, 0, 'run_a', (0, -1)),
    ((0, 0), (1, 1), (2, 2), (3, 2), 0, -1, 'run_a', (1, -1)),
]
def rock(r, f):
    def fn(g):
        shear(g, 8, 28, r, f, y1=33)
    return fn
for f, (nh, fh, ff, nf, bdy, hdy, tl, rk) in enumerate(RUN):
    spec = four(nh, fh, ff, nf)
    stand_like('run%d' % f, tl, [(s_[0], s_[1], max(0, s_[2] - bdy), s_[3]) for s_ in spec], bdy=bdy, hdy=hdy + rk[1],
               pre=rock(rk[0], rk[1]), top=30)

anim('Stand', True, ('stand0', 0.8), ('stand1', 0.8))
anim('Walk', True, *[('walk%d' % f, 0.09) for f in range(8)])
anim('Run', True, *[('run%d' % f, 0.07) for f in range(6)])


HUNT_BODY = """
..........bbbbbbb|21
........bbbbsbbbsbb
.......bbbbbsbbbsbbbb
......bbqqqbbbbbbsbbbbb
......bqqoooqbbbbbsbbbbbb
.....bbqobbbobbbbbbbsbbbbbb
.....bbobbbbbobbbbbbbbbbbbbbb
.....bbobbbbbbobbbbbbbbbbbbbbbl
.....bbobbbbbbobbbbbbbbbbbbppll
.....bbobbbbbbobbbbbbbbbbbbppll
......bbbbbbbbobbbbbbbbbbbbbbll
.......bbbbbbobdddddddbbbbbbbl|32
"""
HUNT_TAIL = {
    'a': """
......bbbb|22
...bsbbsbb
.xxbbsb
.xxx|25
""",
    'b': """
.xx|18
.xxs
..bbb
...sbb
....bbsbbb
......bbbb|23
""",
    'c': """
......bbbb|22
.xxsbbsbbb
.xxbbsbbs
..xx|25
""",
}
def hunt(name, tail='a', eyes='open', mouth='norm', rump=0, rdx=0, hdy=0, bdy=0):
    g = canvas()
    lit(g, HUNT_TAIL[tail], 1, bdy)
    lit(g, HUNT_BODY, 0, bdy)
    if rump: shear(g, 12, 19, -rump, 0, y1=34)
    legs(g, [(HIP['FH'] + rdx, 2, 0, False, 31 + bdy - rump, GROUND), (19, 1, 0, False, 32 + bdy, GROUND),
             (HIP['NH'] + rdx, 2, 0, True, 31 + bdy - rump, GROUND), (23, 1, 0, True, 32 + bdy, GROUND)], 32 + bdy)
    outline(g)
    hx, hy = STAND_HEAD[0], 15 + bdy + hdy
    stamp(g, head(eyes, mouth), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))

stand_like('crouch0', 'up_b', four((1, 0), (1, 0), (1, 0), (1, 0)), bdy=2, hdy=1)
hunt('crouch1', 'a')
hunt('wiggle0', 'b', rump=2)
hunt('wiggle1', 'c', rump=1, rdx=1)
hunt('land0', 'b', 'shut', bdy=1)
stand_like('land1', 'up_a', four((-1, 0), (-1, 0), (1, 0), (1, 0)), bdy=3, hdy=0)

anim('Crouch', False, ('crouch0', 0.08), ('crouch1', 0.14))
anim('Wiggle', True, ('wiggle0', 0.12), ('wiggle1', 0.12))
anim('Land', False, ('land0', 0.1), ('land1', 0.12))


def jump_up(g):
    shear(g, 6, 30, 4, -4, y1=34)
stand_like('jumpup', 'run_a', [(HIP['FH'] + 1, -5, 0, False, 33, 38), (HIP['NH'], -6, 0, True, 33, 38),
                               (HIP['FF'] + 1, 3, 0, False, 26, 30), (HIP['NF'] + 1, 4, 0, True, 26, 31)],
           hdy=-4, pre=jump_up, top=31)

def jump_fall(g):
    shear(g, 6, 30, -2, 2, y1=34)
stand_like('jumpfall', 'up_b', [(HIP['FH'], -2, 0, False, 29, 33), (HIP['NH'], -3, 0, True, 29, 34),
                                (HIP['FF'], 1, 0, False, 33, 38), (HIP['NF'], 2, 0, True, 33, 38)],
           hdy=2, pre=jump_fall, top=31)

anim('JumpUp', True, ('jumpup', 0.2))
anim('JumpFall', True, ('jumpfall', 0.2))

def fall(name, k):
    g = canvas()
    lit(g, TAIL['up_a' if k == 0 else 'up_b'], 0, -3)
    lit(g, STAND_BODY, 0, -3)
    if k == 0:
        spec = [(HIP['FH'], -3, 0, False, 28, 33), (HIP['FF'], 3, 0, False, 28, 33),
                (HIP['NH'] - 1, -4, 0, True, 28, 35), (HIP['NF'] + 1, 4, 0, True, 28, 35)]
    else:
        spec = [(HIP['FH'], -2, 0, False, 28, 34), (HIP['FF'], 2, 0, False, 28, 34),
                (HIP['NH'] - 1, -5, 0, True, 28, 33), (HIP['NF'] + 1, 5, 0, True, 28, 33)]
    legs(g, spec, 28)
    outline(g)
    hx, hy = STAND_HEAD[0], STAND_HEAD[1] - 4
    stamp(g, head('open', 'o'), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))
fall('fall0', 0)
fall('fall1', 1)
anim('Fall', True, ('fall0', 0.12), ('fall1', 0.12))
