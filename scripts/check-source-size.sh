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

if (( violations != 0 )); then
  echo 'Source files above the 600-line limit must be split before merge.' >&2
  exit 1
fi

echo 'Source-size guard passed: all .cs and .razor files are <= 600 lines.'
