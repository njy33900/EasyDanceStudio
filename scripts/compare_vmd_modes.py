"""Offline comparison of actual Windows Player reports; Python standard library only.

The independent oracle parses source VMD bytes directly. Rotation evaluation uses
Bezier time weighting + shortest-path quaternion slerp, as in babylon-mmd. Saba's
inspected revision uses the preceding key's control points; report that alternative
separately rather than pretending the references agree in every detail.
"""
import argparse
import bisect
import hashlib
import json
import math
from pathlib import Path
import struct


def quat(value):
    return [value[k] for k in ("x", "y", "z", "w")]


def normalized(q):
    length = math.sqrt(sum(v * v for v in q))
    if length < 1e-12:
        raise ValueError("Zero-length quaternion in comparison input")
    return [v / length for v in q]


def angle(a, b):
    dot = abs(sum(x * y for x, y in zip(normalized(a), normalized(b))))
    return math.degrees(2 * math.acos(min(1, dot)))


def slerp(a, b, t):
    a, b = normalized(a), normalized(b)
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0:
        b, dot = [-x for x in b], -dot
    if dot > 0.9995:
        return normalized([x + t * (y - x) for x, y in zip(a, b)])
    theta = math.acos(max(-1, min(1, dot)))
    return [(x * math.sin((1 - t) * theta) + y * math.sin(t * theta)) / math.sin(theta)
            for x, y in zip(a, b)]


def bezier_weight(control, x):
    x1, y1, x2, y2 = control
    def coordinate(t, a, b):
        return 3 * (1 - t) ** 2 * t * a + 3 * (1 - t) * t * t * b + t ** 3
    low, high = 0.0, 1.0
    for _ in range(48):
        mid = (low + high) / 2
        if coordinate(mid, x1, x2) < x:
            low = mid
        else:
            high = mid
    return coordinate((low + high) / 2, y1, y2)


def self_check():
    identity = [0, 0, 0, 1]
    quarter_turn = [0, 0, math.sin(math.pi / 4), math.cos(math.pi / 4)]
    assert abs(angle(identity, slerp(identity, quarter_turn, 0.5)) - 45) < 1e-6
    assert angle(slerp(identity, quarter_turn, 0.5),
                 slerp(identity, [-v for v in quarter_turn], 0.5)) < 1e-6
    assert abs(bezier_weight([20/127, 20/127, 107/127, 107/127], 0.37) - 0.37) < 1e-10
    # Analytic layout regression: physics-off bytes must not flatten the
    # rotation curve. This mirrors the documented VMD extension, not UMT APIs.
    raw = bytearray(64)
    raw[2] = raw[3] = 0
    for index, value in zip((48, 52, 56, 60), (20, 20, 107, 107)):
        raw[index] = value
    assert rotation_control(raw) == [20/127, 20/127, 107/127, 107/127]


def rotation_control(raw):
    return [raw[index] / 127 for index in (48, 52, 56, 60)]


def source_tracks(path, wanted):
    data = path.read_bytes()
    if not data.startswith(b"Vocaloid Motion Data 0002"):
        raise ValueError("Independent parser currently requires VMD version 2")
    count = struct.unpack_from("<I", data, 50)[0]
    if 54 + count * 111 > len(data):
        raise ValueError("Truncated VMD bone section")
    tracks = {name: [] for name in wanted}
    for i in range(count):
        offset = 54 + i * 111
        name = data[offset:offset + 15].split(b"\0")[0].decode("cp932", errors="replace")
        if name not in wanted:
            continue
        frame = struct.unpack_from("<I", data, offset + 15)[0]
        x, y, z, w = struct.unpack_from("<4f", data, offset + 31)
        # MMD-to-Unity axis convention of the pinned importer, not an Euler conversion.
        rotation = normalized([-x, y, -z, w])
        raw = data[offset + 47:offset + 111]
        # babylon-mmd vmdLoader uses the rotation block at 3*16. Bytes 2/3
        # of the first block can be physics toggles, NOT Z/R Bezier x1.
        control = rotation_control(raw)
        legacy_control = [raw[i] / 127 for i in (3, 7, 11, 15)]
        tracks[name].append((frame, rotation, control, legacy_control))
    for track in tracks.values():
        track.sort(key=lambda item: item[0])
    return tracks, hashlib.sha256(data).hexdigest()


def source_rotation(track, frame, preceding_control=False):
    frames = [key[0] for key in track]
    upper = bisect.bisect_right(frames, frame)
    if upper == 0:
        return track[0][1]
    if upper == len(track):
        return track[-1][1]
    a, b = track[upper - 1], track[upper]
    gradient = (frame - a[0]) / (b[0] - a[0])
    weight = bezier_weight(a[3] if preceding_control else b[2], gradient)
    return slerp(a[1], b[1], weight)


def oracle_comparison(report, tracks, preceding_control=False):
    checks = []
    for pose in report["motion"]["poses"] + report["motion"].get('fkSweep', []):
        for sample in pose["sourceCurveRotations"]:
            track = tracks[sample["name"]]
            if not track:
                raise ValueError("Missing independent source track: " + sample["name"])
            expected = source_rotation(track, pose["seconds"] * 30, preceding_control)
            checks.append({"seconds": pose["seconds"], "bone": sample["name"],
                           "errorDegrees": angle(expected, quat(sample["rotation"]))})
    if not checks:
        raise ValueError("No independent FK samples were supplied")
    worst = max(checks, key=lambda item: item["errorDegrees"])
    return {"checks": len(checks), "maxErrorDegrees": worst["errorDegrees"],
            "rmsErrorDegrees": math.sqrt(sum(c["errorDegrees"] ** 2 for c in checks) / len(checks)),
            "worst": worst, "withinDiagnosticTolerance": worst["errorDegrees"] <= 0.5,
            "samples": checks}


def pose_comparison(a, b):
    rows = []
    for p, q in zip(a["motion"]["poses"], b["motion"]["poses"]):
        if abs(p["seconds"] - q["seconds"]) > 1e-5:
            raise ValueError("Mismatched sample times")
        if len(p["bones"]) != len(q["bones"]) or not p["bones"]:
            raise ValueError("Mismatched or empty skeleton")
        distances, angles = [], []
        for left, right in zip(p["bones"], q["bones"]):
            if left["name"] != right["name"]:
                raise ValueError("Mismatched skeleton order")
            distance = math.sqrt(sum((left["worldPosition"][axis] - right["worldPosition"][axis]) ** 2
                                     for axis in ("x", "y", "z")))
            distances.append((distance, left["name"]))
            angles.append((angle(quat(left["localRotation"]), quat(right["localRotation"])), left["name"]))
        if len(p["morphWeights"]) != len(q["morphWeights"]):
            raise ValueError("Mismatched morph channel counts")
        morph_error = max((abs(x-y) for x, y in zip(p["morphWeights"], q["morphWeights"])), default=0)
        max_position, position_bone = max(distances)
        max_rotation, rotation_bone = max(angles)
        rows.append({"seconds": p["seconds"], "maxWorldPositionErrorMeters": max_position,
                     "positionBone": position_bone, "maxLocalRotationErrorDegrees": max_rotation,
                     "rotationBone": rotation_bone, "maxMorphWeightError": morph_error,
                     "rmsWorldPositionErrorMeters": math.sqrt(sum(x*x for x, _ in distances) / len(distances)),
                     "rmsLocalRotationErrorDegrees": math.sqrt(sum(x*x for x, _ in angles) / len(angles)),
                     "worstRotations": sorted(angles, reverse=True)[:5]})
    return {"samples": rows, "parityWithinDiagnosticTolerance": all(
        row["maxWorldPositionErrorMeters"] <= 0.001 and row["maxLocalRotationErrorDegrees"] <= 0.5
        and row["maxMorphWeightError"] <= 0.01 for row in rows)}


def shoulder_constraint(report):
    """Controlled Gene check: identity rests, nonlocal 1.0 rotation inheritance."""
    metadata = next(item for item in report["motion"]["constraints"] if item["name"] == "右肩連")
    if metadata['target'] != '右肩' or metadata['influence'] != 1 or 'LocalConstraint' in metadata['flags']:
        raise ValueError('Gene constraint assumption changed')
    rows = []
    for pose in report["motion"]["poses"]:
        bones = {bone['name']: bone for bone in pose['bones']}
        rows.append({'seconds': pose['seconds'], 'errorDegrees': angle(
            quat(bones['右肩連']['localRotation']), quat(bones['右肩']['localRotation']))})
    maximum = max(row['errorDegrees'] for row in rows)
    return {'maxErrorDegrees': maximum, 'withinDiagnosticTolerance': maximum <= 0.05, 'samples': rows}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--vmd", type=Path, required=True)
    parser.add_argument('--require-direct-accuracy', action='store_true')
    args = parser.parse_args()
    self_check()
    names = ["vmd-player", "vmd-player-default-ik", "vmd-player-baked-reference",
             "vmd-player-direct", "vmd-player-direct-default-ik"]
    reports = [json.loads((args.artifacts / (name + ".json")).read_text(encoding="utf-8-sig")) for name in names]
    for report in reports:
        if not report["success"] or len(report["motion"]["poses"]) != 6:
            raise ValueError("Incomplete Player report")
    if len({report["motion"]["source"] for report in reports}) != 1:
        raise ValueError("Player reports used different motions")
    wanted = {sample["name"] for pose in reports[0]["motion"]["poses"] for sample in pose["sourceCurveRotations"]}
    tracks, source_hash = source_tracks(args.vmd, wanted)
    # This is a post-solver local-pose diagnostic, not a pure FK equality claim.
    # It also exposes the fractional-frame approximation in a 30-fps bake.
    baked_selected = json.loads(json.dumps(reports[2]))
    for pose in baked_selected["motion"]["poses"]:
        pose["sourceCurveRotations"] = [{"name": bone["name"], "rotation": bone["localRotation"]}
                                        for bone in pose["bones"] if bone["name"] in wanted]
    result = {"sourceSha256": source_hash, "oracleSelfChecksPassed": True,
              "oracleVersion": "2: Babylon rotation block 48/52/56/60; first-block physics bytes excluded",
              "diagnosticTolerances": {"worldMeters": 0.001, "rotationDegrees": 0.5, "morphWeight": 0.01},
              "sparseVsOfficialBaked": pose_comparison(reports[0], reports[2]),
              "defaultIkSparseVsOfficialBaked": pose_comparison(reports[1], reports[2]),
              "sparseVsDirectBezierSlerp": oracle_comparison(reports[0], tracks),
              "precedingKeyControlAlternative": oracle_comparison(reports[0], tracks, True),
              "bakedSelectedPostSolverRotationsVsSource": oracle_comparison(baked_selected, tracks),
              "directVsOfficialBaked": pose_comparison(reports[3], reports[2]),
              "directVsSparse": pose_comparison(reports[3], reports[0]),
              "directDefaultIkVsOfficialBaked": pose_comparison(reports[4], reports[2]),
              "directVsDirectBezierSlerp": oracle_comparison(reports[3], tracks),
              "shoulderConstraint": {report['motion']['mode']: shoulder_constraint(report) for report in reports},
              "omittedNonrotatableRotationConstraints": [item for item in reports[2]['motion']['constraints']
                  if 'RotationConstraint' in item['flags'] and not item['rotatable'] and not item['hasRotationCurve']],
              "performance": []}
    result['directAccuracyPassed'] = (result['directVsDirectBezierSlerp']['maxErrorDegrees'] <= 0.05
        and result['shoulderConstraint']['direct']['maxErrorDegrees'] <= 0.05
        and result['directVsDirectBezierSlerp']['checks'] >= 1000)
    for report in reports:
        motion = report["motion"]
        result["performance"].append({"mode": motion["mode"], "conversionMilliseconds": motion["conversionMilliseconds"],
            "unityRetainedBytesDelta": motion["unityAllocatedBytesAfterConversion"] - motion["unityAllocatedBytesBeforeConversion"],
            "processPeakWorkingSetBytes": motion["processPeakWorkingSetBytes"] or None})
    (args.artifacts / "vmd-comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = ["# Actual Windows Player VMD comparison", "", "All modes use the same pinned UMT, Gene, Whiplash, physics OFF and camera. This compares playback paths; it is not independent MMD-engine pose parity.", "",
             "| Mode | Conversion ms | Retained Unity delta MiB | Process peak MiB |", "|---|---:|---:|---:|"]
    for row in result["performance"]:
        peak = f'{row["processPeakWorkingSetBytes"]/1048576:.1f}' if row['processPeakWorkingSetBytes'] else 'unavailable'
        lines.append(f'| {row["mode"]} | {row["conversionMilliseconds"]:.1f} | {row["unityRetainedBytesDelta"]/1048576:.1f} | {peak} |')
    lines += ["", "Retained allocation snapshots are not peak conversion allocations. Mono returned zero for process peak, so that metric is unavailable. Single runs are not benchmarks.", ""]
    for key in ("sparseVsOfficialBaked", "defaultIkSparseVsOfficialBaked", "directVsOfficialBaked", "directVsSparse"):
        lines += [f"## {key}", "", f'Within diagnostic tolerances: {result[key]["parityWithinDiagnosticTolerance"]}', "",
                  "| Seconds | Max world error m | Max local angle deg | Max morph weight error |", "|---|---:|---:|---:|"]
        for row in result[key]["samples"]:
            lines.append(f'| {row["seconds"]:.7f} | {row["maxWorldPositionErrorMeters"]:.6f} | {row["maxLocalRotationErrorDegrees"]:.4f} | {row["maxMorphWeightError"]:.6f} |')
    direct = result["sparseVsDirectBezierSlerp"]
    baked_check = result["bakedSelectedPostSolverRotationsVsSource"]
    direct_check = result['directVsDirectBezierSlerp']
    lines += ["", "## Independent raw-VMD FK rotation check", "",
              f'{direct["checks"]} samples, maximum {direct["maxErrorDegrees"]:.6f} degrees; within 0.5 degrees: {direct["withinDiagnosticTolerance"]}.',
              f'Baked selected post-solver local rotations vs the same source: max {baked_check["maxErrorDegrees"]:.6f} degrees. This includes solver effects and between-baked-frame interpolation, so it is not a pure FK equality assertion.',
              "Only selected source FK rotations are checked, before constraints/IK. Missing tracks, morph appearance and physics are outside this oracle.",
              "Oracle v2 uses Babylon's rotation block (48/52/56/60), excluding first-block physics flags. The original first-block 3/7/11/15 oracle was incorrect for this VMD's physics extension; prior ~0.6-degree results from that oracle are withdrawn. The preceding-key first-block Saba variant remains recorded separately. No upstream executable was run.", ""]
    lines += ['## Direct quaternion and constraint checks', '',
              f'Direct evaluation vs independent raw VMD Bezier/Slerp: {direct_check["checks"]} checks, max {direct_check["maxErrorDegrees"]:.8f} degrees.',
              '', '| Mode | Gene shoulder inheritance max error deg |', '|---|---:|']
    for mode, check in result['shoulderConstraint'].items():
        lines.append(f'| {mode} | {check["maxErrorDegrees"]:.8f} |')
    lines += ['', 'The pinned bake omits rotation curves for these constrained non-Rotatable bones:', '']
    lines += ['- ' + item['name'] + ' -> ' + item['target'] for item in result['omittedNonrotatableRotationConstraints']]
    lines += ['', 'This identifies a source-level discrepancy in the reference bake. It does not establish full pose equivalence or validate every constraint/model.', '']
    (args.artifacts / "vmd-comparison.md").write_text("\n".join(lines), encoding="utf-8")
    print(json.dumps({"comparisonReport": str(args.artifacts / "vmd-comparison.md"),
                      "sparseParity": result["sparseVsOfficialBaked"]["parityWithinDiagnosticTolerance"],
                      "defaultIkParity": result["defaultIkSparseVsOfficialBaked"]["parityWithinDiagnosticTolerance"],
                      "sparseFkMaxDegrees": direct["maxErrorDegrees"],
                      "directFkMaxDegrees": direct_check["maxErrorDegrees"],
                      "directShoulderMaxDegrees": result['shoulderConstraint']['direct']['maxErrorDegrees']}))
    if args.require_direct_accuracy and not result['directAccuracyPassed']:
        raise SystemExit('Direct quaternion/constraint acceptance checks failed')


if __name__ == "__main__":
    main()
