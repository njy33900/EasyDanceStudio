"""Generate original diagnostic PMX 2.0 fixtures outside Unity Assets/Git.

No third-party model data is used. PMX field layout follows the pinned UMT reader.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path


def write_model(path, variant):
    data = bytearray()

    def put(fmt, *values):
        data.extend(struct.pack('<' + fmt, *values))

    def text(value):
        raw = value.encode('utf-8')
        put('i', len(raw))
        data.extend(raw)

    def vec(x=0, y=0, z=0):
        put('3f', x, y, z)

    data.extend(b'PMX ')
    put('f9B', 2.0, 8, 1, 0, 1, 1, 1, 1, 1, 1)
    for value in ('Synthetic physics chain ' + variant, variant,
                  'Original diagnostic fixture; no third-party assets.', ''):
        text(value)
    put('i', 3)
    for x, y in ((-1, 8), (1, 8), (0, 10)):
        vec(x, y, 0)
        vec(0, 0, -1)
        put('2fBb f', 0, 0, 0, 2, 1)  # UV, BDEF1, bone, edge scale
    put('i3B', 3, 0, 1, 2)
    put('i', 0)  # textures
    put('i', 1)
    text('plain')
    text('plain')
    put('4f', .7, .7, .7, 1)
    vec()
    put('f', 0)
    vec(.3, .3, .3)
    put('B4ffbbBBB', 0, 0, 0, 0, 1, 0, -1, -1, 0, 1, 0)
    text('')
    put('i', 3)
    bones = [('センター', 10, -1), ('上半身', 10, 0),
             ('pendulum', 8, 1), ('tip', 6, 2), ('ik_target', 6, 0)]
    put('i', len(bones))
    for i, (name, y, parent) in enumerate(bones):
        text(name)
        text('bone_' + str(i))
        vec(0, y, 0)
        flags = 0x1e
        if variant == 'append' and i == 2:
            flags |= 0x100
        if variant == 'ik' and i == 4:
            flags |= 0x20
        put('biH', parent, i, flags)
        vec(0, -2, 0)
        if flags & 0x100:
            put('bf', 1, .5)
        if flags & 0x20:
            put('bifi', 3, 16, .25, 1)
            put('bB', 2, 0)
    put('ii', 0, 0)  # morphs, display frames
    put('i', 2)
    for index, y in enumerate((10, 8)):
        text('body_' + str(index))
        text('')
        put('bBhB', index + 1, 0, 0, 0)  # bone, group, no contacts, sphere
        vec(.3, .3, .3)
        vec(0, y, 0)
        vec()
        put('5fB', 1, .1, .1, 0, .5, index)  # kinetic then dynamic
    put('i', 0 if variant == 'free' else 1)
    if variant != 'free':
        text('hinge')
        text('')
        put('Bbb', 0, 0, 1)
        vec(0, 9, 0)
        vec()
        vec()  # locked translation minimum/maximum
        vec()
        vec(-.5, -.5, -.5)
        vec(.5, .5, .5)
        vec()
        vec()
    path.write_bytes(data)
    return dict(path=str(path.resolve()), variant=variant, bytes=len(data),
                sha256=hashlib.sha256(data).hexdigest(), bones=5, rigidBodies=2,
                joints=0 if variant == 'free' else 1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    entries = [write_model(args.output / (v + '.pmx'), v)
               for v in ('free', 'joint', 'append', 'ik')]
    (args.output / 'manifest.json').write_text(json.dumps(entries, indent=2), encoding='utf-8')
    print('Generated', len(entries), 'original external PMX fixtures')
