# Gemeinsame Werte für die iOS-Skripte (wird per „source“ eingebunden, nicht direkt ausführen).
# Gerät per Umgebung überschreibbar: IOS_DEVICE=<CoreDevice-ID|Name> ./launch.sh
DEVICE="${IOS_DEVICE:-D5BE5F22-361D-54DB-AEB0-2D24813CF02C}"
BUNDLE_ID="com.buddytd.elementalbuddies"
TEAM_ID="8UZ2YT2TA7"
# Zustand von cmd.sh (Zeilenzähler für testout.txt)
STATE_DIR="${IOS_STATE_DIR:-$HOME/Library/Caches/eb-ios}"
export LANG=en_US.UTF-8
export PATH="/opt/homebrew/bin:/usr/local/bin:$PATH"

# Datei im App-Container (Documents = Application.persistentDataPath) auf den Mac holen
pull_container() { # <pfad-im-container> <lokaler-pfad>
  xcrun devicectl device copy from --device "$DEVICE" --domain-type appDataContainer \
    --domain-identifier "$BUNDLE_ID" --source "$1" --destination "$2" --quiet
}
