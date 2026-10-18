#!/usr/bin/env bash
# Aspire asigna puertos dinámicos: se descubren y se exportan a $GITHUB_ENV (BASE_URL, MAILPIT_URL).
set -euo pipefail
for _ in $(seq 1 90); do
  mailpit=$(docker ps -qf ancestor=axllent/mailpit | head -1)
  [ -n "$mailpit" ] && break
  sleep 2
done
echo "MAILPIT_URL=http://localhost:$(docker port "$mailpit" 8025/tcp | head -1 | sed 's/.*://')" >> "$GITHUB_ENV"
for _ in $(seq 1 90); do
  for p in $(ss -ltnH | awk '{print $4}' | sed 's/.*://' | sort -un); do
    if curl -fs -m 2 "http://localhost:$p/" 2>/dev/null | grep -q 'Diligencia - Backoffice'; then
      echo "BASE_URL=http://localhost:$p" >> "$GITHUB_ENV"
      exit 0
    fi
  done
  sleep 2
done
echo "No se encontró el backoffice" >&2
exit 1
