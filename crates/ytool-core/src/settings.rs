//! Laser settings and their validation against a machine profile.

use crate::error::{JobError, Result, check_range};
use crate::machine::MachineProfile;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum LightSource {
    /// 455 nm diode (`G21`).
    Blue,
    /// 1064 nm MOPA fiber (`G22`), with pulse width and frequency.
    Red,
}

impl LightSource {
    pub fn as_str(self) -> &'static str {
        match self {
            LightSource::Blue => "blue",
            LightSource::Red => "red",
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct LaserSettings {
    pub light: LightSource,
    /// 0–100 %. Sent as `S` = percent × 10.
    pub power: f64,
    /// mm/s. Sent as `F` = mm/s × 60.
    pub speed: f64,
    /// How many times this content is processed (≥ 1).
    pub passes: u32,
    /// Fiber only.
    pub pulse_width_ns: u32,
    /// Fiber only.
    pub frequency_khz: u32,
}

impl LaserSettings {
    pub fn blue(power: f64, speed: f64) -> Self {
        Self {
            light: LightSource::Blue,
            power,
            speed,
            passes: 1,
            pulse_width_ns: 200,
            frequency_khz: 65,
        }
    }

    pub fn red(power: f64, speed: f64, pulse_width_ns: u32, frequency_khz: u32) -> Self {
        Self {
            light: LightSource::Red,
            power,
            speed,
            passes: 1,
            pulse_width_ns,
            frequency_khz,
        }
    }

    pub fn with_passes(mut self, passes: u32) -> Self {
        self.passes = passes;
        self
    }

    /// `S` word value (per-mille of full power).
    pub fn s_value(&self) -> i64 {
        (self.power * 10.0).round() as i64
    }

    /// `F` word value (mm/min).
    pub fn f_value(&self) -> i64 {
        (self.speed * 60.0).round() as i64
    }

    pub fn validate(&self, m: &MachineProfile) -> Result<()> {
        check_range("power", self.power, 0.0, 100.0, "%")?;
        check_range("speed", self.speed, m.min_speed, m.max_speed, "mm/s")?;
        if self.passes == 0 || self.passes > 10_000 {
            return Err(JobError::OutOfRange {
                what: "passes",
                value: self.passes as f64,
                min: 1.0,
                max: 10_000.0,
                unit: "",
            });
        }
        if self.light == LightSource::Red {
            let lim = m
                .mopa_limit(self.pulse_width_ns)
                .ok_or_else(|| JobError::PulseWidth(self.pulse_width_ns, PULSE_WIDTHS))?;
            check_range(
                "frequency",
                self.frequency_khz as f64,
                1.0,
                lim.max_khz as f64,
                "kHz",
            )?;
        }
        Ok(())
    }
}

const PULSE_WIDTHS: &[u32] = &[
    2, 4, 6, 9, 13, 20, 30, 45, 60, 80, 100, 150, 200, 250, 350, 500,
];

#[cfg(test)]
mod tests {
    use super::*;
    use crate::machine::F2_ULTRA;

    #[test]
    fn validates_ranges() {
        assert!(LaserSettings::blue(70.0, 40.0).validate(&F2_ULTRA).is_ok());
        assert!(LaserSettings::blue(0.0, 2.0).validate(&F2_ULTRA).is_ok());
        assert!(
            LaserSettings::blue(100.1, 40.0)
                .validate(&F2_ULTRA)
                .is_err()
        );
        assert!(LaserSettings::blue(-1.0, 40.0).validate(&F2_ULTRA).is_err());
        assert!(LaserSettings::blue(50.0, 1.0).validate(&F2_ULTRA).is_err());
        assert!(
            LaserSettings::blue(50.0, 15001.0)
                .validate(&F2_ULTRA)
                .is_err()
        );
        assert!(
            LaserSettings::blue(f64::NAN, 40.0)
                .validate(&F2_ULTRA)
                .is_err()
        );
        assert!(
            LaserSettings::blue(50.0, 40.0)
                .with_passes(0)
                .validate(&F2_ULTRA)
                .is_err()
        );
    }

    #[test]
    fn validates_mopa_table() {
        assert!(
            LaserSettings::red(70.0, 40.0, 200, 65)
                .validate(&F2_ULTRA)
                .is_ok()
        );
        assert!(
            LaserSettings::red(70.0, 40.0, 200, 1000)
                .validate(&F2_ULTRA)
                .is_ok()
        );
        assert!(
            LaserSettings::red(70.0, 40.0, 200, 1001)
                .validate(&F2_ULTRA)
                .is_err()
        );
        assert!(
            LaserSettings::red(70.0, 40.0, 2, 4000)
                .validate(&F2_ULTRA)
                .is_ok()
        );
        assert!(
            LaserSettings::red(70.0, 40.0, 201, 65)
                .validate(&F2_ULTRA)
                .is_err()
        );
        assert!(
            LaserSettings::red(70.0, 40.0, 200, 0)
                .validate(&F2_ULTRA)
                .is_err()
        );
        // Blue ignores fiber parameters entirely.
        assert!(
            LaserSettings {
                pulse_width_ns: 1,
                ..LaserSettings::blue(1.0, 2.0)
            }
            .validate(&F2_ULTRA)
            .is_ok()
        );
    }

    #[test]
    fn s_and_f_scaling() {
        let s = LaserSettings::blue(70.0, 40.0);
        assert_eq!((s.s_value(), s.f_value()), (700, 2400));
        assert_eq!(LaserSettings::blue(90.0, 1250.0).f_value(), 75000);
    }
}
