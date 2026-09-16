#!/usr/bin/env bash
set -euo pipefail

test_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_dir=$(cd -- "$test_dir/../.." && pwd)
build_dir="$test_dir/obj"
module_dir="$build_dir/input"
bash "$repo_dir/src/LivePaper.Platform/KWin/Native/build.sh" "$module_dir"
cp "$repo_dir/src/LivePaper.Platform/KWin/PlasmaWallpaper/contents/ui/input/qmldir" "$module_dir/qmldir"
cp "$test_dir/input.qml" "$build_dir/input.qml"
qt_libexec=$(pkg-config --variable=libexecdir Qt6Core)
read -r -a qt_flags <<< "$(pkg-config --cflags --libs Qt6Quick Qt6WebEngineQuick Qt6Test)"
"$qt_libexec/moc" "$test_dir/MouseInputTests.cpp" -o "$build_dir/MouseInputTests.moc"
"${CXX:-c++}" -std=c++17 -fPIC -I"$build_dir" \
    "-DTEST_QML_PATH=\"$build_dir/input.qml\"" \
    "$test_dir/MouseInputTests.cpp" -o "$build_dir/MouseInputTests" "${qt_flags[@]}"
QT_QPA_PLATFORM=offscreen QT_QUICK_BACKEND=software "$build_dir/MouseInputTests" "$@"
