#!/bin/sh
# macOS/Linux equivalent of build_plugin.cmd. Usage: ./build_plugin.sh /path/to/ndk
set -e
NDK="${1:-$ANDROID_NDK_ROOT}"
[ -n "$NDK" ] || { echo "Usage: $0 /path/to/ndk"; exit 1; }
HOST=$(ls "$NDK/toolchains/llvm/prebuilt" | head -1)
CLANG="$NDK/toolchains/llvm/prebuilt/$HOST/bin/clang++"
cd "$(dirname "$0")"
for pair in arm64-v8a:aarch64-linux-android29 armeabi-v7a:armv7a-linux-androideabi29; do
  abi=${pair%%:*}; target=${pair#*:}
  mkdir -p "../libs/$abi"
  "$CLANG" --target=$target -shared -fPIC -O2 -Wall -s -static-libstdc++ \
    -Wl,-z,max-page-size=16384 -Wl,--build-id \
    -o "../libs/$abi/libSoundStageNative.so" main.cpp Filter.cpp -lm
  echo "built ../libs/$abi/libSoundStageNative.so"
done
