"""Raw VMD2 camera oracle, independent of UMT's parsed camera representation.

Enforces selected source-key and fractional fidelity for the controlled fixture.
Unequal XYZ curves can expose the reference path's merged XYZ/60 Hz approximation;
this acceptance is not whole-engine parity.
"""
import argparse
import bisect
import hashlib
import json
import math
import struct
from pathlib import Path


def read_camera(path):
    data = Path(path).read_bytes()
    if not data.startswith(b"Vocaloid Motion Data 0002"):
        raise ValueError("Oracle requires VMD2")
    offset = 50
    for size in (111, 23):
        count, = struct.unpack_from('<I', data, offset)
        offset += 4 + count * size
    count, = struct.unpack_from('<I', data, offset)
    offset += 4
    keys = {}
    for index in range(count):
        record = struct.unpack_from('<If3f3f24sIB', data, offset + index * 61)
        keys[record[0]] = record
    return [keys[f] for f in sorted(keys)], hashlib.sha256(data).hexdigest()


def bezier(raw, offset, x):
    a, b, c, d = (v / 127 for v in raw[offset:offset + 4])
    def cubic(t, p, q):
        return 3 * (1-t)**2*t*p + 3*(1-t)*t*t*q + t**3
    lo, hi = 0., 1.
    for _ in range(48):
        t = (lo + hi) / 2
        if cubic(t, a, b) < x:
            lo = t
        else:
            hi = t
    return cubic((lo+hi)/2, c, d)


def evaluate(keys, frame):
    frames = [k[0] for k in keys]
    index = bisect.bisect_right(frames, frame)
    if index == 0:
        return keys[0][1:8], float(keys[0][9])
    if index == len(keys):
        return keys[-1][1:8], float(keys[-1][9])
    a, b = keys[index - 1:index + 1]
    # Consecutive camera keys represent a cut: hold until the next integer key.
    t = 0 if b[0] - a[0] == 1 else (frame-a[0])/(b[0]-a[0])
    w = [bezier(b[8], j*4, t) for j in range(6)]
    values = [a[1] + (b[1]-a[1])*w[4]]
    values.extend(a[j] + (b[j]-a[j])*w[j-2] for j in range(2, 5))
    values.extend(a[j] + ((b[j]-a[j]+math.pi) % (2*math.pi)-math.pi)*w[3] for j in range(5, 8))
    return values, a[9] + (b[9]-a[9])*w[5]


def basis(rx, ry, rz, vector):
    # Row-vector Z * X * Y camera basis, followed by MMD -> Unity rebasing.
    x, y, z = vector
    x, y = x*math.cos(rz)-y*math.sin(rz), x*math.sin(rz)+y*math.cos(rz)
    y, z = y*math.cos(rx)-z*math.sin(rx), y*math.sin(rx)+z*math.cos(rx)
    angle = -(ry + math.pi)
    return (x*math.cos(angle)+z*math.sin(angle), y, -x*math.sin(angle)+z*math.cos(angle))


def expected(values):
    distance, x, y, z, rx, ry, rz = values
    f = basis(rx, ry, rz, (0, 0, 1))
    forward = (f[0], -f[1], f[2])
    u = basis(rx, ry, rz, (0, 1, 0))
    up = (-u[0], u[1], -u[2])
    center = (-x*.08, y*.08, -z*.08)
    return tuple(center[i]-forward[i]*abs(distance)*.08 for i in range(3)), forward, up


def rotate(q, v):
    u = [q[k] for k in ('x', 'y', 'z')]
    norm = math.sqrt(sum(x*x for x in u) + q['w']**2)
    u, w = [x/norm for x in u], q['w']/norm
    dot = sum(a*b for a, b in zip(u, v))
    cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
    return tuple(2*dot*u[i] + (2*w*w-1)*v[i] + 2*w*cross[i] for i in range(3))


def angle(a, b):
    return math.degrees(math.acos(max(-1, min(1, sum(x*y for x, y in zip(a, b))))))


def main():
    # Analytic checks for camera layout and Unity basis; do not silently accept
    # an oracle with swapped camera-control axes or a reversed view direction.
    linear = bytes((20, 107, 20, 107)) * 6
    assert abs(bezier(linear, 0, .37)-.37) < 1e-10
    position, forward, up = expected((-10, 0, 10, 0, 0, 0, 0))
    assert math.dist(position, (0, .8, .8)) < 1e-10
    assert math.dist(forward, (0, 0, -1)) < 1e-10
    assert math.dist(up, (0, 1, 0)) < 1e-10
    parser = argparse.ArgumentParser()
    parser.add_argument('--report', required=True)
    parser.add_argument('--vmd', required=True)
    args = parser.parse_args()
    keys, digest = read_camera(args.vmd)
    report = json.loads(Path(args.report).read_text(encoding='utf-8-sig'))
    if not report['success']:
        raise ValueError(report['error'])
    key_errors, fractional_errors = [], []
    for pose in report['poses']:
        frame = pose['seconds']*30
        nearest = min(keys, key=lambda k: abs(k[0]-frame))
        on_key = abs(nearest[0]-frame) < .002
        values, fov = (nearest[1:8], nearest[9]) if on_key else evaluate(keys, frame)
        position, forward, up = expected(values)
        actual = tuple(pose['position'][c] for c in 'xyz')
        errors = (math.dist(position, actual), max(angle(forward, rotate(pose['rotation'], (0, 0, 1))),
                   angle(up, rotate(pose['rotation'], (0, 1, 0)))), abs(pose['fov']-fov))
        (key_errors if on_key else fractional_errors).append(errors)
    def maxima(values):
        return dict(zip(('positionMeters', 'basisDegrees', 'fovDegrees'), map(max, zip(*values))))
    result = {'sourceSha256': digest, 'sourceKeys': len(keys), 'keyChecks': len(key_errors),
              'keyMaximum': maxima(key_errors), 'fractionalChecks': len(fractional_errors),
              'fractionalMaximumFinding': maxima(fractional_errors),
              'differentXyzCurveKeys': sum(k[8][:4] != k[8][4:8] or k[8][:4] != k[8][8:12] for k in keys),
              'limit': report['limitation']}
    m = result['keyMaximum']
    f = result['fractionalMaximumFinding']
    result['accepted'] = (len(key_errors) >= 60 and m['positionMeters'] <= .001 and
                          m['basisDegrees'] <= .05 and m['fovDegrees'] <= .01 and
                          f['positionMeters'] <= .001 and f['basisDegrees'] <= .05 and f['fovDegrees'] <= .01)
    Path(args.report).with_name('camera-comparison.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    if not result['accepted']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
