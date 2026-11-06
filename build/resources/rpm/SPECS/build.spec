Name: yagt
Version: %_version
Release: 1
Summary: Yet Another Git Tool
License: MIT
URL: https://github.com/bawkee/yagt
Source: https://github.com/bawkee/yagt/archive/refs/tags/v%_version.tar.gz
Requires: libX11.so.6()(%{__isa_bits}bit)
Requires: libSM.so.6()(%{__isa_bits}bit)
Requires: libicu
Requires: xdg-utils

%define _build_id_links none

%description
Yet Another Git Tool

%install
mkdir -p %{buildroot}/opt/yagt
mkdir -p %{buildroot}/%{_bindir}
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons
cp -f %{_topdir}/../../YAGT/* %{buildroot}/opt/yagt/
ln -rsf %{buildroot}/opt/yagt/yagt %{buildroot}/%{_bindir}
cp -r %{_topdir}/../_common/applications %{buildroot}/%{_datadir}
cp -r %{_topdir}/../_common/icons %{buildroot}/%{_datadir}
chmod 755 -R %{buildroot}/opt/yagt
chmod 755 %{buildroot}/%{_datadir}/applications/yagt.desktop

%files
%dir /opt/yagt/
/opt/yagt/*
/usr/share/applications/yagt.desktop
/usr/share/icons/*
%{_bindir}/yagt

%changelog
# skip
