"""Original mesh studies for result grades. Run with Blender 5.2 --background.

No fonts, extracted game assets, image textures, or external Python packages.
Three deliberately different S constructions; these are direction studies only.
"""
from pathlib import Path
import argparse
import math
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'design/grades/v4'


def linear(hex_color):
    values = [int(hex_color[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values)


def material(name, color, metallic=.75, roughness=.26, glow=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*linear(color), 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = m.diffuse_color
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = roughness
    p.inputs['Coat Weight'].default_value = .3
    p.inputs['Coat Roughness'].default_value = .2
    if glow:
        p.inputs['Emission Color'].default_value = m.diffuse_color
        p.inputs['Emission Strength'].default_value = glow
    return m


def polygon_area(poly):
    return sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(poly, poly[1:] + poly[:1])) / 2


def prism(name, poly, z0, z1, mats, bevel=.035):
    poly = list(poly)
    if polygon_area(poly) < 0:
        poly.reverse()
    n = len(poly)
    verts = [(x, y, z) for z in (z0, z1) for x, y in poly]
    faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    for m in mats:
        mesh.materials.append(m)
    # Material 0: sidewall, 1: face, 2: chamfer. No boolean slot merging.
    mesh.polygons[1].material_index = min(1, len(mats) - 1)
    if bevel:
        mod = o.modifiers.new('Machined edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        mod.affect = 'EDGES'
        mod.material = min(2, len(mats) - 1)
        mod.harden_normals = True
        normal = o.modifiers.new('Face normals', 'WEIGHTED_NORMAL')
        normal.keep_sharp = True
        normal.weight = 40
    return o


def shift(poly, dx=0, dy=0, scale=1, shear=0):
    return [(x * scale + y * shear + dx, y * scale + dy) for x, y in poly]


def clip(poly, nx, ny, threshold, positive=True):
    """Sutherland-Hodgman clipping; split the original silhouette into slabs."""
    out = []
    sign = 1 if positive else -1
    for a, b in zip(poly, poly[1:] + poly[:1]):
        da = (a[0] * nx + a[1] * ny - threshold) * sign
        db = (b[0] * nx + b[1] * ny - threshold) * sign
        if da >= 0:
            out.append(a)
        if (da >= 0) != (db >= 0):
            t = da / (da - db)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


def rail(name, points, radius, mat):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = radius
    curve.bevel_resolution = 1
    spline = curve.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for v, p in zip(spline.points, points):
        v.co = (*p, 1)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    curve.materials.append(mat)
    return obj


def light(name, loc, energy, color, size, size_y=None):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.color = linear(color)
    data.shape = 'RECTANGLE'
    data.size = size
    data.size_y = size_y or size
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (Vector((0, 0, 0)) - obj.location).to_track_quat('-Z', 'Y').to_euler()


def setup(samples):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    prefs = bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type = 'OPTIX'
        prefs.get_devices()
        gpu = False
        for d in prefs.devices:
            d.use = d.type == 'OPTIX'
            gpu |= d.use
        sc.cycles.device = 'GPU' if gpu else 'CPU'
    except Exception:
        sc.cycles.device = 'CPU'
    sc.render.resolution_x = sc.render.resolution_y = 1024
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.render.image_settings.color_depth = '8'
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'
    sc.view_settings.exposure = -.35
    # Rest state contains no compositor bloom. Specular lights describe the solid.
    world = bpy.data.worlds.new('Studio ambient')
    sc.world = world
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (*linear('#9DBBD6'), 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .23
    light('Upper softbox', (-3.5, 4.0, 6), 720, '#E2F5FF', 4, 1.5)
    light('Right strip', (4.5, .5, 3.0), 600, '#78DFFF', 1.2, 5)
    light('Low reflected blue', (-2.7, -3.5, 2.5), 270, '#278BAB', 3, 1)
    light('Pink rear edge', (2, 2, -2.5), 380, '#FF4D8F', 2, 3)
    camera = bpy.data.cameras.new('Camera')
    obj = bpy.data.objects.new('Camera', camera)
    bpy.context.collection.objects.link(obj)
    camera.type = 'ORTHO'
    camera.ortho_scale = 3.72
    obj.rotation_euler = (math.radians(10), math.radians(-13), 0)
    obj.location = obj.rotation_euler.to_quaternion() @ Vector((0, 0, 8))
    sc.camera = obj
    return sc


def palette():
    return {
        'dark': material('Blue-black titanium', '#142537', .87, .27),
        'edge': material('Pale titanium cut', '#A4C5D4', .9, .21),
        'cyan': material('Cyan ceramic', '#169FBE', .48, .24),
        'light': material('Light cyan ceramic', '#35E0FF', .48, .25),
        'deep': material('Deep blue enamel', '#125570', .6, .28),
        'black': material('Dark machined recess', '#060E19', .4, .32),
        'energy': material('Energy seam', '#35E0FF', .3, .25, 2.4),
        'pink': material('Pink service notch', '#FF4D8F', .25, .24, 1),
    }


SILHOUETTE = [
    (-.72, 1.23), (.95, 1.23), (1.18, .97), (.94, .56), (.64, .79),
    (-.37, .79), (-.59, .57), (-.38, .36), (.71, -.13), (.98, -.46),
    (.87, -.89), (.51, -1.23), (-1.00, -1.23), (-1.23, -.96),
    (-.94, -.56), (-.64, -.80), (.28, -.80), (.47, -.62), (.31, -.43),
    (-.75, .02), (-1.01, .35), (-1.00, .87),
]


def armor(p):
    """Three load-bearing plates, diagonal fault lines, thick asymmetric corners."""
    poly = shift(SILHOUETTE, shear=.12)
    prism('Continuous recessed spine', poly, -.25, -.12, [p['dark'], p['black'], p['deep']], .045)
    limits = [(-10, -.29), (-.29, .50), (.50, 10)]
    for i, (lo, hi) in enumerate(limits):
        part = clip(clip(poly, -.19, 1, lo + .027), -.19, 1, hi - .027, False)
        dx, dy, z = [(-.055, -.025, .02), (.015, 0, .075), (.065, .035, .12)][i]
        part = shift(part, dx, dy)
        prism(f'Armor plate {i}', part, -.10, z + .12, [p['dark'], p['cyan'], p['edge']], .043)
        # An inset panel has its own perimeter and shadow gap.
        cx = sum(x for x, y in part) / len(part)
        cy = sum(y for x, y in part) / len(part)
        inset = [(cx + (x - cx) * .88, cy + (y - cy) * .80) for x, y in part]
        prism(f'Inset ceramic {i}', inset, z + .123, z + .145,
              [p['deep'], p['light'] if i == 2 else p['cyan'], p['deep']], .013)
    rail('Top inset energy seam', [(-.45, 1.115, .278), (.84, 1.115, .278)], .008, p['energy'])
    rail('Lower inset energy seam', [(-.85, -1.12, .18), (.37, -1.12, .18)], .008, p['energy'])
    # Two tiny enamel markers, like cut pinstripes, never a full glowing outline.
    prism('Pink marker', [(.91,.96),(1.035,.84),(1.065,.89),(.94,1.01)], .245,.26,[p['pink']], .002)
    for i in range(3):
        x = -.82 + i * .12
        prism('Bottom vents ' + str(i), [(x,-.99),(x+.03,-.96),(x+.1,-1.065),(x+.07,-1.095)],
              .187,.195,[p['black']], .002)


def blades(p):
    """Two opposing hooked blades weave through a narrow diagonal waist."""
    upper = [(-.89, 1.03), (-.46, 1.28), (1.48, 1.22), (.90, .69),
             (-.32,.78),(-.62,.54),(.38,-.07),(.17,-.35),(-.80,.15),(-1.12,.54)]
    lower = [(-x, -y) for x, y in upper]
    for index, poly in enumerate((upper, lower)):
        z = .05 if index else .14
        prism(f'Forged blade {index}', poly, -.21, z, [p['dark'],p['edge'],p['edge']], .034)
    top_face = [(-.78,.95),(-.39,1.17),(1.21,1.13),(.82,.83),(-.34,.9),(-.76,.54),(.23,-.065),(.15,-.18),(-.71,.26),(-.98,.57)]
    for index in (0,1):
        poly = top_face if index == 0 else [(-x,-y) for x,y in top_face]
        z = .155 if index == 0 else .065
        prism(f'Blade enamel {index}',poly,z,z+.028,[p['deep'],p['cyan'] if index else p['light'],p['deep']],.012)
    # Dark triangular rakes accentuate the cutting direction.
    prism('Top rake', [(-.39,1.13),(1.12,1.09),(.05,.98),(-.61,.92)],.19,.205,[p['deep']],.008)
    prism('Bottom rake',[(.39,-1.13),(-1.12,-1.09),(-.05,-.98),(.61,-.92)],.10,.115,[p['deep']],.008)
    rail('Upper blade live edge',[(-.46,1.28,-.02),(1.39,1.225,-.02)],.012,p['energy'])
    rail('Lower blade live edge',[(.46,-1.28,-.09),(-1.39,-1.225,-.09)],.012,p['energy'])
    prism('Floating pink lock',[(-1.15,.84),(-1.01,.96),(-1.035,.76),(-1.16,.65)],-.12,.03,[p['dark'],p['pink'],p['edge']],.01)


def crystal(p):
    """A faceted cyan monolith with a raised irregular ridge, no fog or glass glow."""
    # Variable-width open path is the custom S skeleton, with mitered angular bends.
    centres = [(1.00,.89),(-.32,1.04),(-.81,.67),(-.57,.18),(.54,-.24),(.76,-.68),(.30,-1.05),(-1.01,-.88)]
    widths = [.38,.55,.63,.61,.63,.56,.54,.36]
    left, right = [], []
    for i, (pos, width) in enumerate(zip(centres,widths)):
        if i==0:
            tang = Vector(centres[1])-Vector(pos)
        elif i==len(centres)-1:
            tang = Vector(pos)-Vector(centres[i-1])
        else:
            tang = (Vector(pos)-Vector(centres[i-1])).normalized() + (Vector(centres[i+1])-Vector(pos)).normalized()
        tang.normalize()
        norm = Vector((-tang.y,tang.x))
        if 0 < i < len(centres)-1:
            segment=(Vector(centres[i+1])-Vector(pos)).normalized()
            width /= max(.65, abs(norm.dot(Vector((-segment.y,segment.x)))))
        left.append(tuple(Vector(pos)+norm*width/2))
        right.append(tuple(Vector(pos)-norm*width/2))
    outline = left + list(reversed(right))
    prism('Obsidian crystal girdle',outline,-.30,.10,[p['dark'],p['deep'],p['edge']],.025)
    facets = [material('Crystal face '+str(i),col,.52,.16) for i,col in enumerate(
        ['#28ACCD','#58DCED','#0D607F','#298BB2','#99DBE2','#217394'])]
    verts=[]
    for i, (l,r,c) in enumerate(zip(left,right,centres)):
        ridge=Vector(c)+Vector((.02 if i%2 else -.04,.03 if i%2 else -.01))
        verts += [(*l,.13),(*ridge,.31+[.025,-.015,.09,-.04,.08,-.02,.06,.02][i]),(*r,.13)]
    faces=[]
    for i in range(len(centres)-1):
        a,b=i*3,(i+1)*3
        faces += [(a,a+1,b),(b,a+1,b+1),(a+1,a+2,b+2),(a+1,b+2,b+1)]
    mesh=bpy.data.meshes.new('Irregular mineral planes')
    mesh.from_pydata(verts,[],faces)
    mesh.update()
    obj=bpy.data.objects.new('Faceted S crown',mesh)
    bpy.context.collection.objects.link(obj)
    for m in facets:mesh.materials.append(m)
    for i,face in enumerate(mesh.polygons):face.material_index=[0,1,2,3,4,1,3,2,0,4,2,3][i%12]
    # Sparse crystal splinters, in the same controlled resting silhouette.
    prism('Cyan splinter',[(1.13,.62),(1.32,.86),(1.35,.48)],-.08,.07,[p['deep'],p['light'],p['edge']],.006)
    prism('Obsidian splinter',[(-1.05,-.56),(-1.31,-.83),(-1.26,-.48)],-.19,-.01,[p['dark'],p['deep'],p['edge']],.008)
    rail('Lower crystal seam',[(*left[5],-.12),(*left[6],-.12),(*left[7],-.12)],.01,p['energy'])


BUILDERS = {'01_armor': armor, '02_blades': blades, '03_crystal': crystal}


def main():
    args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser()
    parser.add_argument('--only',choices=list(BUILDERS))
    parser.add_argument('--samples',type=int,default=96)
    parser.add_argument('--output',type=Path,default=OUT)
    opts=parser.parse_args(args)
    opts.output.mkdir(parents=True,exist_ok=True)
    for key,build in BUILDERS.items():
        if opts.only and key != opts.only:continue
        sc=setup(opts.samples)
        build(palette())
        sc.render.filepath=str(opts.output / (key+'_S_master.png'))
        bpy.ops.wm.save_as_mainfile(filepath=str(opts.output / (key+'_S.blend')))
        bpy.ops.render.render(write_still=True)
        print('CONCEPT_DONE '+key,flush=True)


if __name__=='__main__':
    main()
