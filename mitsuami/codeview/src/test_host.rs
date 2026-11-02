//! A headless UI for the port's C# tests (`mitsuami/tests/`): the C# views over this library, with
//! no window opened, and what the test kit does for Rust's tests, which C# can't reach.

use std::cell::RefCell;

use mitsuami::core::{A11yAction, A11yNode, NodeId, TestHooks, Ui};
use mitsuami_ffi::MitsuamiStr;
use mitsuami_ffi::extend::{guard, string};
use mitsuami_headless::{HeadlessBackend, HeadlessHandle};

thread_local! {
    static HOST: RefCell<Option<(Ui, HeadlessHandle)>> = const { RefCell::new(None) };
}

/// Starts the UI on this thread, which stays its UI thread.
#[unsafe(no_mangle)]
pub extern "C" fn sourcegit_test_host_start() -> bool {
    guard(|| {
        let backend = HeadlessBackend::new();
        let handle = backend.handle();
        let ui = Ui::new(backend);
        mitsuami_ffi::attach(&ui);
        HOST.with(|h| *h.borrow_mut() = Some((ui, handle)));
        Ok(())
    })
    .is_some()
}

/// One turn of the run loop: what's posted, what finished, and what that changed, as the test
/// kit's `settle` does.
#[unsafe(no_mangle)]
pub extern "C" fn sourcegit_test_host_settle() {
    HOST.with(|h| {
        if let Some((ui, handle)) = h.borrow().as_ref() {
            for _ in 0..16 {
                ui.tick();
                handle.settle();
                if ui.events().is_empty() {
                    break;
                }
            }
        }
    });
}

fn named(node: &A11yNode, name: &str) -> Option<u32> {
    if node.name.as_deref() == Some(name) {
        return Some(node.id.raw());
    }
    node.children.iter().find_map(|child| named(child, name))
}

/// The first node in `window` assistive technology knows by `name`, or 0.
///
/// # Safety
/// `name` is a string as mitsuami's functions take them.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn sourcegit_test_host_find(window: u32, name: MitsuamiStr) -> u32 {
    guard(|| {
        let name = unsafe { string(name)? };
        Ok(HOST.with(|h| {
            let host = h.borrow();
            let (ui, _) = host.as_ref()?;
            named(&ui.a11y_tree(NodeId::from_raw(window))?, &name)
        }))
    })
    .flatten()
    .unwrap_or(0)
}

/// Presses a control, as assistive technology does.
#[unsafe(no_mangle)]
pub extern "C" fn sourcegit_test_host_activate(node: u32) -> bool {
    guard(|| {
        let ui = HOST.with(|h| h.borrow().as_ref().map(|(ui, _)| ui.clone())).ok_or("no host")?;
        ui.perform(NodeId::from_raw(node), &A11yAction::Activate).map_err(|e| format!("{e}"))
    })
    .is_some()
}
