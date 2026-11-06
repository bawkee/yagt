#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

mkdir -p YAGT.app/Contents/Resources
mv YAGT YAGT.app/Contents/MacOS
cp resources/app/App.icns YAGT.app/Contents/Resources/App.icns
sed "s/YAGT_VERSION/$VERSION/g" resources/app/App.plist > YAGT.app/Contents/Info.plist
rm -rf YAGT.app/Contents/MacOS/YAGT.dsym
rm -f YAGT.app/Contents/MacOS/*.pdb

case "$RUNTIME" in
  osx-arm64) ARCH=arm64 ;;
  osx-x64)   ARCH=x86_64 ;;
  *) echo "Unsupported RUNTIME: $RUNTIME" >&2; exit 1 ;;
esac

clang -arch "$ARCH" ../tools/setsid-macos/setsid.c -o YAGT.app/Contents/MacOS/setsid -mmacosx-version-min=13.0
codesign --force --deep --sign - YAGT.app
codesign --verify --deep --strict --verbose=2 YAGT.app

zip "yagt_$VERSION.$RUNTIME.zip" -r YAGT.app
