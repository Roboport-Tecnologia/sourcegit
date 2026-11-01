#!/bin/sh
# Every automated test of the code view: each plugin's specs, then the C ABI's on
# mitsuami's headless backend. Neither opens a window.
set -e
cd "$(dirname "$0")"
# The bemtvi worktree the library embeds: the plugins use what it adds. NXVIM runs the
# specs on another checkout.
nxvim=${NXVIM:-/mnt/data2/david/work/nxvim-ts-grammars}
cargo build --manifest-path "$nxvim/Cargo.toml" -p bemtvi
bemtvi=$(cargo metadata --manifest-path "$nxvim/Cargo.toml" --format-version 1 --no-deps \
  | sed -n 's/.*"target_directory":"\([^"]*\)".*/\1/p')/debug/bemtvi
for plugin in editor/pack/sourcegit/start/*/; do
  "$bemtvi" --test-plugin "$plugin"
done
cargo test --test ffi
