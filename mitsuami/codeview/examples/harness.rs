//! A code view by itself, to try its plugins by hand without SourceGit:
//! `cargo run --example harness [fixtures/edit.json]`.
//!
//! It's the real editor, config and plugins, in a window of mitsuami's. A button per
//! file in `fixtures/` sends it to the plugin as the app would, the next row lays it out as
//! the diff view's toolbar does, the next sets what the Code View preferences set, and the line below shows what the plugins said back. Edit the Lua and run it again: it isn't compiled in.

use std::path::{Path, PathBuf};

use bemtvi_mitsuami::{Editor, EditorEvent, EditorHandle};
use mitsuami::prelude::*;
use sourcegit_codeview::{call, checkout_editor_dir, session, to_json};

fn fixtures() -> Vec<PathBuf> {
    let dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("fixtures");
    let mut files: Vec<PathBuf> = std::fs::read_dir(dir)
        .expect("fixtures/")
        .filter_map(|entry| Some(entry.ok()?.path()))
        .filter(|path| path.extension().is_some_and(|e| e == "json"))
        .collect();
    files.sort();
    files
}

fn main() {
    let first = std::env::args().nth(1).map(PathBuf::from);
    let session = session(checkout_editor_dir()).expect("start the editor");
    let handle = EditorHandle::new();
    let ui_handle = handle.clone();
    mitsuami::App::new()
        .id("br.com.roboport.mitsuami.SourceGit.CodeViewHarness")
        .name("Code view")
        .open(move || window(ui_handle, session, first))
        .run();
    if handle.close() {
        eprintln!("harness: an editor server thread panicked");
        std::process::exit(101);
    }
}

fn window(editor: EditorHandle, session: bemtvi_mitsuami::Session, first: Option<PathBuf>) -> Window {
    let open = signal(true);
    let log = signal(String::from("Nothing from the plugins yet."));
    let session = std::cell::Cell::new(Some(session));
    let send = {
        let editor = editor.clone();
        move |path: &Path| {
            let json = match std::fs::read_to_string(path) {
                Ok(json) => json,
                Err(e) => return log.set(format!("{}: {e}", path.display())),
            };
            // `file-*.json` is a whole file, `blame-*.json` a blame, the rest are diffs.
            let name = path.file_name().map_or_else(String::new, |n| n.to_string_lossy().into_owned());
            let module = match name {
                n if n.starts_with("file-") => "sourcegit-file",
                n if n.starts_with("blame-") => "sourcegit-blame",
                _ => "sourcegit-diff",
            };
            let editor = editor.clone();
            spawn_local(async move {
                if let Err(e) = call(&editor, module, "show", &json).await {
                    log.set(format!("raised: {e:#}"));
                }
            });
        }
    };
    Window::new("Code view").size(Size::new(1000.0, 650.0)).open(open).content(move || {
        let session = session.take().expect("the window's content is built once");
        let buttons: Vec<Button> = fixtures()
            .into_iter()
            .map(|path| {
                let name = path.file_stem().map_or_else(String::new, |s| s.to_string_lossy().into_owned());
                let send = send.clone();
                Button::new(name).on_click(move || send(&path))
            })
            .collect();
        // A task, so the editor below is mounted when it runs.
        if let Some(path) = first.clone().or_else(|| fixtures().into_iter().next()) {
            send(&path);
        }
        Column::new().size(100.pct(), 100.pct()).children((
            Row::new().padding(8.px()).gap(Spacing::Sm).children(buttons),
            Editor::new(session, move || open.set(false))
                .handle(editor.clone())
                .native_dialogs(false)
                .autofocus(true)
                .on_event(move |event| {
                    let EditorEvent::Host { name, payload } = event;
                    log.set(format!("{name}: {}", to_json(&payload)));
                }),
            Text::new(move || log.get()).padding(8.px()),
        ))
    })
}
