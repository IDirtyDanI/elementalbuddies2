#!/bin/zsh
# Xcode-Projekt aus Unity (TesterBuild.BuildIos) signieren, bauen und aufs iPhone installieren.
# Aufruf:  Tools/ios/build_install.sh [--no-install] <xcode-projekt-ordner>
#   z. B.  Tools/ios/build_install.sh /Users/daniel/Desktop/BuddyTD/Build/iOS/Xcode-v16
# --no-install: nur bauen. Gerät: IOS_DEVICE (Standard siehe common.sh).
# Voraussetzung: Apple-ID in Xcode > Settings > Accounts angemeldet; iPhone verbunden (Personal Team braucht das
# registrierte Gerät für das Provisioning-Profil). Log: <ordner>/xcodebuild.log
set -euo pipefail
source "${0:A:h}/common.sh"

install=1
if [[ "${1:-}" == "--no-install" ]]; then install=0; shift; fi
if [[ $# -lt 1 || ! -d "$1" ]]; then
  print -u2 "Aufruf: $0 [--no-install] <xcode-projekt-ordner>"; exit 2
fi
dir="${1:A}"
log="$dir/xcodebuild.log"

# CocoaPods: normalerweise erledigt der Resolver das schon beim Unity-Build
if [[ -f "$dir/Podfile" && ! -d "$dir/Unity-iPhone.xcworkspace" ]]; then
  print "pod install …"
  (cd "$dir" && pod install)
fi

if [[ -d "$dir/Unity-iPhone.xcworkspace" ]]; then
  container=(-workspace "$dir/Unity-iPhone.xcworkspace")
else
  container=(-project "$dir/Unity-iPhone.xcodeproj")
fi

print "xcodebuild (Debug, Team $TEAM_ID) … Log: $log"
if ! xcodebuild "${container[@]}" -scheme Unity-iPhone -configuration Debug \
    -destination 'generic/platform=iOS' -derivedDataPath "$dir/DerivedData" \
    -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM_ID" CODE_SIGN_STYLE=Automatic \
    build > "$log" 2>&1; then
  print -u2 "xcodebuild fehlgeschlagen. Fehler:"
  grep -E "error:|\*\* BUILD FAILED" "$log" | sort -u | head -30 >&2 || true
  print -u2 -- "--- letzte Zeilen ---"
  tail -40 "$log" >&2
  exit 1
fi

app=$(find "$dir/DerivedData/Build/Products/Debug-iphoneos" -maxdepth 1 -name '*.app' -type d | head -1)
if [[ -z "$app" ]]; then print -u2 "Keine .app unter DerivedData gefunden"; exit 1; fi
print "Gebaut: $app"

if (( install )); then
  print "Installiere auf $DEVICE …"
  xcrun devicectl device install app --device "$DEVICE" "$app"
  print "Fertig. Starten: ${0:A:h}/launch.sh"
fi
