//! The notification-area icon (`TrayBranding`, `App` tray menu): double-click
//! opens the window; the menu offers Open VineMix, Windows sound settings
//! and Quit VineMix. Runs on its own thread with a message-only window.

use std::sync::mpsc::Sender;
use std::sync::{Arc, OnceLock};

use windows::core::PCWSTR;
use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::UI::Shell::{Shell_NotifyIconW, NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW};
use windows::Win32::UI::WindowsAndMessaging::{
    AppendMenuW, CreateIconFromResourceEx, CreatePopupMenu, CreateWindowExW, DefWindowProcW, DestroyIcon, DestroyMenu, DestroyWindow, DispatchMessageW,
    GetCursorPos, GetMessageW, GetSystemMetrics, LookupIconIdFromDirectoryEx, PostMessageW, PostQuitMessage, RegisterClassW, RegisterWindowMessageW,
    SetForegroundWindow, TrackPopupMenu, TranslateMessage, HICON, HWND_MESSAGE, IMAGE_FLAGS, LR_DEFAULTCOLOR, MF_SEPARATOR, MF_STRING, MSG,
    SM_CXSMICON, SM_CYSMICON, TPM_BOTTOMALIGN, TPM_RETURNCMD, TPM_RIGHTBUTTON, WINDOW_EX_STYLE, WINDOW_STYLE, WM_APP, WM_CLOSE, WM_CONTEXTMENU,
    WM_DESTROY, WM_LBUTTONDBLCLK, WM_RBUTTONUP, WNDCLASSW,
};

use crate::platform::wide;
use crate::ui::app::Command;

const CALLBACK: u32 = WM_APP + 1;
const ICON: &[u8] = include_bytes!("../assets/branding/tray.ico");

struct Shared {
    sender: Sender<Command>,
    repaint: Arc<dyn Fn() + Send + Sync>,
    taskbar_created: u32,
    icon: isize,
}

static SHARED: OnceLock<Shared> = OnceLock::new();

fn send(command: Command) {
    if let Some(shared) = SHARED.get() {
        let _ = shared.sender.send(command);
        (shared.repaint)();
    }
}

fn add_icon(window: HWND, icon: HICON) {
    let mut data = NOTIFYICONDATAW {
        cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: window,
        uID: 1,
        uFlags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage: CALLBACK,
        hIcon: icon,
        ..Default::default()
    };
    for (target, source) in data.szTip.iter_mut().zip("VineMix".encode_utf16()) {
        *target = source;
    }
    // SAFETY: valid structure and window.
    unsafe {
        let _ = Shell_NotifyIconW(NIM_ADD, &data);
    }
}

fn remove_icon(window: HWND) {
    let data = NOTIFYICONDATAW { cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32, hWnd: window, uID: 1, ..Default::default() };
    // SAFETY: valid structure.
    unsafe {
        let _ = Shell_NotifyIconW(NIM_DELETE, &data);
    }
}

fn show_menu(window: HWND) {
    // SAFETY: menu created and destroyed here; window owned by this thread.
    unsafe {
        let Ok(menu) = CreatePopupMenu() else { return };
        let items = [(1usize, "Open VineMix"), (2, "Windows sound settings")];
        for (id, label) in items {
            let text = wide(label);
            let _ = AppendMenuW(menu, MF_STRING, id, PCWSTR(text.as_ptr()));
        }
        let _ = AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
        let quit = wide("Quit VineMix");
        let _ = AppendMenuW(menu, MF_STRING, 3, PCWSTR(quit.as_ptr()));
        let mut point = Default::default();
        let _ = GetCursorPos(&mut point);
        let _ = SetForegroundWindow(window);
        let chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.x, point.y, Some(0), window, None);
        let _ = DestroyMenu(menu);
        match chosen.0 {
            1 => send(Command::Show),
            2 => send(Command::SoundSettings),
            3 => send(Command::Quit),
            _ => {}
        }
    }
}

unsafe extern "system" fn procedure(window: HWND, message: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    let shared = SHARED.get();
    if message == CALLBACK {
        match (lparam.0 & 0xFFFF) as u32 {
            WM_LBUTTONDBLCLK => send(Command::Show),
            WM_RBUTTONUP | WM_CONTEXTMENU => show_menu(window),
            _ => {}
        }
        return LRESULT(0);
    }
    if let Some(shared) = shared {
        if message == shared.taskbar_created && shared.taskbar_created != 0 {
            add_icon(window, HICON(shared.icon as *mut _));
            return LRESULT(0);
        }
    }
    match message {
        WM_CLOSE => {
            remove_icon(window);
            // SAFETY: our own window.
            unsafe {
                let _ = DestroyWindow(window);
            }
            LRESULT(0)
        }
        WM_DESTROY => {
            // SAFETY: ends this thread's message loop.
            unsafe { PostQuitMessage(0) };
            LRESULT(0)
        }
        // SAFETY: default handling.
        _ => unsafe { DefWindowProcW(window, message, wparam, lparam) },
    }
}

fn load_icon() -> HICON {
    // SAFETY: the ICO data is a valid icon directory embedded at build time.
    unsafe {
        let cx = GetSystemMetrics(SM_CXSMICON);
        let cy = GetSystemMetrics(SM_CYSMICON);
        let offset = LookupIconIdFromDirectoryEx(ICON.as_ptr(), true, cx, cy, LR_DEFAULTCOLOR);
        if offset <= 0 || offset as usize >= ICON.len() {
            return HICON::default();
        }
        CreateIconFromResourceEx(&ICON[offset as usize..], true, 0x0003_0000, cx, cy, IMAGE_FLAGS(LR_DEFAULTCOLOR.0)).unwrap_or_default()
    }
}

/// A running tray icon; dropping it removes the icon.
#[derive(Debug)]
pub struct Tray {
    window: Arc<std::sync::Mutex<isize>>,
    thread: Option<std::thread::JoinHandle<()>>,
}

impl Tray {
    pub fn start(sender: Sender<Command>, repaint: Arc<dyn Fn() + Send + Sync>) -> Self {
        let window = Arc::new(std::sync::Mutex::new(0isize));
        let slot = Arc::clone(&window);
        let thread = std::thread::Builder::new()
            .name("VineMix tray".into())
            .spawn(move || {
                let class = wide("VineMix.Tray");
                let created = wide("TaskbarCreated");
                // SAFETY: registers and creates a message-only window on this thread.
                unsafe {
                    let taskbar_created = RegisterWindowMessageW(PCWSTR(created.as_ptr()));
                    let icon = load_icon();
                    let _ = SHARED.set(Shared { sender, repaint, taskbar_created, icon: icon.0 as isize });
                    let wc = WNDCLASSW { lpfnWndProc: Some(procedure), lpszClassName: PCWSTR(class.as_ptr()), ..Default::default() };
                    RegisterClassW(&wc);
                    let Ok(hwnd) = CreateWindowExW(
                        WINDOW_EX_STYLE(0),
                        PCWSTR(class.as_ptr()),
                        PCWSTR(class.as_ptr()),
                        WINDOW_STYLE(0),
                        0,
                        0,
                        0,
                        0,
                        Some(HWND_MESSAGE),
                        None,
                        None,
                        None,
                    ) else {
                        return;
                    };
                    *slot.lock().unwrap_or_else(std::sync::PoisonError::into_inner) = hwnd.0 as isize;
                    add_icon(hwnd, icon);
                    let mut message = MSG::default();
                    while GetMessageW(&mut message, None, 0, 0).as_bool() {
                        let _ = TranslateMessage(&message);
                        DispatchMessageW(&message);
                    }
                    if !icon.is_invalid() {
                        let _ = DestroyIcon(icon);
                    }
                }
            })
            .ok();
        Self { window, thread }
    }
}

impl Drop for Tray {
    fn drop(&mut self) {
        let handle = *self.window.lock().unwrap_or_else(std::sync::PoisonError::into_inner);
        if handle != 0 {
            // SAFETY: posting to our own window.
            unsafe {
                let _ = PostMessageW(Some(HWND(handle as *mut _)), WM_CLOSE, WPARAM(0), LPARAM(0));
            }
        }
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}
