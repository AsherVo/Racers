#!/bin/sh
# Builds a universal (arm64 + x86_64) HelloSprite.app from the NativeAOT desktop host.
# No Catalyst and no macOS workload: just a NativeAOT executable, libSDL3, libEngineNative and an
# Info.plist. Run tools/build-native-macos.sh first.
# The result is ad-hoc signed; use a Developer ID identity and notarize it for distribution.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
NAME=HelloSprite
BUNDLE_ID=${BUNDLE_ID:-com.example.hellosprite}
VERSION=${VERSION:-0.1.0}
SIGN_IDENTITY=${SIGN_IDENTITY:--}
OUT="$ROOT/artifacts/macos"
APP="$OUT/$NAME.app"

for rid in osx-arm64 osx-x64; do
  dotnet publish "$ROOT/hosts/Desktop" -c Release -r "$rid" -o "$OUT/$rid" -v quiet -nologo
done

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# NativeAOT resolves P/Invokes next to the executable, so libSDL3 lives in Contents/MacOS.
lipo -create "$OUT/osx-arm64/$NAME" "$OUT/osx-x64/$NAME" -output "$APP/Contents/MacOS/$NAME"
lipo -create "$OUT/osx-arm64/libSDL3.dylib" "$OUT/osx-x64/libSDL3.dylib" -output "$APP/Contents/MacOS/libSDL3.dylib"
cp "$ROOT/artifacts/native/osx/libEngineNative.dylib" "$APP/Contents/MacOS/"   # already universal

# SDL_GetBasePath() returns Contents/Resources/ inside a bundle.
cp -R "$OUT/osx-arm64/Content" "$APP/Contents/Resources/Content"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>$NAME</string>
  <key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
  <key>CFBundleName</key><string>$NAME</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.games</string>
</dict>
</plist>
EOF

# Hardened runtime (required for notarization) enforces library validation, which rejects
# ad-hoc signatures, so only enable it when signing with a real identity.
if [ "$SIGN_IDENTITY" = "-" ]; then
  SIGN_FLAGS=""
else
  SIGN_FLAGS="--options runtime --timestamp"
fi
codesign --force $SIGN_FLAGS --sign "$SIGN_IDENTITY" "$APP/Contents/MacOS/libSDL3.dylib"
codesign --force $SIGN_FLAGS --sign "$SIGN_IDENTITY" "$APP/Contents/MacOS/libEngineNative.dylib"
codesign --force $SIGN_FLAGS --sign "$SIGN_IDENTITY" "$APP"

echo "Built $APP"
lipo -info "$APP/Contents/MacOS/$NAME"
