"""Read-only Windows PE / notice inventory. No DLL is loaded by this script."""
import argparse
import hashlib
import json
import struct
from pathlib import Path


def inspect_pe(path):
    data = path.read_bytes()
    pe, = struct.unpack_from('<I', data, 60)
    if data[:2] != b'MZ' or data[pe:pe+4] != b'PE\0\0':
        raise ValueError(f'Not a PE file: {path}')
    machine, sections = struct.unpack_from('<HH', data, pe+4)
    optional_size, = struct.unpack_from('<H', data, pe+20)
    optional = pe+24
    magic, = struct.unpack_from('<H', data, optional)
    directories = optional + (112 if magic == 0x20b else 96)
    section_start = optional+optional_size
    def offset(rva):
        for i in range(sections):
            virtual_size, address, raw_size, raw = struct.unpack_from('<IIII', data, section_start+i*40+8)
            if address <= rva < address+max(virtual_size, raw_size):
                return raw+rva-address
        raise ValueError(f'Unmapped RVA {rva:x}')
    def name(rva):
        start = offset(rva)
        return data[start:data.index(b'\0', start)].decode('ascii')
    exports = []
    export_rva, = struct.unpack_from('<I', data, directories)
    if export_rva:
        export = offset(export_rva)
        count, = struct.unpack_from('<I', data, export+24)
        names_rva, = struct.unpack_from('<I', data, export+32)
        names = offset(names_rva)
        exports = [name(struct.unpack_from('<I', data, names+4*i)[0]) for i in range(count)]
    imports = []
    import_rva, = struct.unpack_from('<I', data, directories+8)
    if import_rva:
        descriptor = offset(import_rva)
        while any(data[descriptor:descriptor+20]):
            imports.append(name(struct.unpack_from('<I', data, descriptor+12)[0]))
            descriptor += 20
    return {'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data),
            'machine': hex(machine), 'x64': machine == 0x8664, 'imports': imports, 'exports': exports}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--player', required=True)
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    root = Path(args.player)
    files = [root/'MmdStudioPoC.exe', root/'UnityPlayer.dll',
             root/'MmdStudioPoC_Data/Plugins/x86_64/UMTNativePlugin.dll']
    if (root/'GameAssembly.dll').exists():
        files.append(root/'GameAssembly.dll')
    inventory = {p.relative_to(root).as_posix(): inspect_pe(p) for p in files}
    plugin = inventory['MmdStudioPoC_Data/Plugins/x86_64/UMTNativePlugin.dll']
    required_exports = {'MMDBulletPhysicsCreate', 'MMDBulletPhysicsDestroy', 'MMDBulletPhysicsStepSimulation'}
    notices = list((root/'ThirdPartyNotices').rglob('*'))
    notice_files = [p for p in notices if p.is_file()]
    required_notices = {'LICENSE.md', 'Third Party Notices.md', 'COPYING', 'AUTHORS', 'PROVENANCE.md'}
    result = {'files': inventory, 'requiredExportsPresent': required_exports.issubset(plugin['exports']),
              'noticeFiles': [str(p.relative_to(root)) for p in notice_files],
              'requiredNoticesPresent': required_notices.issubset({p.name for p in notice_files}),
              'limit': 'Static inventory plus same-PC runtime tests; not a clean-machine dependency or complete release audit.'}
    result['success'] = all(p['x64'] for p in inventory.values()) and result['requiredExportsPresent'] and result['requiredNoticesPresent']
    Path(args.report).write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'success': result['success'], 'imports': plugin['imports'], 'exports': len(plugin['exports']), 'notices': len(notice_files)}))
    if not result['success']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
