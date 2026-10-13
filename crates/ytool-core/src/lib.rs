//! Core logic for YTool Studio: machine limits, laser settings, geometry,
//! G-code generation, and job packaging for xTool galvo lasers.
//!
//! Everything here is pure (no I/O beyond byte buffers) so it can be tested
//! exhaustively and shared by the CLI and the desktop app.

pub mod bgcode;
pub mod error;
pub mod gcode;
pub mod geom;
pub mod machine;
pub mod numfmt;
pub mod settings;
pub mod xf;

pub use error::{JobError, Result};
pub use geom::{Point, Polyline};
pub use machine::MachineProfile;
pub use settings::{LaserSettings, LightSource};
