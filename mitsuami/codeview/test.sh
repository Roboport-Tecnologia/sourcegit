#!/bin/sh
# Every automated test of the code view: each plugin's specs, the C ABI's on mitsuami's
# headless backend, then the port's C# views on it, with real git. None opens a window.
set -e
cd "$(dirname "$0")"
# The bemtvi checkout the library embeds (`../../../bemtvi`): the plugins use what it adds.
# BEMTVI runs the specs on another checkout.
checkout=${BEMTVI:-../../../bemtvi}
cargo build --manifest-path "$checkout/Cargo.toml" -p bemtvi
bemtvi=$(cargo metadata --manifest-path "$checkout/Cargo.toml" --format-version 1 --no-deps \
  | sed -n 's/.*"target_directory":"\([^"]*\)".*/\1/p')/debug/bemtvi
for plugin in editor/pack/sourcegit/start/*/; do
  "$bemtvi" --test-plugin "$plugin"
done
cargo test --test ffi
# `run-port.sh` builds the library again without the headless host.
cargo build --lib --features test-host
target=$(cargo metadata --format-version 1 --no-deps | sed -n 's/.*"target_directory":"\([^"]*\)".*/\1/p')
library="$target/debug/libsourcegit_codeview.so"
[ "$(uname)" = Darwin ] && library="$target/debug/libsourcegit_codeview.dylib"
MITSUAMI_FFI="$library" dotnet run --project ../tests/SourceGit.Mitsuami.Tests.csproj
