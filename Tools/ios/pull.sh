#!/bin/zsh
# Datei aus dem App-Container auf den Mac holen (z. B. Bildschirmfotos von „shot“).
# Aufruf:  Tools/ios/pull.sh <pfad-im-container> <lokaler-pfad>
#   z. B.  Tools/ios/pull.sh Documents/s1.png ~/Desktop/s1.png
# Documents/ = Application.persistentDataPath. Gerät: IOS_DEVICE (Standard siehe common.sh).
set -euo pipefail
source "${0:A:h}/common.sh"
if [[ $# -lt 2 ]]; then print -u2 "Aufruf: $0 <pfad-im-container> <lokaler-pfad>"; exit 2; fi
pull_container "$1" "$2"
print "$2"
