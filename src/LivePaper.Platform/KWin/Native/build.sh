#!/usr/bin/env bash
set -euo pipefail

source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
build_dir=$1
mkdir -p -- "$build_dir"
input_version=$(cat "$source_dir/MouseInput.h" "$source_dir/MouseInput.cpp" "$source_dir/Plugin.cpp" "$source_dir/build.sh" | sha256sum | cut -d ' ' -f 1)
printf '%s\n' "$input_version" > "$build_dir/input-version.txt"
printf '#define LIVEPAPER_INPUT_VERSION "%s"\n' "$input_version" > "$build_dir/InputVersion.h"
qt_libexec=$(pkg-config --variable=libexecdir Qt6Core)
read -r -a qt_includes <<< "$(pkg-config --cflags Qt6Quick Qt6Qml)"
"$qt_libexec/moc" "${qt_includes[@]}" "$source_dir/MouseInput.h" -o "$build_dir/moc_MouseInput.cpp"
"$qt_libexec/moc" "${qt_includes[@]}" "$source_dir/Plugin.cpp" -o "$build_dir/Plugin.moc"
read -r -a qt_flags <<< "$(pkg-config --cflags --libs Qt6Quick Qt6Qml)"
"${CXX:-c++}" -std=c++17 -shared -fPIC -O2 -Wall -Wextra \
    -I"$build_dir" -I"$source_dir" \
    "$source_dir/MouseInput.cpp" "$source_dir/Plugin.cpp" "$build_dir/moc_MouseInput.cpp" \
    -o "$build_dir/liblivepaperinput.so" "${qt_flags[@]}"
