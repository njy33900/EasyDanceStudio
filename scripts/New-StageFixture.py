"""Generate an original synthetic PMX 2.0 stage outside Unity Assets/Git.

No third-party data. A floor quad plus four box pillars, two double-sided
textured materials (generated PNGs next to the PMX, referenced by relative path)
and a single root bone, so it exercises the static-background path.
Field layout follows the pinned UMT reader (same as New-PhysicsChainFixtures.py).
"""
import argparse
import hashlib
import json
import struct
import zlib
from pathlib import Path

FLOOR_HALF = 60.0      # PMX units (UMT imports at ~0.08 m/unit -> ~9.6 m floor)
PILLAR_HALF = 3.0
PILLAR_HEIGHT = 40.0
PILLAR_OFFSET = 45.0


def write_png(path, size, pixel):
    """Minimal RGB PNG; pixel(x, y) -> (r, g, b) bytes."""
    rows = b''.join(b'\x00' + bytes(c for x in range(size) for c in pixel(x, y)) for y in range(size))

    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body) & 0xffffffff)

    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 2, 0, 0, 0)) +
                     chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b''))


def box(cx, cz, half, height):
    """24 vertices (4 per face) and 12 triangles for an axis-aligned box on y=0."""
    x0, x1, z0, z1, y0, y1 = cx - half, cx + half, cz - half, cz + half, 0.0, height
    faces = [
        ((0, 1, 0), [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)]),
        ((0, 0, -1), [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]),
        ((0, 0, 1), [(x1, y0, z1), (x0, y0, z1), (x0, y1, z1), (x1, y1, z1)]),
        ((-1, 0, 0), [(x0, y0, z1), (x0, y0, z0), (x0, y1, z0), (x0, y1, z1)]),
        ((1, 0, 0), [(x1, y0, z0), (x1, y0, z1), (x1, y1, z1), (x1, y1, z0)]),
        ((0, -1, 0), [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)]),
    ]
    vertices, triangles = [], []
    for normal, corners in faces:
        base = len(vertices)
        vertices.extend((corner, normal, uv) for corner, uv in zip(corners, ((0, 1), (1, 1), (1, 0), (0, 0))))
        triangles.extend([(base, base + 1, base + 2), (base, base + 2, base + 3)])
    return vertices, triangles


def write_stage(path):
    data = bytearray()

    def put(fmt, *values):
        data.extend(struct.pack('<' + fmt, *values))

    def text(value):
        raw = value.encode('utf-8')
        put('i', len(raw))
        data.extend(raw)

    floor_vertices = [((-FLOOR_HALF, 0, -FLOOR_HALF), (0, 1, 0), (0, 0)), ((FLOOR_HALF, 0, -FLOOR_HALF), (0, 1, 0), (1, 0)),
                      ((FLOOR_HALF, 0, FLOOR_HALF), (0, 1, 0), (1, 1)), ((-FLOOR_HALF, 0, FLOOR_HALF), (0, 1, 0), (0, 1))]
    floor_triangles = [(0, 2, 1), (0, 3, 2)]
    pillar_vertices, pillar_triangles = [], []
    for sx in (-1, 1):
        for sz in (-1, 1):
            vertices, triangles = box(sx * PILLAR_OFFSET, sz * PILLAR_OFFSET, PILLAR_HALF, PILLAR_HEIGHT)
            base = len(floor_vertices) + len(pillar_vertices)
            pillar_vertices.extend(vertices)
            pillar_triangles.extend(tuple(base + i for i in tri) for tri in triangles)
    vertices = floor_vertices + pillar_vertices
    assert len(vertices) <= 255, 'vertex index size is 1 byte'

    data.extend(b'PMX ')
    # version, 8 globals: UTF-8, no additional UV, index sizes vertex/texture/material/bone/morph/rigid = 1
    put('f9B', 2.0, 8, 1, 0, 1, 1, 1, 1, 1, 1)
    for value in ('Synthetic stage', 'Synthetic stage',
                  'Original diagnostic stage fixture; no third-party assets.', ''):
        text(value)
    put('i', len(vertices))
    for (x, y, z), (nx, ny, nz), (u, v) in vertices:
        put('3f', x, y, z)
        put('3f', nx, ny, nz)
        put('2fBb f', u, v, 0, 0, 1)  # UV, BDEF1, root bone, edge scale
    put('i', 3 * (len(floor_triangles) + len(pillar_triangles)))
    for tri in floor_triangles + pillar_triangles:
        put('3B', *tri)
    textures = ('tex/floor.png', 'tex/pillar.png')
    (path.parent / 'tex').mkdir(exist_ok=True)
    write_png(path.parent / textures[0], 64,
              lambda x, y: (70, 82, 104) if (x // 8 + y // 8) % 2 else (122, 136, 160))
    write_png(path.parent / textures[1], 16,
              lambda x, y: (196, 186, 160) if y % 8 else (150, 140, 118))
    put('i', len(textures))
    for texture in textures:
        text(texture)
    put('i', 2)
    for index, (name, colour, count) in enumerate((('floor', (1, 1, 1, 1), len(floor_triangles) * 3),
                                                   ('pillar', (1, 1, 1, 1), len(pillar_triangles) * 3))):
        text(name)
        text(name)
        put('4f', *colour)
        put('3f', 0, 0, 0)
        put('f', 0)
        put('3f', .5, .5, .5)
        # flags: double-sided; edge colour/size; texture index; no sphere; shared toon 0
        put('B4ffbbBBB', 0x01, 0, 0, 0, 1, 0, index, -1, 0, 1, 0)
        text('')
        put('i', count)
    put('i', 1)
    text('全ての親')
    text('root')
    put('3f', 0, 0, 0)
    put('biH', -1, 0, 0x1e)
    put('3f', 0, 10, 0)
    put('ii', 0, 0)  # morphs, display frames
    put('ii', 0, 0)  # rigid bodies, joints
    path.write_bytes(data)
    return dict(path=str(path.resolve()), bytes=len(data), sha256=hashlib.sha256(data).hexdigest(),
                vertices=len(vertices), triangles=len(floor_triangles) + len(pillar_triangles),
                materials=2, textures=list(textures), bones=1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    entry = write_stage(args.output / 'stage.pmx')
    (args.output / 'manifest.json').write_text(json.dumps([entry], indent=2), encoding='utf-8')
    print('Generated original external stage fixture', entry['path'])
