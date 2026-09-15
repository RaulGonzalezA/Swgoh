#!/usr/bin/env bash
set -euo pipefail

readonly max_lines=600
violations=0

while IFS= read -r -d '' file; do
  line_count=$(awk 'END { print NR }' "$file")
  if (( line_count > max_lines )); then
    printf '%s: %d lines (max %d)\n' "$file" "$line_count" "$max_lines" >&2
    violations=1
  fi
done < <(find src -type f \( -name '*.cs' -o -name '*.razor' \) -print0 | sort -z)

if grep -R --line-number --include='*.cs' 'GetRequiredService' src; then
  echo 'Service Locator usage via GetRequiredService is forbidden in production source.' >&2
  violations=1
fi

if (( violations != 0 )); then
  echo 'Architecture guard failed. Split oversized files and remove Service Locator usage before merge.' >&2
  exit 1
fi

echo 'Architecture guard passed: source files are <= 600 lines and production code has no GetRequiredService usage.'
