use thiserror::Error;

pub type Result<T, E = JobError> = std::result::Result<T, E>;

/// Everything that can make a job invalid. Messages are written for the
/// person running the machine, not for a developer.
#[derive(Debug, Error, Clone, PartialEq)]
pub enum JobError {
    #[error("{what} must be between {min} and {max} {unit} (got {value})")]
    OutOfRange {
        what: &'static str,
        value: f64,
        min: f64,
        max: f64,
        unit: &'static str,
    },

    #[error("{what} is not a valid number")]
    NotFinite { what: &'static str },

    #[error("pulse width {0} ns is not supported; choose one of {1:?}")]
    PulseWidth(u32, &'static [u32]),

    #[error("point ({x:.3}, {y:.3}) mm is outside the work area (0–{w} × 0–{h} mm){context}")]
    OutsideWorkArea {
        x: f64,
        y: f64,
        w: f64,
        h: f64,
        context: String,
    },

    #[error("the job has nothing to process")]
    Empty,

    #[error("{0}")]
    Invalid(String),
}

pub(crate) fn check_range(
    what: &'static str,
    value: f64,
    min: f64,
    max: f64,
    unit: &'static str,
) -> Result<()> {
    if !value.is_finite() {
        return Err(JobError::NotFinite { what });
    }
    if value < min || value > max {
        return Err(JobError::OutOfRange {
            what,
            value,
            min,
            max,
            unit,
        });
    }
    Ok(())
}
