#!/usr/bin/env bash
# Packages the Linux-published binary into a single SupremeCommanderMapEditor-x86_64.AppImage.
# Assumes `dotnet publish -p:PublishProfile=linux-x64` has already produced the self-contained ELF
# at src/SupremeCommanderEditor.App/bin/publish/linux-x64/SupremeCommanderMapEditor.
#
# Output: src/SupremeCommanderEditor.App/bin/publish/linux-x64/SupremeCommanderMapEditor-x86_64.AppImage
#
# Usage : ./tools/build-appimage.sh           (downloads appimagetool on the fly if missing)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/src/SupremeCommanderEditor.App/bin/publish/linux-x64"
APP_NAME="SupremeCommanderMapEditor"
APP_BIN="$PUBLISH_DIR/$APP_NAME"
APPDIR="$PUBLISH_DIR/$APP_NAME.AppDir"
APPIMAGE_OUT="$PUBLISH_DIR/$APP_NAME-x86_64.AppImage"

if [[ ! -x "$APP_BIN" ]]; then
    echo "❌ Linux publish missing: $APP_BIN" >&2
    echo "   Run: dotnet publish src/SupremeCommanderEditor.App -p:PublishProfile=linux-x64" >&2
    exit 1
fi

# Fresh AppDir layout.
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp "$APP_BIN" "$APPDIR/usr/bin/$APP_NAME"
chmod +x "$APPDIR/usr/bin/$APP_NAME"

# Icon at AppDir root and under usr/share/icons (some launchers prefer one or the other).
cp "$REPO_ROOT/src/SupremeCommanderEditor.App/Assets/supcom.png" "$APPDIR/supcom-map-editor.png"
mkdir -p "$APPDIR/usr/share/icons/hicolor/48x48/apps"
cp "$REPO_ROOT/src/SupremeCommanderEditor.App/Assets/supcom.png" "$APPDIR/usr/share/icons/hicolor/48x48/apps/supcom-map-editor.png"

# Desktop entry (validated by appimagetool — invalid keys would fail the package).
cat > "$APPDIR/supcom-map-editor.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Supreme Commander Map Editor
Comment=Map editor for Supreme Commander 1 / Forged Alliance
Exec=$APP_NAME
Icon=supcom-map-editor
Categories=Game;Development;
Terminal=false
EOF

# AppRun = entrypoint script. AppImage tools require it at the AppDir root.
cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/SupremeCommanderMapEditor" "$@"
EOF
chmod +x "$APPDIR/AppRun"

# Fetch appimagetool if not installed system-wide.
APPIMAGETOOL="${APPIMAGETOOL:-}"
if [[ -z "$APPIMAGETOOL" ]]; then
    if command -v appimagetool >/dev/null 2>&1; then
        APPIMAGETOOL=appimagetool
    else
        TOOL_DIR="$PUBLISH_DIR/.tools"
        mkdir -p "$TOOL_DIR"
        APPIMAGETOOL="$TOOL_DIR/appimagetool-x86_64.AppImage"
        if [[ ! -x "$APPIMAGETOOL" ]]; then
            echo "→ downloading appimagetool…"
            curl -fsSL -o "$APPIMAGETOOL" \
                https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
            chmod +x "$APPIMAGETOOL"
        fi
    fi
fi

# Some CI / sandbox environments don't allow FUSE — use --appimage-extract-and-run to bypass.
ARCH=x86_64 "$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$APPIMAGE_OUT"
echo "✓ AppImage built: $APPIMAGE_OUT"
ls -lh "$APPIMAGE_OUT"
