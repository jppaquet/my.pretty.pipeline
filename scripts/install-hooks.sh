#!/usr/bin/env bash
#
# Install repo git hooks as symlinks so `git pull` keeps them in sync with
# the version in this repo. Idempotent.
#
# Usage:  bash scripts/install-hooks.sh

set -e

REPO="$(git rev-parse --show-toplevel)"

install_hook() {
    local name="$1"
    local src="${REPO}/scripts/${name}"
    local dst="${REPO}/.git/hooks/${name}"

    if [ ! -f "$src" ]; then
        echo "Hook source missing at $src" >&2
        exit 1
    fi
    chmod +x "$src"

    if [ -L "$dst" ] && [ "$(readlink "$dst")" = "$src" ]; then
        echo "✓ ${name} already linked"
        return
    fi
    if [ -e "$dst" ]; then
        local backup="${dst}.bak.$(date +%s)"
        mv "$dst" "$backup"
        echo "  backed up existing ${name} → ${backup}"
    fi
    ln -s "$src" "$dst"
    echo "✓ ${name} installed: ${dst} → ${src}"
}

install_hook pre-push

# Sanity: warn (don't fail) on missing tools so the user knows what their
# next push will skip.
missing=()
command -v gitleaks  >/dev/null 2>&1 || missing+=("gitleaks (brew install gitleaks)")
command -v dotnet    >/dev/null 2>&1 || missing+=("dotnet (https://dotnet.microsoft.com/download)")
command -v bicep     >/dev/null 2>&1 || command -v az >/dev/null 2>&1 || missing+=("bicep or az (brew install azure-cli)")
command -v swiftlint >/dev/null 2>&1 || missing+=("swiftlint (brew install swiftlint)")

if [ "${#missing[@]}" -gt 0 ]; then
    echo
    echo "Missing tools — pre-push will skip the corresponding checks:"
    for m in "${missing[@]}"; do
        echo "  - $m"
    done
fi

echo
echo "Bypass once with: git push --no-verify"
