//! The code view over its C ABI, as the port drives it: the real editor and plugins on
//! mitsuami's headless backend, which has no window to present to, so what's tested is
//! the editor behind the surface, not its pixels.

use std::cell::RefCell;
use std::ffi::c_void;
use std::time::{Duration, Instant};

use mitsuami::core::{NodeId, Ui, View};
use mitsuami_ffi::*;
use mitsuami_test::prelude::*;
use sourcegit_codeview::*;

thread_local! {
    static EVENTS: RefCell<Vec<(String, String)>> = const { RefCell::new(Vec::new()) };
    static DONE: RefCell<Vec<(bool, String)>> = const { RefCell::new(Vec::new()) };
}

fn text(s: MitsuamiStr) -> String {
    let bytes = unsafe { std::slice::from_raw_parts(s.ptr, s.len) };
    String::from_utf8(bytes.to_vec()).unwrap()
}

extern "C" fn heard(_: *mut c_void, name: MitsuamiStr, json: MitsuamiStr) {
    EVENTS.with(|e| e.borrow_mut().push((text(name), text(json))));
}

extern "C" fn done(_: *mut c_void, ok: bool, result: MitsuamiStr) {
    DONE.with(|d| d.borrow_mut().push((ok, text(result))));
}

fn str(s: &str) -> MitsuamiStr {
    MitsuamiStr { ptr: s.as_ptr(), len: s.len() }
}

fn last_error() -> String {
    let mut ptr = std::ptr::null();
    let len = unsafe { mitsuami_last_error(&mut ptr) };
    text(MitsuamiStr { ptr, len })
}

/// A node made through the C ABI, mounted as a view.
struct Adopt(u32);

impl View for Adopt {
    fn build(self, _: &Ui) -> NodeId {
        NodeId::from_raw(self.0)
    }
}

async fn until(app: &TestApp, what: &str, ready: impl Fn() -> bool) {
    let deadline = Instant::now() + Duration::from_secs(20);
    while !ready() {
        assert!(Instant::now() < deadline, "timed out waiting for {what}");
        app.settle().await;
        std::thread::sleep(Duration::from_millis(5));
    }
}

async fn mounted(app: &TestApp) -> u32 {
    EVENTS.with(|e| e.borrow_mut().clear());
    DONE.with(|d| d.borrow_mut().clear());
    attach(app.ui());
    let dir = checkout_editor_dir();
    let dir = dir.to_str().unwrap();
    let id = unsafe { sourcegit_codeview_create(str(dir), heard, std::ptr::null_mut()) };
    assert_ne!(id, 0, "{}", last_error());
    app.mount(move || Adopt(id));
    app.settle().await;
    id
}

/// Calls a plugin and waits for how it went.
async fn call(app: &TestApp, id: u32, module: &str, function: &str, json: &str) -> (bool, String) {
    DONE.with(|d| d.borrow_mut().clear());
    assert!(unsafe { sourcegit_codeview_call(id, str(module), str(function), str(json), done, std::ptr::null_mut()) });
    until(app, "the call", || DONE.with(|d| !d.borrow().is_empty())).await;
    DONE.with(|d| d.borrow_mut().remove(0))
}

fn event(name: &str) -> Option<String> {
    EVENTS.with(|e| e.borrow().iter().find(|(n, _)| n == name).map(|(_, json)| json.clone()))
}

const FIXTURE: &str = include_str!("../fixtures/edit.json");

#[mitsuami_test::test]
async fn a_diff_reaches_the_plugin_and_it_answers(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    assert!(ok, "{result}");
    until(&app, "shown", || event("shown").is_some()).await;
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(&event("shown").unwrap()).unwrap(),
        serde_json::json!({ "path": "src/app.rs", "lines": 28, "hunks": 3 }),
    );
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn text_arrives_byte_for_byte(app: TestApp) {
    // What careless quoting of the chunk would change.
    let line = "a\"b\\c\r\n\t]]é日本\u{1}9";
    let json = serde_json::json!({
        "path": "x",
        "lines": [{ "kind": "normal", "text": line, "old": 1, "new": 1 }],
    });
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", &json.to_string()).await;
    assert!(ok, "{result}");
    // It survives as far as Lua: its length in bytes comes back.
    let (ok, result) = call(&app, id, "string", "len", &serde_json::to_string(line).unwrap()).await;
    assert!(ok, "{result}");
    assert_eq!(result, line.len().to_string());
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn what_the_plugin_refuses_comes_back_as_the_error(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", r#"{"path":"x","lines":[{"kind":"moved"}]}"#).await;
    assert!(!ok);
    assert!(result.contains("unknown kind: moved"), "{result}");
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn quitting_the_editor_tells_the_app(app: TestApp) {
    let id = mounted(&app).await;
    assert!(unsafe { sourcegit_codeview_command(id, str("qa!")) });
    until(&app, "exit", || event("exit").is_some()).await;
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn quitting_with_a_diff_showing_tells_the_app(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    assert!(ok, "{result}");
    until(&app, "shown", || event("shown").is_some()).await;
    assert!(unsafe { sourcegit_codeview_command(id, str("q")) });
    until(&app, "exit", || event("exit").is_some()).await;
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn the_preferences_reach_the_editor(app: TestApp) {
    let id = mounted(&app).await;
    let settings = r#"{"mode":"modeless","font":"Iosevka","font_size":12.5,"tab_width":2,"relative_numbers":true,"colorscheme":"habamax"}"#;
    let (ok, result) = call(&app, id, "sourcegit-settings", "apply", settings).await;
    assert!(ok, "{result}");
    let (ok, result) = call(&app, id, "sourcegit-settings", "apply", r#"{"mode":"emacs"}"#).await;
    assert!(!ok);
    assert!(result.contains("unknown mode: emacs"), "{result}");
    assert!(mitsuami_destroy(id));
}

/// bemtvi's single-file lockdown, with no file: a code view opens nothing.
#[mitsuami_test::test]
async fn it_opens_no_file(app: TestApp) {
    attach(app.ui());
    let handle = bemtvi_mitsuami::EditorHandle::new();
    let editor = bemtvi_mitsuami::Editor::new(session(checkout_editor_dir()).unwrap(), || {}).handle(handle.clone());
    app.mount(move || editor);
    app.settle().await;
    let lua = async |code: &str| {
        let future = handle.exec_lua(code);
        let mut future = std::pin::pin!(future);
        let mut cx = std::task::Context::from_waker(std::task::Waker::noop());
        let deadline = Instant::now() + Duration::from_secs(20);
        loop {
            if let std::task::Poll::Ready(value) = future.as_mut().poll(&mut cx) {
                return value.unwrap();
            }
            assert!(Instant::now() < deadline, "the editor did not answer");
            app.settle().await;
            std::thread::sleep(Duration::from_millis(2));
        }
    };
    handle.open(file!()).unwrap();
    assert_eq!(lua("return btv.buf.name(0)").await.as_str(), Some(""));
    let locked = "return btv.command_disabled('terminal') and btv.command_disabled('lua') \
        and btv.plugins.disabled() and btv.lsp.disabled()";
    assert_eq!(lua(locked).await.as_bool(), Some(true));
    // The one it gets back, for `sourcegit-grammars`.
    let install = "return btv.command_disabled('TSInstall')";
    assert_eq!(lua(install).await.as_bool(), Some(false));
}

#[mitsuami_test::test]
async fn the_grammar_preference_reaches_the_plugin(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-grammars", "configure", r#"{"enabled":false,"tried":["rust"]}"#).await;
    assert!(ok, "{result}");
    let (ok, result) = call(&app, id, "sourcegit-grammars", "ready", r#"{"lang":"rust"}"#).await;
    assert!(ok, "{result}");
    let (ok, result) = call(&app, id, "sourcegit-grammars", "configure", "{}").await;
    assert!(!ok);
    assert!(result.contains("bad enabled"), "{result}");
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn the_toolbar_reaches_the_plugin(app: TestApp) {
    let id = mounted(&app).await;
    call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    until(&app, "shown", || event("shown").is_some()).await;
    EVENTS.with(|e| e.borrow_mut().clear());
    let (ok, result) = call(&app, id, "sourcegit-diff", "layout", r#"{"all":true}"#).await;
    assert!(ok, "{result}");
    until(&app, "shown again", || event("shown").is_some()).await;
    assert!(event("shown").unwrap().contains(r#""lines":252"#), "{:?}", event("shown"));
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn quitting_side_by_side_tells_the_app(app: TestApp) {
    let id = mounted(&app).await;
    call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    until(&app, "shown", || event("shown").is_some()).await;
    EVENTS.with(|e| e.borrow_mut().clear());
    let (ok, result) = call(&app, id, "sourcegit-diff", "layout", r#"{"side_by_side":true}"#).await;
    assert!(ok, "{result}");
    until(&app, "shown again", || event("shown").is_some()).await;
    assert!(unsafe { sourcegit_codeview_command(id, str("q")) });
    until(&app, "exit", || event("exit").is_some()).await;
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn a_file_shows_as_the_port_sends_it(app: TestApp) {
    let id = mounted(&app).await;
    let json = serde_json::json!({ "path": "src/app.rs", "text": "fn main() {}\r\n" }).to_string();
    let (ok, result) = call(&app, id, "sourcegit-file", "show", &json).await;
    assert!(ok, "{result}");
    until(&app, "shown", || event("shown").is_some()).await;
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(&event("shown").unwrap()).unwrap(),
        serde_json::json!({ "path": "src/app.rs", "language": "rust" }),
    );
    let (ok, result) = call(&app, id, "sourcegit-file", "show", r#"{"path":"a"}"#).await;
    assert!(!ok);
    assert!(result.contains("no text"), "{result}");
    assert!(mitsuami_destroy(id));
}

/// The cursor's commit reaches the app, past a real host: the plugin's specs hear its keys.
#[mitsuami_test::test]
async fn blame_tells_the_app_about_its_lines(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-blame", "show", include_str!("../fixtures/blame-sample.json")).await;
    assert!(ok, "{result}");
    until(&app, "shown", || event("shown").is_some()).await;
    until(&app, "the first line's commit", || event("blame_line").is_some()).await;
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(&event("blame_line").unwrap()).unwrap(),
        serde_json::json!({ "sha": "a1b2c3d4", "file": "src/app.rs" }),
    );
    EVENTS.with(|e| e.borrow_mut().clear());
    assert!(unsafe { sourcegit_codeview_command(id, str("normal! 3G")) });
    until(&app, "the next commit", || event("blame_line").is_some()).await;
    assert!(event("blame_line").unwrap().contains("e5f6a7b8"), "{:?}", event("blame_line"));
    assert!(mitsuami_destroy(id));
}

/// A pane keeps its editor from one thing to the next, so one editor shows another file's
/// diff, then its blame, in the same view.
#[mitsuami_test::test]
async fn one_editor_shows_one_thing_after_another(app: TestApp) {
    let id = mounted(&app).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    assert!(ok, "{result}");
    until(&app, "shown", || event("shown").is_some()).await;
    EVENTS.with(|e| e.borrow_mut().clear());
    let next = FIXTURE.replace("src/app.rs", "src/other.rs");
    let (ok, result) = call(&app, id, "sourcegit-diff", "show", &next).await;
    assert!(ok, "{result}");
    until(&app, "the next file", || event("shown").is_some_and(|s| s.contains("other.rs"))).await;
    assert!(event("exit").is_none());
    assert!(mitsuami_destroy(id));
}

/// Where the cursor is among the changes reaches the app, for the toolbar's `2/5`.
#[mitsuami_test::test]
async fn the_toolbar_hears_where_the_cursor_is(app: TestApp) {
    let id = mounted(&app).await;
    call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    until(&app, "the position", || event("change_position").is_some()).await;
    let position = |json: String| serde_json::from_str::<serde_json::Value>(&json).unwrap();
    assert_eq!(position(event("change_position").unwrap()), serde_json::json!({ "at": 1, "total": 3 }));
    EVENTS.with(|e| e.borrow_mut().clear());
    let (ok, result) = call(&app, id, "sourcegit-diff", "go_to", r#"{"where":"last"}"#).await;
    assert!(ok, "{result}");
    until(&app, "the last change", || event("change_position").is_some()).await;
    assert_eq!(position(event("change_position").unwrap()), serde_json::json!({ "at": 3, "total": 3 }));
    assert!(mitsuami_destroy(id));
}

/// What to stage reaches the app, past a real host, with what makes its patch.
#[mitsuami_test::test]
async fn the_change_to_stage_reaches_the_app(app: TestApp) {
    let id = mounted(&app).await;
    call(&app, id, "sourcegit-diff", "show", FIXTURE).await;
    until(&app, "shown", || event("shown").is_some()).await;
    let (ok, result) = call(&app, id, "sourcegit-diff", "act_on", r#"{"action":"stage"}"#).await;
    assert!(ok, "{result}");
    until(&app, "the chunk", || event("chunk").is_some()).await;
    assert_eq!(
        serde_json::from_str::<serde_json::Value>(&event("chunk").unwrap()).unwrap(),
        serde_json::json!({ "action": "stage", "first": 5, "last": 7, "combined": true, "is_old": false }),
    );
    assert!(mitsuami_destroy(id));
}

#[mitsuami_test::test]
async fn destroyed_it_is_gone(app: TestApp) {
    let id = mounted(&app).await;
    assert!(mitsuami_destroy(id));
    assert!(!unsafe { sourcegit_codeview_command(id, str("qa!")) });
}

mitsuami_test::main!();
