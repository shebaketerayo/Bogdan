HELD_BODY = """
....................bbbbbbbb|15
...................bbbbbbbbbb
..................bbbbbbbbbbbb
.................bbsbbbbbbbbbbb
.................bsbbbbbbbbblll
................bbsbbbbbbbbbllll
................bsbbqqqbbbbbllll
................bbbqqqqqbbbbllll
................bsbbqqqbbbbbllll
................bbsbbbbbbbbbllll
...............bbsbbbbbbbbbbllll
...............bsbbbbbbbbbbbblll
...............bbbbbbbbbbbbbbbll
...............bbbbbbbbbbbbbbbbb
................bbbbbbbbbbbbbbb
.................bbbbbbbbbbbbb|30
"""
HELD_HIND = {
    0: """
..................bbb..ddd|29
..................bbb..ddd
..................sss..ddd
..................bbb..ddd
..................bbb..ddd
..................sss..ddd
..................bbb..fff
..................fff..fff
..................fff|37
..................fff|38
""",
    1: """
...................bbb..ddd|29
...................bbb..ddd
...................sss..ddd
...................bbb...ddd
...................bbb...ddd
....................sss..ddd
....................bbb..fff
....................fff..fff
....................fff|37
....................fff|38
""",
}
HELD_TAIL = {
    0: """
...............bb|27
..............bbs
.............bsb
.............sbb
............bbs
............bsb
............bbx
............xxx
.............xx|35
""",
    1: """
...............bb|27
...............bsb
...............sbb
..............bbs
..............bsb
.............bbs
.............bxx
.............xxx
..............xx|35
""",
}
HELD_FRONT_FAR = {
    0: """
........................ddd|18
........................ddd
........................ddd
........................ddd
........................ddd
........................fff
........................fff|24
""",
    1: """
.........................ddd|18
.........................ddd
.........................ddd
.........................ddd
.........................ddd
.........................fff
.........................fff|24
""",
}
HELD_FRONT = {
    0: """
...........................bbb|18
...........................bbb
...........................sss
...........................bbb
...........................bbb
...........................sss
...........................bbb
...........................fff
...........................fff|26
""",
    1: """
............................bbb|18
............................bbb
............................sss
............................bbb
............................bbb
............................sss
............................bbb
............................fff
............................fff|26
""",
}
HELD_HEAD = (15, 2)     # held by the scruff: the brain keeps the cursor just above the head
def held(name, k):
    g = canvas()
    lit(g, HELD_TAIL[k])
    lit(g, HELD_BODY)
    lit(g, HELD_HIND[k])
    outline(g)
    part(g, HELD_FRONT_FAR[k])
    part(g, HELD_FRONT[k])
    stamp(g, head('open', 'o'), HELD_HEAD[0], HELD_HEAD[1])
    return add(name, g, (HELD_HEAD[0] + HEAD_CX, HELD_HEAD[1] + HEAD_CY))
held('held0', 0)
held('held1', 1)
anim('Held', True, ('held0', 0.35), ('held1', 0.35))


LOAF_BODY = """
..............bbbbbbbbbb|25
...........bbbbsbbbsbbbbbb
.........bbbbsbbbsbbbsbbbbbb
........bbbbbbbbbbbbbbbbbbbbbb
.......bbqqqbbbbbbbbbbbbbbbbbbb
......bbqqqqqbbbbbbbbbbbbbbbbbbb
......bsbqqqbbbbbbbbbbbbbbbbbbbbbl
.....bbsbbbbbbbbbbbbbbbbbbbbbpppllll
.....bsbbbbbbbbbbbbbbbbbbbbbbppllllll
.....bbsbbbbbbbbbbbbbbbbbbbbbbplllllll
.....bsbbbbbbbbbbbbbbbbbbbbbbbbllllll
.....bbbbbbbbbbbbbbbbbbbbbbbbbbbllll
......bbbbbbbbbbbbbbbbbbbbbbbbbbbbb
.......bbbbbbbbbbbbbbbbbbbbbbbbbbbb|38
"""
LOAF_PAWS = """
.................................fffff|37
.................................fffff|38
"""
LOAF_TAIL = """
......bb|33
.....bsbb
.....bbsb
......bsbb.................xx
.......bbsbbsbbsbbsbbsbbsbbxx
.........bsbbsbbsbbsbbsbbsb|38
"""
LOAF_HEAD = (23, 15)
def loaf(name, eyes='open', mouth='norm', up=0, raise_front=0, hdy=0):
    g = canvas()
    lit(g, LOAF_BODY)
    for k in range(up): breathe(g, 33, 5, 34)
    if raise_front: shear(g, 12, 28, 0, -raise_front, y1=36)
    outline(g)
    part(g, LOAF_TAIL)
    hx, hy = LOAF_HEAD[0], LOAF_HEAD[1] - up - raise_front + hdy
    stamp(g, head(eyes, mouth), hx, hy)
    part(g, LOAF_PAWS)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))

loaf('loaf0')
loaf('loaf1', up=1)
loaf('loafblink', 'shut')
loaf('lie1', raise_front=3)
sit('lie0', hdy=2, tail='b')
anim('LieDown', False, ('lie0', 0.18), ('lie1', 0.2), ('loaf0', 0.2))
anim('Loaf', True, ('loaf0', 1.2), ('loaf1', 1.0), ('loaf0', 1.4), ('loafblink', 0.15))


SLEEP_BODY = """
..............bbbbbbbbbb|18
...........bbbbsbbbbsbbbbb
.........bbbbsbbbbsbbbbsbbbb
........bbbbbsbbbbsbbbbsbbbbb
.......bbqqqbbbbbbbbbbbbbbbbbbb
......bbqqqqqbbbbbbbbbbbbbbbbbbb
......bsbqqqbbbbbbbbbbbbbbbbbbbbb
.....bbsbbbbbbbbbbbbbbbbbbbbbbbbb
.....bsbbboooobbbbbbbbbbbbbbbbbbbb
.....bbsobqqqbobbbbbbbbbbbbbbbbbbb
.....bsobqqqbbbobbbbbbbbbbbbbbbbbb
.....bbobbbbbbbobbbbbbbbbbbbbbbbbb
.....bbobbbbbbbobbbbbbbbbbbbbbbbbb
......bbobbbbbffffbbbbbbbbbbbbbbbb
.......bbooooofffffbbbbbbbbbbbbbb
......bbbbbbbbbbbbbbbbbbbbbbbbbbbb
......bbbbbbbbbbbbbbbbbbbbbbbbbbbb
.......bbbbbbbbbbbbbbbbbbbbbbbbbbb
.......bbbbbbbbbbbbbbbbbbbbbbbbbbb
........bbbbbbbbbbbbbbbbbbbbbbbbbb
.........bbbbbbbbbbbbbbbbbbbbbbbbb|38
"""
SLEEP_TAIL = {
    'a': """
.....bb|31
.....bsb
.....bbs
......bsb...........................xx
......bbsb.........................xxs
.......bsbb.......................bbsb
........bbsbbsbbsbbsbbsbbsbbsbbsbbsb
..........bsbbsbbsbbsbbsbbsbbsbbsb|38
""",
    'b': """
.....bb|31
.....bsb
.....bbs
......bsb............................xx
......bbsb..........................xxs
.......bsbb........................bbsb
........bbsbbsbbsbbsbbsbbsbbsbbsbbsbb
..........bsbbsbbsbbsbbsbbsbbsbbsbb|38
""",
}
SLEEP_HEAD = (18, 21)
def sleep(name, eyes='shut', up=0, tail='a', hdy=0, mouth='norm'):
    g = canvas()
    lit(g, SLEEP_BODY)
    for k in range(up): breathe(g, 30, 5, 36)
    outline(g)
    hx, hy = SLEEP_HEAD[0], SLEEP_HEAD[1] + hdy
    stamp(g, head(eyes, mouth), hx, hy)
    part(g, SLEEP_TAIL[tail])
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))

sleep('sleep0')
sleep('sleep1', up=1)
sleep('sleep2', tail='b')
sleep('wake0', 'shut', hdy=-3)
anim('Sleep', True, ('sleep0', 0.9), ('sleep1', 0.9), ('sleep0', 0.9), ('sleep1', 0.9), ('sleep2', 0.5))
anim('WakeUp', False, ('wake0', 0.3), ('lie1', 0.2), ('lie0', 0.2))


STRETCH = {
    1: """
...xxx|9
..xxxx
..sss
..bbb
...bbb
...sss
...bbbb
....bbbbbbb|16
....bbbsbbbsbb
....bqqqbbbsbbbbb
....bqqqqbbbsbbbbbb
....bbqqqbbbbsbbbbbbb
....bbbbbbbbbbbsbbbbbbbb
....bbobbbbbbbbbbbbbbbbbbb
....bbbobbbbbbbbbbbbbbbbbbbb
....bbbbobbbbbbbbbbbbbbbbbbbbb
....bbbbbobbbbbbbbbbbbbbbbbbbbbl
....bbbbbobbbbbbbbbbbbbbbbbbbbbll
.....bbbbobbbbbbbbbbbbbbbbbbbbblll
......bbbobdddddddbbbbbbbbbbbbllll
.......bbbodd....dddddbbbbbbbbllll|29
.......bbbodd........bbbbbbbbbbbbb
.......sssodd........bbbbbbbbbbbbbb
.......bbbodd.........bbbbbbbbbbbbbbbbbbbb
.......bbbodd..........sssbbbbsssbbbbbbbffff
.......sssodd............bbbbbbbbbbbbbbbffff
.......bbbodd.............bbbbbbbbbbbbbbffff|35
.......bbbofff
.......ffffffff
.......fffffff|38
""",
    0: """
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
......bbbobdddddddbbbbbbbbbbbllll
.......bbbodd....ddddbbbbbbbbbllll|30
.......bbbodd........bbbbbbbbbbbb
.......sssodd.........bbbbbbbbbbbbb
.......bbbodd..........sssbbbbbbbbbbbb
.......bbbodd...........bbbbbsssbbbbbbbb
.......sssodd............bbbbbbbbbbbbbffff
.......bbbofff............bbbbbbbbbbbbffff
.......ffffffff.............bbbbbbbbbbffff
.......fffffff|38
""",
}
def stretch(name, k, eyes, mouth, hx, hy):
    g = canvas()
    lit(g, STRETCH[k])
    outline(g)
    stamp(g, head(eyes, mouth), hx, hy)
    return add(name, g, (hx + HEAD_CX, hy + HEAD_CY))

stretch('stretch0', 0, 'shut', 'norm', 24, 15)
stretch('stretch1', 1, 'tight', 'o', 25, 17)
stretch('stretch2', 0, 'open', 'norm', 24, 15)
anim('Stretch', False, ('stretch0', 0.35), ('stretch1', 1.1), ('stretch2', 0.35))
