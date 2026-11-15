#!/bin/zsh
# Befehle an MobileTestRemote im laufenden Development-Build schicken und neue Antworten ausgeben.
# Aufruf:  Tools/ios/cmd.sh "<befehle>" [warte-sekunden=3]
#   mehrere Befehle zeilenweise:  Tools/ios/cmd.sh $'state\nview' 5
#   nur neue Antworten abholen:   Tools/ios/cmd.sh "" 0
#   Bildschirmfoto:               Tools/ios/cmd.sh "shot s1.png"  → danach pull.sh Documents/s1.png ./s1.png
#     (relativer Name landet auf Mobilgeräten in persistentDataPath = Documents/)
# Dateien im App-Container: Documents/testcmd.txt (App liest alle 0,5 s und löscht), Documents/testout.txt (Antworten).
# Die App muss im Vordergrund laufen. Ein noch nicht abgeholtes testcmd.txt wird überschrieben.
set -euo pipefail
source "${0:A:h}/common.sh"

if [[ $# -lt 1 ]]; then print -u2 "Aufruf: $0 \"<befehle>\" [warte-sekunden]"; exit 2; fi
cmds="$1"
wait_s="${2:-3}"
mkdir -p "$STATE_DIR"
counter="$STATE_DIR/testout.lines"
out="$STATE_DIR/testout.txt"

if [[ -n "$cmds" ]]; then
  tmpdir=$(mktemp -d)
  trap 'rm -rf "$tmpdir"' EXIT
  # Quelldatei heißt wie das Ziel, falls devicectl --destination als Ordner auslegt
  print -r -- "$cmds" > "$tmpdir/testcmd.txt"
  xcrun devicectl device copy to --device "$DEVICE" --domain-type appDataContainer \
    --domain-identifier "$BUNDLE_ID" --source "$tmpdir/testcmd.txt" --destination Documents/testcmd.txt --quiet
fi

if (( wait_s > 0 )); then sleep "$wait_s"; fi

rm -f "$out"
if ! pull_container Documents/testout.txt "$out" || [[ ! -f "$out" ]]; then
  print -u2 "testout.txt nicht lesbar (App gestartet? Development-Build?)"; exit 1
fi

seen=0
if [[ -f "$counter" ]]; then seen=$(<"$counter"); fi
total=$(wc -l < "$out" | tr -d ' ')
if (( total < seen )); then seen=0; fi   # Datei neu (App neu installiert/Daten gelöscht)
tail -n +$((seen + 1)) "$out"
print -r -- "$total" > "$counter"
