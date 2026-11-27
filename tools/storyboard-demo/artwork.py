"""Original vector-drawn art, including the lettering; no installed fonts or input assets."""

from math import cos, radians, sin
from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFilter

AMBER = '#FFA960'
CORAL = '#FB795F'
CYAN = '#73F6DD'
BONE = '#F5F2E7'
INK = '#10131A'

# Single-line technical lettering: hand-authored strokes in a 4×6 cell.
GLYPHS = {
    'A': ('062046', '1333'), 'B': ('06003041423343453606', '0333'),
    'C': ('401001051646',), 'D': ('06003041453606',), 'E': ('40000646', '0343'),
    'F': ('060040', '0343'), 'G': ('400006464323',), 'H': ('0006', '4046', '0343'),
    'I': ('0040', '2026', '0646'), 'J': ('40460102',), 'K': ('0006', '400346'),
    'L': ('000646',), 'M': ('0600234046',), 'N': ('06004640',),
    'O': ('103041453616050110',), 'P': ('0600404303',), 'Q': ('103041453616050110', '2446'),
    'R': ('06003041423203', '2346'), 'S': ('400003434606',),
    'T': ('0040', '2026'), 'U': ('000516364540',), 'V': ('002640',),
    'W': ('0006224640',), 'X': ('0046', '4006'), 'Y': ('002340', '2326'), 'Z': ('00400646',),
    '0': ('104041454606010010', '0640'), '1': ('012026', '0646'), '2': ('004043030646',),
    '3': ('00404606', '0343'), '4': ('000343', '4046'), '5': ('400003434606',),
    '6': ('400006464303',), '7': ('004016',), '8': ('0040460600', '0343'),
    '9': ('4640000343',), '/': ('0640',), '-': ('0343',), '.': ('2625',), ':': ('2221', '2524'),
}


def rgba(color, alpha):
    return tuple(int(color[i:i + 2], 16) for i in (1, 3, 5)) + (alpha,)


class Plate:
    """Logical-pixel vector surface; smaller plates are supersampled."""

    def __init__(self, width, height, color=(0, 0, 0, 0), ss=2):
        self.size = (width, height)
        self.ss = ss
        self.image = Image.new('RGBA', (width * ss, height * ss), color)
        self.draw = ImageDraw.Draw(self.image)

    def xy(self, point):
        return tuple(round(n * self.ss) for n in point)

    def polygon(self, points, fill):
        self.draw.polygon([self.xy(p) for p in points], fill)

    def line(self, points, fill, width=1):
        self.draw.line([self.xy(p) for p in points], fill, max(1, round(width * self.ss)), joint='curve')

    def rect(self, box, fill):
        self.draw.rectangle(self.xy(box), fill)

    def ellipse(self, box, fill, width=0):
        if width:
            self.draw.ellipse(self.xy(box), outline=fill, width=max(1, round(width * self.ss)))
        else:
            self.draw.ellipse(self.xy(box), fill)

    def arc(self, center, radius, start, end, color, width=1):
        x, y = center
        self.draw.arc(self.xy((x - radius, y - radius, x + radius, y + radius)),
                      start, end, color, max(1, round(width * self.ss)))

    def text(self, x, y, text, cell, color, width):
        for letter in text:
            if letter != ' ':
                for stroke in GLYPHS[letter]:
                    points = [(x + int(stroke[i]) * cell, y + int(stroke[i + 1]) * cell)
                              for i in range(0, len(stroke), 2)]
                    self.line(points, color, width)
            x += cell * (3 if letter == ' ' else 5.6)

    def finish(self):
        return self.image.resize(self.size, Image.Resampling.LANCZOS) if self.ss != 1 else self.image


def polar(x, y, r, angle):
    theta = radians(angle)
    return x + r * cos(theta), y + r * sin(theta)


def dial(plate, x, y, r, color, strong=False):
    for n, start, end in ((1, 13, 162), (1, 191, 321), (.91, 48, 277), (1.07, 214, 310)):
        plate.arc((x, y), n * r, start, end, rgba(color, 190 if strong else 82), 1.2)
    for i in range(0, 360, 6):
        if 124 < i < 155 or 306 < i < 338:
            continue
        plate.line([polar(x, y, r * (1.08 if i % 30 == 0 else 1.14), i), polar(x, y, r * 1.19, i)],
                   rgba(color, 230 if i % 30 == 0 else (95 if strong else 57)), 1.5 if i % 30 == 0 else 1)


def glass_glow(plate, items, radius):
    haze = Plate(*plate.size, ss=plate.ss)
    for box, color in items:
        haze.ellipse(box, color)
    plate.image.alpha_composite(haze.image.filter(ImageFilter.GaussianBlur(radius * plate.ss)))


def surround():
    p = Plate(1920, 1080, ss=1)
    # Transparent central and lower note field. The same plate is used over both backdrops.
    glass_glow(p, [((24, 53, 535, 418), rgba('#783A43', 132)),
                   ((1440, 24, 1920, 440), rgba('#176B64', 155))], 117)
    for x, direction, color in ((30, 1, AMBER), (1890, -1, CYAN)):
        def local(points):
            return [(x + direction * dx, y) for dx, y in points]

        p.line(local([(0, 125), (0, 293), (22, 315), (22, 428), (3, 447), (3, 820)]), rgba(color, 80), 2)
        p.line(local([(17, 134), (17, 285), (49, 317), (49, 373)]), rgba(color, 43), 1)
        p.line(local([(20, 681), (20, 792), (70, 842), (105, 842)]), rgba(color, 58), 1)
        p.line(local([(83, 166), (122, 127), (334, 127), (352, 145), (445, 145)]), rgba(color, 88), 1)
        p.line(local([(102, 170), (132, 140), (270, 140)]), rgba(color, 50), 1)
        for i in range(26):
            y = 455 + 12 * i
            p.line(local([(0, y), (8 if i % 5 == 0 else 4, y)]), rgba(color, 70 if i % 5 == 0 else 42))
        for i in range(12):
            p.line(local([(40 + i * 12, 112), (44 + i * 12, 119)]), rgba(color, 119 if i % 4 == 0 else 54))
    rng = Random(2711)
    for _ in range(210):
        x, y = rng.randrange(65, 552), rng.randrange(121, 468)
        if rng.randrange(2):
            x = 1920 - x
        p.rect((x, y, x, y), rgba(AMBER if x < 960 else CYAN, rng.randrange(18, 85)))
    p.text(82, 423, '01 / SPECTRAL ARRAY', 2.05, rgba(AMBER, 169), 1)
    p.text(1548, 422, '02 / RELAY TELEMETRY', 2.05, rgba(CYAN, 169), 1)
    return p.finish()


def title_plate():
    p = Plate(540, 225)
    glass_glow(p, [((1, 22, 480, 194), rgba('#532943', 150)),
                   ((9, 48, 18, 180), rgba(AMBER, 195))], 20)
    silhouette = [(12, 32), (482, 32), (520, 69), (520, 123), (458, 184), (12, 184)]
    p.polygon(silhouette, (14, 17, 24, 233))
    p.line(silhouette + [silhouette[0]], rgba(AMBER, 164), 1.5)
    p.polygon([(12, 32), (20, 32), (20, 184), (12, 184)], AMBER)
    p.line([(27, 174), (454, 174), (509, 122)], rgba(CORAL, 164))
    p.text(35, 49, 'ATLAS LAB / AUDIO VISUAL STUDY', 2.25, AMBER, 1.25)
    p.text(34, 82, 'PRISM RELAY', 8.2, BONE, 4.3)
    p.text(36, 151, 'VISIBLE SIGNAL / 01', 2.45, CYAN, 1.5)
    for i in range(12):
        x = 361 + i * 7
        p.line([(x, 151), (x - 4, 162)], rgba(AMBER, 211 if i in (0, 3, 8, 11) else 75), 2)
    return p.finish()


def phase_plate():
    p = Plate(330, 72)
    p.polygon([(5, 12), (309, 12), (325, 29), (325, 60), (5, 60)], (14, 24, 28, 228))
    p.line([(5, 12), (309, 12), (325, 29), (325, 60), (5, 60), (5, 12)], rgba(CYAN, 170), 1)
    p.rect((5, 12, 10, 60), CYAN)
    p.text(22, 22, '02 / CARRIER LOCK', 2.65, BONE, 1.5)
    p.text(22, 45, '120 / 160 BPM', 1.55, CYAN, 1)
    for i, height in enumerate((5, 12, 16, 25, 12, 19, 8, 4)):
        p.rect((263 + i * 5, 47 - height, 264 + i * 5, 47), rgba(CYAN, 175))
    return p.finish()


def observatory():
    p = Plate(384, 298)
    glass_glow(p, [((102, 32, 290, 220), rgba(CYAN, 64))], 39)
    p.polygon([(10, 58), (43, 24), (287, 24), (371, 105), (371, 239), (336, 273), (42, 273), (10, 238)],
              (13, 24, 31, 122))
    p.line([(10, 58), (43, 24), (287, 24), (371, 105), (371, 239), (336, 273), (42, 273), (10, 238), (10, 58)],
           rgba(CYAN, 109))
    for x in range(24, 366, 18):
        for y in range(54, 246, 18):
            p.rect((x, y, x + 1, y + 1), rgba(CYAN, 60))
    dial(p, 192, 137, 87, CYAN)
    for start, end in ((9, 54), (142, 234), (252, 306)):
        p.arc((192, 137), 55, start, end, rgba(CYAN, 137), 3)
    p.line([(78, 137), (298, 137)], rgba(CYAN, 66))
    p.line([(192, 42), (192, 236)], rgba(CYAN, 66))
    for x, y in ((22, 62), (357, 115), (22, 229), (357, 231)):
        p.ellipse((x - 2, y - 2, x + 2, y + 2), rgba(CYAN, 139))
    p.text(53, 251, 'SCOPE / A3', 1.8, rgba(CYAN, 192), 1)
    p.text(280, 251, 'LIVE', 1.8, rgba(AMBER, 195), 1)
    return p.finish()


def shutter_frame():
    p = Plate(328, 76)
    p.line([(4, 66), (4, 4), (323, 4), (323, 66)], rgba(BONE, 196), 1.5)
    p.line([(4, 71), (323, 71)], rgba(CYAN, 143), 1)
    p.line([(9, 10), (24, 10), (32, 19)], CYAN, 2)
    p.line([(298, 52), (308, 62), (319, 62)], AMBER, 2)
    for i in range(13):
        p.rect((32 + 22 * i, 67, 33 + 22 * i, 71), rgba(CYAN, 157))
    return p.finish()


def beam():
    p = Plate(256, 78)
    glass_glow(p, [((18, 22, 234, 56), (255, 255, 255, 172))], 13)
    p.polygon([(10, 43), (66, 28), (202, 28), (249, 37), (201, 42), (77, 48)], (255, 255, 255, 136))
    p.line([(8, 41), (231, 37)], (255, 255, 255, 249), 2.5)
    p.line([(53, 53), (191, 49), (224, 45)], (255, 255, 255, 119), 1.4)
    for i in range(14):
        x = 12 + i * 16
        p.rect((x, 64 - (i * 7) % 5, x + 5, 65 - (i * 7) % 5), (255, 255, 255, 111))
    return p.finish()


def atlas():
    # Three intentionally adjacent 96×96 regions. Colored edge pixels make linear
    # bleed visible; one atlas, no baked per-frame cropped textures.
    atlas = Image.new('RGBA', (288, 96))
    for i, color in enumerate((CYAN, AMBER, '#DE88FB')):
        p = Plate(96, 96)
        glass_glow(p, [((10, 10, 86, 86), rgba(color, 115))], 14)
        p.rect((0, 38, 2, 57), rgba(color, 241))
        p.rect((93, 38, 95.8, 57), rgba(color, 241))
        p.arc((48, 48), 35, 20 + 35 * i, 245 + 30 * i, color, 2.3)
        p.arc((48, 48), 30, 208, 375, rgba(color, 150), 1)
        if i == 0:
            p.polygon([(47, 25), (65, 62), (48, 54), (28, 64)], rgba(color, 145))
            p.line([(28, 64), (47, 25), (65, 62), (48, 54), (28, 64)], color, 2)
        elif i == 1:
            p.polygon([(50, 25), (63, 42), (57, 65), (30, 61), (42, 44)], rgba(color, 149))
            p.line([(50, 25), (63, 42), (57, 65), (30, 61), (42, 44), (50, 25)], color, 2)
        else:
            p.polygon([(47, 24), (71, 48), (47, 69), (31, 53)], rgba(color, 149))
            p.line([(47, 24), (71, 48), (47, 69), (31, 53), (47, 24)], color, 2)
        p.ellipse((46, 45, 51, 50), BONE)
        atlas.paste(p.finish(), (i * 96, 0))
    return atlas


def mote():
    p = Plate(18, 18)
    glass_glow(p, [((4, 5, 15, 14), rgba(CYAN, 165))], 2)
    p.polygon([(2, 12), (13, 5), (16, 9), (12, 12)], rgba(CYAN, 218))
    p.ellipse((11, 6, 14, 9), rgba(BONE, 235))
    return p.finish()


def cover():
    p = Plate(768, 768, INK)
    glass_glow(p, [((11, 140, 436, 480), rgba('#89394D', 157)),
                   ((310, 101, 733, 527), rgba('#188273', 169))], 103)
    for y in range(47, 735, 19):
        for x in range(41, 733, 19):
            p.rect((x, y, x + 1.1, y + 1.1), rgba(BONE, 68))
    for color, x, y in ((CYAN, 433, 295), (AMBER, 322, 354)):
        dial(p, x, y, 174, color, True)
        p.arc((x, y), 133, 181, 300, rgba(color, 211), 4)
        p.arc((x, y), 116, 28, 159, rgba(color, 165), 2)
    p.polygon([(222, 462), (410, 150), (557, 398), (460, 352), (408, 247), (317, 403)], '#16232A')
    p.line([(222, 462), (410, 150), (557, 398), (460, 352), (408, 247), (317, 403), (222, 462)], CYAN, 3)
    p.line([(222, 462), (557, 398), (463, 343), (410, 150)], AMBER, 2)
    p.line([(45, 123), (45, 52), (102, 52)], AMBER, 3)
    p.line([(666, 52), (723, 52), (723, 123)], CYAN, 3)
    p.line([(45, 659), (45, 713), (102, 713)], AMBER, 3)
    p.line([(666, 713), (723, 713), (723, 659)], CYAN, 3)
    p.polygon([(57, 527), (706, 527), (706, 665), (56, 665)], '#111923')
    p.line([(55, 527), (707, 527)], AMBER, 2)
    p.line([(55, 665), (707, 665)], CYAN, 2)
    p.text(63, 82, 'ATLAS LAB / S01', 3.65, AMBER, 1.6)
    p.text(70, 550, 'PRISM RELAY', 10.75, BONE, 5.7)
    p.text(74, 630, 'TWO TEMPI / ONE CARRIER', 3.15, CYAN, 2)
    return Image.alpha_composite(Image.new('RGBA', p.size, INK), p.finish())


def write_art(path: Path):
    def save(image, target):
        with target.open('xb') as stream:
            image.save(stream, format='PNG', compress_level=9)
        with Image.open(target) as rendered:
            if rendered.format != 'PNG' or rendered.size != image.size or rendered.mode != 'RGBA':
                raise ValueError(f'invalid generated PNG: {target}')
            rendered.verify()

    art = path / 'storyboard' / 'assets'
    art.mkdir()
    for name, create in (('surround', surround), ('title', title_plate), ('phase', phase_plate),
                         ('scope', observatory), ('gate', shutter_frame), ('beam', beam),
                         ('atlas', atlas), ('mote', mote)):
        save(create(), art / f'{name}.png')
    save(cover(), path / 'cover.png')
