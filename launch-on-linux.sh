#!/bin/sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec pwsh -NoLogo -NoProfile -File ./Start-AMAG23.ps1 "$@"
