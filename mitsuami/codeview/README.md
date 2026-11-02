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

## One editor per pane

A pane (`CodePane`) keeps its editor from one file to the next, and hides it for what isn't
text (an image, a binary), so switching files doesn't start an editor each time. Windows
each have their own. An editor only hears of its own grammar installs, so `CodeView` passes
`grammar_ready` on to the others (`sourcegit-grammars`' `ready`).

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

The diff view's toolbar is upstream's: first, previous, next and last change with where
the cursor is among them, context, all lines, syntax, word wrap (with one pane), hidden
symbols, side by side, ignoring whitespace and the merge tool. Only ignoring whitespace asks
git again. Upstream's shortcuts for them (Ctrl+Alt+Up…) aren't ported.

A local change's Stage, Unstage and Discard are in the editor's right-click menu
(`btv.contextmenu`), and on `gs`, `gu` and `gx`. They act on what's selected, or the run of
changes under the cursor. The app says which it can do (`actions`, with its labels, so
they're translated), the plugin says which lines (`chunk`), and
`DiffView.ApplyChunk` makes upstream's patch of them for `git apply`. The diff then shows
again with the same path, which keeps the cursor where it was.

The menu is hard to find, so a diff with `actions` shows a float at the top right saying so.
The first mouse click, anywhere, ends it for as long as that editor lives. It's a `btv.view`
float, not `btv.ui.float`, which has no top-right placement. Its sentences are English; the
labels in them are the app's.

## The merge editor

`sourcegit-conflict` shows a conflicted file's two sides over the result, in three panes of one
editor that line up row for row and scroll together. `MergeEditor.cs` owns the window and uses
upstream's `MergeConflictEditor` as is: it parses the file, holds what's chosen, and saves. Each
choice makes it give new lines, which `show` puts in the panes, keeping the cursor.

Upstream floats buttons over the chunk under the pointer. Nothing native may draw over the
editor, so the choices are native buttons above it, which act on the conflict the cursor is in
(`conflict_at`), the right-click menu, and keys: `gm` mine, `gt` theirs, `gb` and `gB` both,
`gu` undo, `]c` and `[c` to move. The result isn't edited by hand, as upstream's isn't.
Closing the window asks first once something's been chosen (`Window.CanClose`).

Mine's lines are signed `-` on a faint red and theirs `+` on a faint green. In the conflict the
cursor is in those are stronger, and its markers and empty rows have a very subtle grey: a click on
the toolbar leaves the editor with no cursor to show it. Other rows have no background.

Going to a conflict puts it at the top of the panes, as `zt`.

Each pane is parsed for colours as a file of its own, without the markers and filler. The
result with a conflict open holds both sides, so its colours there are only as good as the
grammar's recovery.

## The commit message

`sourcegit-message` edits a commit message, in the local changes' box and the interactive
rebase's sheet (`MessageEditor`). This editor is `single_file`, on an empty `COMMIT_EDITMSG`
in a temporary directory of its own, as an editor that edits needs a file. The app owns the
text: what's typed goes to it with each key, and its changes (a template, a commit clearing it)
replace the buffer. Each text the app sends has a revision, and what was typed on an older one
is dropped. So nothing reads the file, `<C-s>` stays freed, and the buffer is never modified,
so `:q` doesn't ask; it starts a new editor on the same text.

The buffer's filetype is `gitcommit`, set by the plugin, so `sourcegit-grammars` installs that
grammar if the preference is on, and the editor colours it. The editor takes every key, so the
page's shortcuts (commit, stage all…) are the plugin's maps, sent as `message_action`.

The box has no bars or numbers, whatever the preferences say (`dress`). Tab types a tab here,
so it doesn't move focus out of the box. The stash, tag and branch description fields are still
native text areas.

## Preferences

Preferences' Code View page sets the mode (modeless, vim, helix), font, size, colorscheme,
tab width and relative line numbers. Modeless has no Normal mode, read-only buffers included, so the plugins bind their keys in its
`modeless` layer too. `sourcegit-settings` frees its chords for what a code view doesn't have
(`<C-w>` would close the view showing, and `<C-s>`, `<C-p>` and the buffer keys have nothing to
act on). `CodeView` sends them to `sourcegit-settings` as each editor starts and
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
  their errors, events, quitting, destroying. No pixels: headless has no surface;
- `../tests/`, the port's C# on the same backend, through the library's `test-host` feature,
  with real git in temporary repositories, and temporary preferences: what `ApplyChunk` does
  to the index and the file; a change from git through `DiffJson`, the plugin and back to
  `git apply`; the toolbar; a pane keeping its editor; the blame window. An argument to it runs
  the tests with that in their name.

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
`[patch.crates-io]` in `Cargo.toml` is what keeps one mitsuami: bemtvi takes 2.0.0 from
crates.io, and builds against the sibling here, whose C ABI isn't published.
