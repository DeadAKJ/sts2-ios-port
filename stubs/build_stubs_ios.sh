#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

SDK_PATH=$(xcrun --sdk iphoneos --show-sdk-path)
OUT_DIR="$SCRIPT_DIR/out/ios-arm64"
mkdir -p "$OUT_DIR"

echo "Compiling Steamworks stub for iOS arm64..."
clang -arch arm64 \
    -isysroot "$SDK_PATH" \
    -miphoneos-version-min=15.0 \
    -dynamiclib \
    -O2 \
    -o "$OUT_DIR/libsteam_api.dylib" \
    steam_stub.c steam_stub_auto.c \
    -install_name @rpath/libsteam_api.dylib

echo "Compiling Sentry stub for iOS arm64..."
clang -arch arm64 \
    -isysroot "$SDK_PATH" \
    -miphoneos-version-min=15.0 \
    -dynamiclib \
    -O2 \
    -o "$OUT_DIR/libsentry.dylib" \
    sentry_stub.c \
    -install_name @rpath/libsentry.dylib

echo "Stubs compiled successfully to $OUT_DIR:"
ls -lh "$OUT_DIR"
