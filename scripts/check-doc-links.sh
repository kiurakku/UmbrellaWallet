#!/usr/bin/env bash
# Check relative markdown links in root + docs/ (+ LEGAL/, SECURITY/, .github/).
# Usage (repo root): bash scripts/check-doc-links.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

broken=0
checked=0

# Collect markdown files (skip archive noise if huge)
mapfile -t files < <(find . -type f -name '*.md' \
  ! -path './.git/*' \
  ! -path './docs/archive/*' \
  ! -path './node_modules/*' \
  ! -path './desktop/bin/*' \
  ! -path './desktop/obj/*' \
  | sort)

for f in "${files[@]}"; do
  dir="$(dirname "$f")"
  # Extract markdown links: ](path) excluding http(s), mailto, anchors-only, #
  while IFS= read -r raw; do
    link="${raw#*(}"
    link="${link%)}"
    # strip optional title "..." 
    link="${link%% \"*}"
    link="${link%% \'*}"
    case "$link" in
      http://*|https://*|mailto:*|\#*) continue ;;
      "") continue ;;
    esac
    # drop fragment
    path="${link%%#*}"
    [[ -z "$path" ]] && continue
    if [[ "$path" == /* ]]; then
      target=".$path"
    else
      target="$dir/$path"
    fi
    # normalize ..
    if command -v realpath >/dev/null 2>&1; then
      resolved="$(realpath -m "$target" 2>/dev/null || true)"
    else
      resolved="$target"
    fi
    checked=$((checked + 1))
    if [[ ! -e "$target" && ! -e "$resolved" ]]; then
      echo "BROKEN  $f  ->  $link"
      broken=$((broken + 1))
    fi
  done < <(grep -oE '\[[^]]*\]\([^)]+\)' "$f" || true)
done

echo "Checked ~$checked relative link refs across ${#files[@]} markdown files."
if [[ "$broken" -gt 0 ]]; then
  echo "Found $broken broken link(s)."
  exit 1
fi
echo "OK — no broken relative markdown links found."
