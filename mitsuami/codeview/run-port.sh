#!/bin/sh
# Builds the code view's library and runs the port on it, with its arguments.
set -e
cd "$(dirname "$0")"
cargo build --lib
target=$(cargo metadata --format-version 1 --no-deps | sed -n 's/.*"target_directory":"\([^"]*\)".*/\1/p')
export MITSUAMI_FFI="$target/debug/libsourcegit_codeview.so"
[ "$(uname)" = Darwin ] && MITSUAMI_FFI="$target/debug/libsourcegit_codeview.dylib"
exec dotnet run --project ../SourceGit.Mitsuami.csproj -- "$@"
