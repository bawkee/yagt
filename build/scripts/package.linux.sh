#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

# ICU versions to support (Debian has no virtual package, must list all)
# Format: space-separated version numbers
ICU_VERSIONS="78 77 76 74 72 71 70 69 68 67 66 65 63"

arch=
appimage_arch=
target=
case "$RUNTIME" in
    linux-x64)
        arch=amd64
        appimage_arch=x86_64
        target=x86_64;;
    linux-arm64)
        arch=arm64
        appimage_arch=arm_aarch64
        target=aarch64;;
    *)
        echo "Unknown runtime $RUNTIME"
        exit 1;;
esac

APPIMAGETOOL_URL=https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

cd build

if [[ ! -f "appimagetool" ]]; then
    curl -o appimagetool -L "$APPIMAGETOOL_URL"
    chmod +x appimagetool
fi

rm -f YAGT/*.dbg
rm -f YAGT/*.pdb

mkdir -p YAGT.AppDir/opt
mkdir -p YAGT.AppDir/usr/share/metainfo
mkdir -p YAGT.AppDir/usr/share/applications

cp -r YAGT YAGT.AppDir/opt/yagt
desktop-file-install resources/_common/applications/yagt.desktop --dir YAGT.AppDir/usr/share/applications \
    --set-icon io.github.bawkee.yagt --set-key=Exec --set-value=AppRun
mv YAGT.AppDir/usr/share/applications/{yagt,io.github.bawkee.yagt}.desktop
cp resources/_common/icons/yagt.png YAGT.AppDir/io.github.bawkee.yagt.png
ln -rsf YAGT.AppDir/opt/yagt/yagt YAGT.AppDir/AppRun
ln -rsf YAGT.AppDir/usr/share/applications/io.github.bawkee.yagt.desktop YAGT.AppDir
cp resources/appimage/yagt.appdata.xml YAGT.AppDir/usr/share/metainfo/io.github.bawkee.yagt.appdata.xml

ARCH="$appimage_arch" ./appimagetool -v \
    -u "gh-releases-zsync|bawkee|yagt|latest|yagt-*.linux.$arch.AppImage.zsync" \
    YAGT.AppDir "yagt-$VERSION.linux.$arch.AppImage"

mkdir -p resources/deb/opt/yagt/
mkdir -p resources/deb/usr/bin
mkdir -p resources/deb/usr/share/applications
mkdir -p resources/deb/usr/share/icons
cp -f YAGT/* resources/deb/opt/yagt
ln -rsf resources/deb/opt/yagt/yagt resources/deb/usr/bin
cp -r resources/_common/applications resources/deb/usr/share
cp -r resources/_common/icons resources/deb/usr/share

# Calculate installed size in KB
installed_size=$(du -sk resources/deb | cut -f1)

# Generate ICU dependencies string for Debian
# Debian lacks libicu virtual package, must list all versions with OR operator
icu_deps="libicu"
for v in $ICU_VERSIONS; do
    icu_deps="$icu_deps | libicu$v"
done

# Update the control file (replace placeholder, not whole Depends line)
sed -i -e "s/^Version:.*/Version: $VERSION/" \
    -e "s/^Architecture:.*/Architecture: $arch/" \
    -e "s/^Installed-Size:.*/Installed-Size: $installed_size/" \
    -e "s/@ICU_DEPS@/$icu_deps/" \
    resources/deb/DEBIAN/control

# Build deb package with gzip compression
dpkg-deb -Zgzip --root-owner-group --build resources/deb "yagt_$VERSION-1_$arch.deb"

rpmbuild -bb --target="$target" resources/rpm/SPECS/build.spec --define "_topdir $(pwd)/resources/rpm" --define "_version $VERSION"
mv "resources/rpm/RPMS/$target/yagt-$VERSION-1.$target.rpm" ./
