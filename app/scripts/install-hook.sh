#!/usr/bin/env bash
#
# Install the iOS pre-push git hook as a symlink, so `git pull` keeps it
# in sync with the version in this repo. Idempotent.
#
# Usage:  bash app/scripts/install-hook.sh

set -e

REPO="$(git rev-parse --show-toplevel)"
SRC="${REPO}/app/scripts/pre-push"
DST="${REPO}/.git/hooks/pre-push"

if [ ! -f "$SRC" ]; then
    echo "Hook source missing at $SRC" >&2
    exit 1
fi

chmod +x "$SRC"

if [ -L "$DST" ] && [ "$(readlink "$DST")" = "$SRC" ]; then
    echo "Pre-push hook already installed at $DST"
    exit 0
fi

if [ -e "$DST" ]; then
    backup="${DST}.bak.$(date +%s)"
    mv "$DST" "$backup"
    echo "Backed up existing hook to $backup"
fi

ln -s "$SRC" "$DST"
echo "Pre-push hook installed: $DST -> $SRC"
echo "Bypass with: git push --no-verify"
