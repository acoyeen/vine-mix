//! The VineMix desktop: the node workspace, its engine connection and
//! Windows integration.
//!
//! The logic layer (graph rules, history, mixer state, engine link) is free
//! of any UI toolkit and is tested directly. The egui presentation lives in
//! [`app`] and its submodules.

#![cfg(windows)]

pub mod availability;
pub mod connection;
pub mod editing;
pub mod frames;
pub mod graph;
pub mod history;
pub mod link;
pub mod migration;
pub mod mixer;
pub mod paths;
pub mod platform;
pub mod processing_load;
pub mod preview;
pub mod protocol;
pub mod runtime;
pub mod smoke;
pub mod supervisor;
pub mod text;
pub mod time;
pub mod tray;
pub mod ui;
pub mod update;
