//! SourceGit's code view: bemtvi as a mitsuami view, over a C ABI, in one library with
//! mitsuami's and extras' (whose functions it exports too): two copies of mitsuami in
//! one process would be two cores. The port loads this library in their place.
//!
//! The features are Lua plugins in `editor/`, the editor's whole config. This crate only
//! carries calls to them and what they say back, as JSON.

use std::cell::RefCell;
use std::collections::HashMap;
use std::ffi::c_void;
use std::future::Future;
use std::path::PathBuf;

use bemtvi_mitsuami::{Config, Editor, EditorEvent, EditorHandle, Session, SessionOptions, Shada, Value};
use mitsuami::core::View;
use mitsuami::reactive::{Owner, on_cleanup};
use mitsuami_ffi::MitsuamiStr;
use mitsuami_ffi::extend::{guard, keep, string, ui};

pub use mitsuami_extras_ffi;

#[cfg(feature = "test-host")]
mod test_host;

/// The name plugins see as `btv.host.name()`.
pub const HOST: &str = "sourcegit";

/// `editor/` in this checkout, for what runs from it: the tests and the manual window.
pub fn checkout_editor_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("editor")
}

/// An editor on `config`, its whole config, in bemtvi's single-file lockdown with no file:
/// a code view shows what its plugins put in views, and opens nothing itself. It shares
/// nothing with the user's bemtvi but its grammars (`:TSInstall`, which
/// `sourcegit-grammars` runs).
pub fn session(config: PathBuf) -> Result<Session, String> {
    start(SessionOptions::views_only(), config)
}

/// An editor for the one file `file`, in the same lockdown, for what's edited: a commit
/// message.
pub fn file_session(config: PathBuf, file: String) -> Result<Session, String> {
    start(SessionOptions::single_file(file), config)
}

fn start(options: SessionOptions, config: PathBuf) -> Result<Session, String> {
    options
        .config(Config::Dir(config))
        .shada(Shada::Off)
        .host(HOST)
        .enable_commands(["TSInstall"])
        .spawn()
        .map_err(|e| format!("{e:#}"))
}

/// Calls `require(module)[function](json decoded)` and returns its result as JSON.
const CALL: &str = "local module, name, json = ...; \
    return btv.json.encode(require(module)[name](btv.json.decode(json)) or btv.json.null)";

/// Calls a plugin in `editor`, to [`CALL`]'s result.
pub fn call(
    editor: &EditorHandle,
    module: &str,
    function: &str,
    json: &str,
) -> impl Future<Output = anyhow::Result<Value>> + 'static {
    let args = vec![Value::from(module), Value::from(function), Value::from(json)];
    editor.exec_lua_with(CALL, args)
}

/// What a plugin sent, as JSON.
pub fn to_json(value: &Value) -> serde_json::Value {
    use serde_json::Value as J;
    match value {
        Value::Nil => J::Null,
        Value::Boolean(b) => J::Bool(*b),
        Value::Integer(i) => i.as_i64().map(J::from).or_else(|| i.as_u64().map(J::from)).unwrap_or(J::Null),
        Value::F32(f) => serde_json::Number::from_f64(f64::from(*f)).map_or(J::Null, J::Number),
        Value::F64(f) => serde_json::Number::from_f64(*f).map_or(J::Null, J::Number),
        Value::String(s) => {
            J::String(s.as_str().map_or_else(|| String::from_utf8_lossy(s.as_bytes()).into_owned(), str::to_owned))
        }
        Value::Binary(b) | Value::Ext(_, b) => J::String(String::from_utf8_lossy(b).into_owned()),
        Value::Array(items) => J::Array(items.iter().map(to_json).collect()),
        Value::Map(pairs) => J::Object(
            pairs
                .iter()
                .map(|(k, v)| {
                    let key = match to_json(k) {
                        J::String(s) => s,
                        other => other.to_string(),
                    };
                    (key, to_json(v))
                })
                .collect(),
        ),
    }
}

/// What a code view tells the app: a plugin's `btv.host.send(name, payload)`, its payload
/// as JSON, or `exit` when the editor quit (`:q`).
pub type EventCallback = extern "C" fn(user: *mut c_void, name: MitsuamiStr, json: MitsuamiStr);

/// How a call went: its result as JSON, or what it raised.
pub type DoneCallback = extern "C" fn(user: *mut c_void, ok: bool, text: MitsuamiStr);

fn str_of(s: &str) -> MitsuamiStr {
    MitsuamiStr { ptr: s.as_ptr(), len: s.len() }
}

thread_local! {
    static EDITORS: RefCell<HashMap<u32, EditorHandle>> = RefCell::new(HashMap::new());
}

fn editor(id: u32) -> Result<EditorHandle, String> {
    EDITORS.with(|e| e.borrow().get(&id).cloned()).ok_or_else(|| format!("{id} isn't a code view"))
}

/// A code view, with `config` as its editor's config directory, editing `file`, or showing
/// only what its plugins put in views if that's empty. It fills what its row or
/// column has left; mitsuami's functions place it, focus it and destroy it, which stops
/// its editor. `on_event` runs on the UI thread until then. 0 on failure.
///
/// # Safety
/// `config` and `file` are strings as mitsuami's functions take them; `user` is what `on_event`
/// expects.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn sourcegit_codeview_create(
    config: MitsuamiStr,
    file: MitsuamiStr,
    on_event: EventCallback,
    user: *mut c_void,
) -> u32 {
    let user = user as usize;
    guard(|| {
        let ui = ui()?;
        let config = PathBuf::from(unsafe { string(config)? });
        let file = unsafe { string(file)? };
        let session = if file.is_empty() { session(config)? } else { file_session(config, file)? };
        let handle = EditorHandle::new();
        let owner = Owner::new_root();
        let node = owner.with(|| {
            Editor::new(session, move || on_event(user as *mut c_void, str_of("exit"), str_of("null")))
                .handle(handle.clone())
                .native_dialogs(false)
                .on_event(move |event| {
                    let EditorEvent::Host { name, payload } = event;
                    let json = to_json(&payload).to_string();
                    on_event(user as *mut c_void, str_of(&name), str_of(&json));
                })
                .build(&ui)
        });
        let raw = node.raw();
        EDITORS.with(|e| e.borrow_mut().insert(raw, handle));
        owner.with(|| on_cleanup(move || _ = EDITORS.with(|e| e.borrow_mut().remove(&raw))));
        keep(raw, owner);
        Ok(raw)
    })
    .unwrap_or(0)
}

/// Calls `require(module)[function](json)` in the editor. `done` runs once on the UI
/// thread, also if the editor goes first.
///
/// # Safety
/// The strings are as mitsuami's functions take them; `user` is what `done` expects.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn sourcegit_codeview_call(
    id: u32,
    module: MitsuamiStr,
    function: MitsuamiStr,
    json: MitsuamiStr,
    done: DoneCallback,
    user: *mut c_void,
) -> bool {
    let user = user as usize;
    guard(|| {
        let future = unsafe { call(&editor(id)?, &string(module)?, &string(function)?, &string(json)?) };
        ui()?.spawn_local(async move {
            let user = user as *mut c_void;
            match future.await {
                Ok(value) => {
                    let text = value.as_str().unwrap_or("null").to_owned();
                    done(user, true, str_of(&text));
                }
                Err(e) => done(user, false, str_of(&format!("{e:#}"))),
            }
        });
        Ok(())
    })
    .is_some()
}

/// Runs an ex command in the editor, whatever mode it's in (`qa!`).
///
/// # Safety
/// `command` is a string as mitsuami's functions take them.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn sourcegit_codeview_command(id: u32, command: MitsuamiStr) -> bool {
    guard(|| {
        let command = unsafe { string(command)? };
        editor(id)?.command(&command).map_err(|e| format!("{e:#}"))
    })
    .is_some()
}
