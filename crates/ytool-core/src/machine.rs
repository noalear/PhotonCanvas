//! Per-model machine limits. Values come from the stock extension for the
//! model (see docs/) or from measurements on the user's machine.

#[derive(Debug, Clone, PartialEq)]
pub struct MachineProfile {
    /// Device code reported by discovery / machineInfo.
    pub device_code: &'static str,
    /// Comment tag used in G-code section markers, e.g. `# GS004-4 HEAD`.
    pub gcode_tag: &'static str,
    /// Galvo field in mm, as written in the G-code header (`gc={"size":...}`).
    pub field_w: f64,
    pub field_h: f64,
    pub min_speed: f64,
    pub max_speed: f64,
    /// Head Z (mm) = material distance + this offset.
    pub z_offset: f64,
    /// Highest Z the head may be sent to.
    pub z_max: f64,
    /// Supported fiber pulse widths (ns) and the max frequency (kHz) for each.
    pub mopa: &'static [MopaLimit],
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct MopaLimit {
    pub pulse_width_ns: u32,
    /// Frequency suggested by the stock software for this pulse width.
    pub default_khz: u32,
    pub max_khz: u32,
}

const fn m(pulse_width_ns: u32, default_khz: u32, max_khz: u32) -> MopaLimit {
    MopaLimit {
        pulse_width_ns,
        default_khz,
        max_khz,
    }
}

/// From the GS004 extension's `H_` table (pulse width → [default, max] kHz).
pub const F2_ULTRA_MOPA: &[MopaLimit] = &[
    m(2, 1950, 4000),
    m(4, 1350, 4000),
    m(6, 850, 4000),
    m(9, 600, 4000),
    m(13, 360, 3000),
    m(20, 252, 3000),
    m(30, 187, 3000),
    m(45, 132, 2000),
    m(60, 115, 2000),
    m(80, 90, 2000),
    m(100, 80, 1000),
    m(150, 70, 1000),
    m(200, 65, 1000),
    m(250, 65, 900),
    m(350, 60, 600),
    m(500, 60, 500),
];

/// xTool F2 Ultra (dual 40 W diode + 60 W MOPA fiber).
pub const F2_ULTRA: MachineProfile = MachineProfile {
    device_code: "GS004-CLASS-4",
    gcode_tag: "GS004-4",
    field_w: 220.0,
    field_h: 220.0,
    min_speed: 2.0,
    max_speed: 15000.0,
    z_offset: 6.0,
    z_max: 156.0,
    mopa: F2_ULTRA_MOPA,
};

impl MachineProfile {
    pub fn mopa_limit(&self, pulse_width_ns: u32) -> Option<&MopaLimit> {
        self.mopa
            .iter()
            .find(|l| l.pulse_width_ns == pulse_width_ns)
    }

    pub fn contains(&self, x: f64, y: f64) -> bool {
        // A hair of tolerance so 220.0000001 from float maths is still inside.
        const EPS: f64 = 1e-6;
        x >= -EPS && y >= -EPS && x <= self.field_w + EPS && y <= self.field_h + EPS
    }

    /// Head Z for a material distance, if reachable.
    pub fn z_for_distance(&self, distance_mm: f64) -> Option<f64> {
        let z = distance_mm + self.z_offset;
        (distance_mm.is_finite() && distance_mm >= 0.0 && z <= self.z_max).then_some(z)
    }
}
