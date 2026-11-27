"""Emit the reviewed KDL subset; author demo cues in media ms or exact BarTime."""

from random import Random

STRESS_COUNTS = (100, 500, 1500)
STRESS_FROM = 4000
STRESS_UNTIL = 16000
REPLACE = 'backdrop mode="replace" color="#10131A"\n'

TITLE = '''
// A1: media-clock entrance, held position, then exit by opacity only.
sprite "main-title" asset="title" clock="media-ms" order=60 {
    active from=160 until=3540
    base "pivot" 0 0
    base "position.y" 142
    track "position.x" {
        key at=160 value=-555 ease="sine-out"
        key at=1000 value=85
    }
    track "opacity" {
        key at=160 value=0 ease="sine-out"
        key at=700 value=1
        key at=2540 value=1 ease="linear"
        key at=3540 value=0
    }
}
'''

DEMO_ASSETS = '''asset "surround" file="assets/surround.png"
asset "phase" file="assets/phase.png"
asset "scope" file="assets/scope.png"
asset "gate" file="assets/gate.png"
asset "beam" file="assets/beam.png"
asset "atlas" file="assets/atlas.png"
asset "mote" file="assets/mote.png"
region "cyan-token" asset="atlas" x=0 y=0 width=96 height=96
region "amber-token" asset="atlas" x=96 y=0 width=96 height=96
region "violet-token" asset="atlas" x=192 y=0 width=96 height=96
'''

DEMO = '''
// The transparent surround stays out of the central note field and bottom HUD.
sprite "surround-plate" asset="surround" clock="media-ms" order=-50 {
    active from=0 until=20400
    base "position.x" 960
    base "position.y" 540
    track "opacity" { key at=0 value=0 ease="sine-out"; key at=570 value=1; key at=19300 value=1; key at=20400 value=0; }
}
sprite "scope-plate" asset="scope" clock="media-ms" order=-30 {
    active from=1200 until=20400
    base "position.x" 1629
    base "position.y" 271
    track "opacity" { key at=1200 value=0 ease="sine-out"; key at=2190 value=0.91; key at=18850 value=0.91; key at=20400 value=0; }
}

// A3: same adjacent regions on two samplers. Left flies along a cubic and holds frame 2.
// The moving loop on the right flips at 5.2 s but never changes its frame clock.
sprite "flight" clock="media-ms" order=10 {
    active from=3300 until=8290
    base "sampling" "linear"
    base "rotation" -12
    base "scale.x" 1.24
    base "scale.y" 1.24
    sequence mode="once-hold" {
        frame "cyan-token" duration-ms=650
        frame "amber-token" duration-ms=650
        frame "violet-token" duration-ms=650
    }
    path {
        key at=3300 x=-65 y=370 c1-x=90 c1-y=495 c2-x=75 c2-y=166 ease="sine-out"
        key at=5550 x=337 y=211 c1-x=545 c1-y=106 c2-x=489 c2-y=372 ease="sine-in-out"
        key at=7980 x=563 y=179
    }
    track "opacity" { key at=3300 value=0; key at=3780 value=0.95; key at=7190 value=0.95; key at=8290 value=0; }
}
sprite "scope-token" clock="media-ms" order=0 {
    active from=2730 until=18220
    base "position.x" 1629
    base "position.y" 257
    base "sampling" "nearest"
    sequence mode="loop" {
        frame "cyan-token" duration-ms=320
        frame "amber-token" duration-ms=320
        frame "violet-token" duration-ms=320
    }
    track "flip-x" { key at=5200 value=#true; key at=7770 value=#false; }
    track "opacity" { key at=2730 value=0; key at=3110 value=0.87; key at=17410 value=0.87; key at=18220 value=0; }
}

// A3: the window and rectangular crop rotate together. An oversized asymmetric
// beam and a long gradient are clipped in local coordinates, not a world AABB.
group "angled-window" clock="media-ms" order=5 {
    base "position.x" 1460
    base "position.y" 315
    track "rotation" {
        key at=4200 value=-14 ease="sine-in-out"
        key at=8410 value=10 ease="sine-in-out"
        key at=13650 value=-13 ease="sine-in-out"
        key at=17800 value=9
    }
    group "window-mask" order=0 {
        clip-rect x=4 y=4 width=320 height=61
        shape "window-spectrum" kind="rect" width=406 height=86 order=0 {
            active from=4200 until=18000
            base "pivot" 0 0
            base "position.x" -30
            base "position.y" -8
            linear-gradient from-x=0 from-y=0 to-x=1 to-y=0.41 {
                stop at=0 color="#193539E0"
                stop at=0.37 color="#367C69B8"
                stop at=0.68 color="#A857506C"
                stop at=1 color="#271D3600"
            }
            track "opacity" { key at=4200 value=0; key at=4610 value=0.95; key at=17400 value=0.95; key at=18000 value=0; }
        }
        sprite "sweeping-slit" asset="beam" order=1 {
            active from=4200 until=18000
            base "pivot" 0 0
            base "blend" "add"
            base "tint" "#9DFFEE"
            base "scale.y" 1.28
            base "rotation" -12
            path {
                key at=4200 x=-255 y=36 c1-x=-132 c1-y=26 c2-x=109 c2-y=-14 ease="quad-out"
                key at=5840 x=244 y=-24 c1-x=324 c1-y=-32 c2-x=247 c2-y=39 ease="sine-in-out"
                key at=7630 x=-161 y=4 c1-x=-33 c1-y=-23 c2-x=156 c2-y=46 ease="sine-in-out"
                key at=9300 x=252 y=-31 c1-x=310 c1-y=-35 c2-x=229 c2-y=31 ease="sine-in-out"
                key at=12440 x=-178 y=13 c1-x=-55 c1-y=38 c2-x=180 c2-y=-34 ease="sine-in-out"
                key at=15100 x=245 y=-31 c1-x=305 c1-y=-27 c2-x=206 c2-y=36 ease="sine-in-out"
                key at=18000 x=-255 y=39
            }
            track "opacity" { key at=4200 value=0; key at=4740 value=0.70; key at=17300 value=0.70; key at=18000 value=0; }
        }
    }
    sprite "window-outline" asset="gate" order=1 {
        active from=4200 until=18000
        base "pivot" 0 0
        track "opacity" { key at=4200 value=0; key at=4610 value=0.84; key at=17400 value=0.84; key at=18000 value=0; }
    }
}

// Tempo label stays in media time even though the next clip follows the chart tempo.
sprite "carrier-status" asset="phase" clock="media-ms" order=20 {
    active from=9100 until=17800
    base "pivot" 0 0
    base "position.y" 149
    track "position.x" { key at=9100 value=52 ease="quad-out"; key at=9620 value=113; }
    track "opacity" { key at=9100 value=0; key at=9500 value=1; key at=16750 value=1; key at=17800 value=0; }
}

// A2: first pulse crosses 120 -> 160 BPM at bar 4+1/4 (9100 ms).
// Duration 1 beat, period 2 beats. Six instances, with real gaps between them.
clip "carrier-pulses" clock="bar" at="4+1/8" duration="0+1/4" order=15 {
    repeat count=6 period="0+1/2"
    group "warm-bank" {
        base "position.x" 101
        base "position.y" 229
        track "rotation" { key at="0" value=-7 ease="sine-out"; key at="0+1/4" value=5; }
        sprite "primary-beam" asset="beam" order=1 {
            active from="0" until="0+1/4"
            base "pivot" 0 0.5
            base "blend" "add"
            base "tint" "#FFAE6E"
            track "scale.x" { key at="0" value=0.18 ease="quad-out"; key at="0+1/4" value=1.57; }
            track "opacity" { key at="0" value=0.06 ease="sine-out"; key at="0+1/8" value=0.66 ease="sine-in"; key at="0+1/4" value=0; }
        }
        sprite "echo-beam" asset="beam" order=0 {
            active from="0+1/16" until="0+1/4"
            base "position.x" 14
            base "position.y" 61
            base "pivot" 0 0.5
            base "blend" "add"
            base "tint" "#F56884"
            base "scale.y" 0.74
            track "scale.x" { key at="0+1/16" value=0.14 ease="sine-out"; key at="0+1/4" value=1.19; }
            track "opacity" { key at="0+1/16" value=0; key at="0+5/32" value=0.43; key at="0+1/4" value=0; }
        }
        sprite "pulse-mote" asset="mote" order=2 {
            active from="0" until="0+1/4"
            base "blend" "add"
            base "scale.x" 1.5
            base "scale.y" 1.5
            path { key at="0" x=21 y=53 c1-x=95 c1-y=-44 c2-x=257 c2-y=-3 ease="sine-out"; key at="0+1/4" x=349 y=21; }
            track "opacity" { key at="0" value=0.15; key at="0+1/8" value=0.77; key at="0+1/4" value=0; }
        }
    }
    shape "cool-bloom" kind="ellipse" width=230 height=190 order=0 {
        active from="0" until="0+1/4"
        base "position.x" 1629
        base "position.y" 257
        base "blend" "add"
        linear-gradient from-x=0 from-y=0 to-x=1 to-y=1 {
            stop at=0 color="#63DACB00"
            stop at=0.45 color="#63DACB50"
            stop at=1 color="#63DACB00"
        }
        track "scale.x" { key at="0" value=0.55; key at="0+1/4" value=1.21; }
        track "scale.y" { key at="0" value=0.55; key at="0+1/4" value=1.08; }
        track "opacity" { key at="0" value=0; key at="0+1/8" value=0.33; key at="0+1/4" value=0; }
    }
}

sprite "coda" asset="title" clock="media-ms" order=60 {
    active from=18475 until=20400
    base "pivot" 0 0
    base "position.x" 91
    base "scale.x" 0.65
    base "scale.y" 0.65
    track "position.y" { key at=18475 value=261 ease="sine-out"; key at=19050 value=223; }
    track "opacity" { key at=18475 value=0; key at=19150 value=0.84; key at=19750 value=0.84; key at=20400 value=0; }
}
'''


def title_script():
    return 'storyboard 0\nasset "title" file="assets/title.png"\n' + TITLE


def demo_script(replace):
    backdrop = REPLACE if replace else 'backdrop mode="keep"\n'
    return ('storyboard 0\n' + backdrop + 'asset "title" file="assets/title.png"\n'
            + DEMO_ASSETS + TITLE + DEMO)


def stress_script(count):
    # 7 KDL nodes per sprite, max fixture 10,503 nodes (the runtime limit is
    # 12,000). Distinct positions and angles, same texture and render state.
    # Exactly count visible objects. No demo decor/pulse can change this count.
    rng = Random(553108)
    out = ['storyboard 0\n', REPLACE, 'asset "mote" file="assets/mote.png"\n',
           f'// Exactly {count} independently animated draws in [{STRESS_FROM},{STRESS_UNTIL}).\n']
    for i in range(count):
        x, y = rng.randrange(48, 1873), rng.randrange(107, 976)
        start = rng.randrange(360)
        end = start + (720 if i % 2 else -720)
        out.append(f'''sprite "mote-{i:04}" asset="mote" clock="media-ms" {{
    active from={STRESS_FROM} until={STRESS_UNTIL}
    base "position.x" {x}
    base "position.y" {y}
    track "rotation" {{ key at={STRESS_FROM} value={start}; key at={STRESS_UNTIL} value={end}; }}
}}
''')
    return ''.join(out)
