#!/usr/bin/env python3
"""Create the user-facing GitHub Release ZIP using only the Python standard library."""
from __future__ import annotations

import argparse
from hashlib import sha256
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo
import re

ROOT = Path(__file__).resolve().parents[1]
FILES = (
    "VERSION", "README.md", "ACKNOWLEDGMENTS.md", "PROTOCOL.md", "CONTRIBUTING.md", "SECURITY.md", "LICENSE",
    "Start-AMAG23.ps1", "Launch-on-Windows.cmd", "launch-on-linux.sh",
    "lib/Native.cs", "lib/Protocol.cs", "lib/LocalServer.cs",
    "ui/index.html", "ui/app.css", "ui/theme.js", "ui/app.js", "ui/codec.js",
    "linux/70-amag23.rules", "tests/SelfTests.cs", "tests/Test-Protocol.ps1",
    "docs/app-light.png", "docs/app-dark.png",
)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='v'+(ROOT/'VERSION').read_text(encoding='utf-8').strip())
    args = parser.parse_args()
    ver = (ROOT/'VERSION').read_text(encoding='utf-8').strip()
    if not re.fullmatch(r'\d+\.\d+\.\d+', ver) or args.version != 'v'+ver:
        parser.error('Tag must match VERSION exactly (v' + ver + ')')
    dest = ROOT/'dist'
    dest.mkdir(exist_ok=True)
    output = dest/f'AMAG23-Classic-v{ver}.zip'
    prefix = f'AMAG23-Classic-v{ver}/'
    with ZipFile(output, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
        for rel in FILES:
            source = ROOT/rel
            if not source.is_file():
                raise FileNotFoundError(f'Required release file missing: {source}')
            info = ZipInfo(prefix+rel)
            info.compress_type = ZIP_DEFLATED
            # Unix executable bit for the optional shell launcher, ordinary file otherwise.
            mode = 0o100755 if rel == 'launch-on-linux.sh' else 0o100644
            info.external_attr = mode << 16
            archive.writestr(info, source.read_bytes(), compress_type=ZIP_DEFLATED, compresslevel=9)
    digest = sha256(output.read_bytes()).hexdigest()
    (dest/'SHA256SUMS.txt').write_text(f'{digest}  {output.name}\n', encoding='utf-8')
    print(f'Created {output} ({output.stat().st_size:,} bytes)')
    print(f'SHA-256: {digest}')


if __name__ == '__main__':
    main()
