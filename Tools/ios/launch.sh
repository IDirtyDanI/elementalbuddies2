#!/bin/zsh
# App auf dem iPhone (neu) starten; laufende Instanz wird beendet.
# Aufruf:  Tools/ios/launch.sh      (Gerät: IOS_DEVICE, Standard siehe common.sh)
# Hinweis: iPhone muss entsperrt sein; beim ersten Start dem Entwickler unter Einstellungen > Allgemein >
# VPN & Geräteverwaltung vertrauen.
set -euo pipefail
source "${0:A:h}/common.sh"
xcrun devicectl device process launch --terminate-existing --device "$DEVICE" "$BUNDLE_ID"
