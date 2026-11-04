"""Package the complete official expanded Fixed Version runtime, without pruning files."""
import hashlib
import json
import pathlib
import sys
import zipfile

source, output = map(pathlib.Path, sys.argv[1:3])
output.mkdir(parents=True, exist_ok=True)
files = []
with zipfile.ZipFile(output / 'Browser.zip', 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path in sorted(source.rglob('*')):
        if path.is_symlink():
            raise ValueError('Runtime links are not allowed')
        if not path.is_file():
            continue
        name = path.relative_to(source).as_posix()
        files.append({'Path': name, 'Length': path.stat().st_size, 'Sha256': hashlib.file_digest(path.open('rb'), 'sha256').hexdigest()})
        archive.write(path, name)
(output / 'Browser.manifest.json').write_text(json.dumps({'Version': '156.0.4314.58', 'Files': files}, separators=(',', ':')), encoding='utf-8')
print(f'Packaged {len(files)} runtime files')
