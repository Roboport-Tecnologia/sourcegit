# The code view

SourceGit's code view is an embedded [bemtvi](https://github.com/bemtvi/bemtvi). Its
features are Lua plugins here, so they live and change with the port.

- `editor/` is the editor's whole config (`Config::Dir`). Plugins are under
  `editor/pack/sourcegit/start/`, and load by themselves. It runs in bemtvi's single-file lockdown with no file
  (`SessionOptions::views_only`): no user config or shada, no plugin manager, language
  servers or commands that run code, and no file opens. A plugin shows a view, with
  `view:mount({ here = true })`. The port copies it next to the app
  as `codeview-editor/`, without the specs.
- `src/lib.rs` is the C ABI, in one library with mitsuami's and extras': two copies of
  mitsuami in one process would be two cores. It carries calls to plugins as JSON, and
  what they send with `btv.host.send`, and knows no feature.
- `../Views/CodeView.cs` is its C# side; `CodeView.DiffJson` makes what `sourcegit-diff`
  takes.
- `fixtures/` has what the app sends, for the specs, the ABI tests and the window below.

## A file

`sourcegit-file` shows a revision's file (the commit's Files tab, a file's history): its
text in a view with its filetype, so the editor highlights it itself, with no copying.

## Blame

`sourcegit-blame` is a file's view with a hash, author and time heading each run of a
commit's lines, in `gutter_text`. That column isn't clickable, so a click selects the
line: every line of its commit lights up, the footer names it, and its buttons, or <CR> and
`gb`, go to the commit or blame the file there. Upstream's click and Ctrl-click on its
margin do those two.

## The diff

The port always asks git for the whole file as context (upstream's `UseFullTextDiff`,
forced on in `Program.cs`), so `sourcegit-diff` has both versions from one call, which
agrees with git's whitespace and CRLF options:

- **Layouts:** unified and side by side are two layouts of the same lines. Context lines
  and "show all" rebuild them with no call to git (`layout`).
- **Colours:** each version is parsed whole, off-buffer (`btv.treesitter.highlight`), and
  the spans are copied onto its lines. Hunks aren't code a grammar can read. bemtvi's
  parse has a 50 ms deadline, so a version past about 350 KB shows plain, and above
  `max_colored_bytes` it isn't tried. The changed characters show `DiffText` alone: bemtvi
  paints one group per cell.
- **Scroll lock:** panes have the same rows, with filler where one side has fewer.

The diff view's toolbar sets context, all lines and side by side.

## Preferences

Preferences' Code View page sets the mode (modeless, vim, helix), font, size, colorscheme,
tab width and relative line numbers. `CodeView` sends them to `sourcegit-settings` as each editor starts and
as they change. In a diff, relative numbers go beside the file's old and new ones.

"Fetch and build syntax highlighting" is off by default, as it downloads code to build:
`sourcegit-grammars` then runs `:TSInstall` for a language with no grammar, as a diff in it
shows, and the diff takes its colours when the grammar is ready. Grammars go in bemtvi's data directory, shared with the user's own bemtvi. The first
install also downloads bemtvi's pinned Zig compiler. The app remembers what it tried, so
a failure isn't retried for every file until the next launch.

## Trying a plugin by hand

In a window, the real editor and plugins with a button per fixture, buttons for what the
Code View preferences set, and what the plugins said back under them (`:q` closes it). The Lua isn't compiled in, so run it again after
an edit:

```sh
cargo run --example harness [fixtures/edit.json]
```

In a terminal, with no app around it:

```sh
BEMTVI_CONFIG=editor cargo run --manifest-path /mnt/data2/david/work/nxvim-ts-grammars/Cargo.toml -p bemtvi
:SourceGitDiff fixtures/edit.json
```

The whole port on this library: `./run-port.sh [repository]`.

## Tests

`./test.sh` runs them all, without opening a window:

- `editor/pack/sourcegit/start/<plugin>/test/*_spec.lua`, on a real editor
  (`bemtvi --test-plugin <plugin>`): what a plugin shows, and its keys;
- `tests/ffi.rs`, the ABI on mitsuami's headless backend: the real plugins, calls and
  their errors, events, quitting, destroying. No pixels: headless has no surface.

Nothing checks `CodeView.DiffJson` against the plugin yet, as the port has no test
project. The plugin raises on what it doesn't understand, naming it.

## Adding a plugin

Add `editor/pack/sourcegit/start/<name>/`, with `lua/<name>/init.lua`, specs and a
fixture, and call it with `CodeView`'s `Call` (`require(module)[function](json)`).

## What belongs in bemtvi

What a plugin or the ABI would have to work around goes in bemtvi instead
(the `nxvim-ts-grammars` worktree, on its branch, not `main`), with its tests and docs: `views_only`, `mount{ here }`, `exec_lua_with` and
building the `Editor` outside a view all came from here.

## Dependencies

`../mitsuami` and `../mitsuami-extras` are sibling checkouts. bemtvi is the worktree
`/mnt/data2/david/work/nxvim-ts-grammars`, by an absolute path for now, which `edge.yml`'s
checkout doesn't have. The
`[patch]` in `Cargo.toml` is a stopgap: bemtvi pins a GitHub revision of mitsuami 1.3.7,
and builds against the sibling's 2.0.0 here. It goes when bemtvi takes mitsuami 2 by a
caret.
