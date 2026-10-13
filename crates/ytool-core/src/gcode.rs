//! G-code for the xTool F2 Ultra galvo, in the dialect documented in
//! `docs/gcode-f2-ultra.md` and verified against four real exports
//! (`tests/samples.rs`).
//!
//! A [`Job`] is a list of [`Block`]s. Each block is one vector path, one
//! filled region, or one dot-mode bitmap, with its own [`LaserSettings`].

use crate::error::{JobError, Result, check_range};
use crate::geom::{Point, Polyline};
use crate::machine::MachineProfile;
use crate::numfmt::{fixed3, js_number, js_rounded, trim3};
use crate::settings::{LaserSettings, LightSource};

/// The last preamble line differs between cut and engrave jobs in the stock
/// software (`M9043 H11…` vs `H1…`). Its meaning is unknown (not air assist);
/// we reproduce it so the machine sees exactly what it is used to.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Preamble {
    Cut,
    Engrave,
}

/// How multiple passes are executed.
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum PassMode {
    /// Stock behaviour: each block repeats its own passes (`G104 P<n>` …
    /// `G105`) before the next block starts.
    PerBlock,
    /// Pass by pass over the whole job: round 1 processes every block once,
    /// then waits `delay_s`, round 2 processes every block that has ≥ 2
    /// passes, and so on. Blocks with fewer passes simply drop out of later
    /// rounds, so mixed layers (engrave ×1, cut ×8) behave naturally.
    Rounds { delay_s: f64 },
}

#[derive(Debug, Clone, PartialEq)]
pub struct VectorBlock {
    pub path: Polyline,
    pub laser: LaserSettings,
}

/// Zigzag line fill of closed polygons, even-odd rule (holes work).
#[derive(Debug, Clone, PartialEq)]
pub struct FillBlock {
    pub polygons: Vec<Vec<Point>>,
    pub laser: LaserSettings,
    pub lines_per_cm: f64,
}

/// A 1-bit image, row-major, `true` = burn. Row 0 is at the smallest Y.
#[derive(Debug, Clone, PartialEq)]
pub struct Bitmap {
    pub width: usize,
    pub height: usize,
    pub bits: Vec<bool>,
}

impl Bitmap {
    pub fn new(width: usize, height: usize) -> Self {
        Self {
            width,
            height,
            bits: vec![false; width * height],
        }
    }

    pub fn get(&self, x: usize, y: usize) -> bool {
        self.bits[y * self.width + x]
    }

    pub fn set(&mut self, x: usize, y: usize, v: bool) {
        self.bits[y * self.width + x] = v;
    }
}

/// Dot-mode bitmap engraving: one laser pulse of `dot_us` per black pixel.
#[derive(Debug, Clone, PartialEq)]
pub struct DotBlock {
    pub bitmap: Bitmap,
    /// Machine position of pixel (0, 0).
    pub origin: Point,
    pub laser: LaserSettings,
    pub dpi: f64,
    pub dot_us: u32,
    /// Lower end of the power range written to `blockConfig` (percent).
    pub min_power: f64,
    /// Dither name written to `blockConfig` (informational).
    pub mode: String,
}

#[derive(Debug, Clone, PartialEq)]
pub enum Block {
    Vector(VectorBlock),
    Fill(FillBlock),
    Dots(DotBlock),
}

impl Block {
    pub fn laser(&self) -> &LaserSettings {
        match self {
            Block::Vector(b) => &b.laser,
            Block::Fill(b) => &b.laser,
            Block::Dots(b) => &b.laser,
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct Job {
    pub machine: MachineProfile,
    pub blocks: Vec<Block>,
    pub pass_mode: PassMode,
    pub preamble: Preamble,
    /// Written to the `# date=` header line, e.g. `2026_10_12_19_02_38`.
    pub created: String,
}

/// Longest single dwell we emit; longer pauses are split into several `G4`s
/// so no firmware integer limit is ever approached.
const MAX_DWELL_MS: u64 = 60_000;
/// Longest pause between passes we accept.
pub const MAX_PASS_DELAY_S: f64 = 24.0 * 3600.0;

impl Job {
    pub fn new(machine: MachineProfile, blocks: Vec<Block>) -> Self {
        let preamble = if blocks.iter().any(|b| matches!(b, Block::Vector(_))) {
            Preamble::Cut
        } else {
            Preamble::Engrave
        };
        Self {
            machine,
            blocks,
            pass_mode: PassMode::PerBlock,
            preamble,
            created: String::new(),
        }
    }

    pub fn with_pass_mode(mut self, mode: PassMode) -> Self {
        self.pass_mode = mode;
        self
    }

    pub fn with_created(mut self, created: impl Into<String>) -> Self {
        self.created = created.into();
        self
    }

    /// Checks every setting and every coordinate the laser will visit.
    pub fn validate(&self) -> Result<()> {
        if self.blocks.is_empty() {
            return Err(JobError::Empty);
        }
        if let PassMode::Rounds { delay_s } = self.pass_mode {
            check_range("pause between passes", delay_s, 0.0, MAX_PASS_DELAY_S, "s")?;
        }
        let mut any_motion = false;
        for (i, block) in self.blocks.iter().enumerate() {
            block.laser().validate(&self.machine)?;
            let ctx = format!(" (item {})", i + 1);
            match block {
                Block::Vector(b) => {
                    b.path.validate()?;
                    any_motion |= b.path.has_extent();
                    for p in b.path.emitted_points() {
                        self.check_point(p, &ctx)?;
                    }
                }
                Block::Fill(b) => {
                    check_range("line density", b.lines_per_cm, 1.0, 1000.0, "lines/cm")?;
                    for p in b.polygons.iter().flatten() {
                        if !p.is_finite() {
                            return Err(JobError::NotFinite {
                                what: "fill coordinate",
                            });
                        }
                        self.check_point(*p, &ctx)?;
                    }
                    any_motion |= !fill_lines(b).is_empty();
                }
                Block::Dots(b) => {
                    check_range("resolution", b.dpi, 10.0, 2540.0, "dpi")?;
                    check_range("dot duration", b.dot_us as f64, 10.0, 100_000.0, "µs")?;
                    if b.dot_us % 10 != 0 {
                        return Err(JobError::Invalid(format!(
                            "dot duration must be a multiple of 10 µs (got {} µs)",
                            b.dot_us
                        )));
                    }
                    check_range("minimum power", b.min_power, 0.0, b.laser.power, "%")?;
                    if b.bitmap.bits.len() != b.bitmap.width * b.bitmap.height {
                        return Err(JobError::Invalid(
                            "bitmap size does not match its data".into(),
                        ));
                    }
                    if !b.origin.is_finite() {
                        return Err(JobError::NotFinite {
                            what: "bitmap position",
                        });
                    }
                    let pitch = 25.4 / b.dpi;
                    // The corners of the pixel grid bound every dot.
                    if b.bitmap.width > 0 && b.bitmap.height > 0 {
                        let far = Point::new(
                            b.origin.x + (b.bitmap.width - 1) as f64 * pitch,
                            b.origin.y + (b.bitmap.height - 1) as f64 * pitch,
                        );
                        self.check_point(b.origin, &ctx)?;
                        self.check_point(far, &ctx)?;
                    }
                    any_motion |= b.bitmap.bits.iter().any(|&v| v);
                }
            }
        }
        if !any_motion {
            return Err(JobError::Empty);
        }
        Ok(())
    }

    fn check_point(&self, p: Point, ctx: &str) -> Result<()> {
        if self.machine.contains(p.x, p.y) {
            Ok(())
        } else {
            Err(JobError::OutsideWorkArea {
                x: p.x,
                y: p.y,
                w: self.machine.field_w,
                h: self.machine.field_h,
                context: ctx.to_string(),
            })
        }
    }

    /// The complete motion file. Validates first.
    pub fn render(&self) -> Result<String> {
        self.validate()?;
        let tag = self.machine.gcode_tag;
        let mut lines = self.header();
        match self.pass_mode {
            PassMode::PerBlock => {
                for b in &self.blocks {
                    lines.extend(render_block(b, tag, b.laser().passes));
                    lines.push(String::new());
                }
            }
            PassMode::Rounds { delay_s } => {
                let rounds = self
                    .blocks
                    .iter()
                    .map(|b| b.laser().passes)
                    .max()
                    .unwrap_or(1);
                for round in 1..=rounds {
                    if round > 1 && delay_s > 0.0 {
                        lines.push(format!("# pass {round} of {rounds}"));
                        lines.extend(dwell(delay_s));
                        lines.push(String::new());
                    }
                    for b in self.blocks.iter().filter(|b| b.laser().passes >= round) {
                        lines.extend(render_block(b, tag, 1));
                        lines.push(String::new());
                    }
                }
            }
        }
        lines.extend(tail(tag));
        Ok(lines.join("\n"))
    }

    fn header(&self) -> Vec<String> {
        let level = match self.preamble {
            Preamble::Cut => 11,
            Preamble::Engrave => 1,
        };
        let m9043: Vec<String> = "HIJKLM".chars().map(|c| format!("{c}{level}")).collect();
        let w = js_number(self.machine.field_w);
        let h = js_number(self.machine.field_h);
        let tag = self.machine.gcode_tag;
        [
            format!("# date={}", self.created),
            "# version=1.9.11".into(),
            "# algorithmVersion=".into(),
            format!("# gc={{\"size\":{{\"w\":{w},\"h\":{h}}}}}"),
            "# gc={\"offset\":{\"x\":0,\"y\":0}}".into(),
            "# gc={\"start\":{\"x\":0,\"y\":0.0000}}".into(),
            "# gc={\"keys\":[\"x\",\"y\"],\"rm\":1,\"is3DMode\":false}".into(),
            "# timeConfig=".into(),
            "G90".into(),
            "G0 F3000".into(),
            "G4P5".into(),
            "G198 P84 \"M9033 V100\"".into(),
            "G198 P85 \"M9401 D2 V500\"".into(),
            "M9064 B3".into(),
            "G198 P70 \"M9064 B3\"".into(),
            "G198 P78 \"M9064 B3\"".into(),
            "M9039 C2".into(),
            "G198 P69 \"M9039 C2\"".into(),
            "G198 P76 \"M9039 V50\"".into(),
            format!("G198 P76 \"M9043 {}\"", m9043.join(" ")),
            "G4P5".into(),
            format!("# {tag} HEAD"),
            "G0 F180000".into(),
            "M4 S0".into(),
            "G1 F180000".into(),
            "G0 X0 Y0".into(),
            String::new(),
        ]
        .into()
    }

    /// The framing file the machine traces at low power when its frame button
    /// is used. `outline` is usually the job's bounding box or hull.
    pub fn border(&self, outline: &[Point]) -> Result<String> {
        if outline.len() < 2 {
            return Err(JobError::Invalid(
                "frame outline needs at least two points".into(),
            ));
        }
        for &p in outline {
            if !p.is_finite() {
                return Err(JobError::NotFinite {
                    what: "frame coordinate",
                });
            }
            self.check_point(p, " (frame)")?;
        }
        let tag = self.machine.gcode_tag;
        let mut lines: Vec<String> = [
            format!("# {tag} HEAD"),
            "G0 F180000".into(),
            "M4 S0".into(),
            "G1 F180000".into(),
            "M114 S1".into(),
            String::new(),
            String::new(),
            format!("# {tag} VECTOR HEAD"),
            "# motion_start".into(),
            String::new(),
            "G21".into(),
            "G90".into(),
            String::new(),
            String::new(),
            String::new(),
            String::new(),
            "# blockConfig={\"powerFactor\":0.064,\"isVector\":true,\"crossDot\":false}".into(),
            format!("G0X{}Y{}", trim3(outline[0].x), trim3(outline[0].y)),
        ]
        .into();
        for (i, p) in outline[1..].iter().enumerate() {
            let mut l = format!("G1X{}Y{}", trim3(p.x), trim3(p.y));
            if i == 0 {
                l.push_str("S64F960000");
            }
            lines.push(l);
        }
        lines.extend([
            String::new(),
            format!("# {tag} VECTOR TAIL"),
            "# motion_end".into(),
        ]);
        lines.extend(tail(tag));
        Ok(lines.join("\n"))
    }

    /// Rough duration in seconds (ignores acceleration; galvo jumps at
    /// 3000 mm/s). Good for comparing settings and showing progress.
    pub fn estimate_seconds(&self) -> f64 {
        const JUMP: f64 = 3000.0;
        let block_time = |b: &Block| -> f64 {
            match b {
                Block::Vector(v) => {
                    v.path.length() / v.laser.speed
                        + v.path.emitted_points().first().map_or(0.0, |_| 0.002)
                }
                Block::Fill(f) => fill_lines(f)
                    .iter()
                    .map(|(_, a, b)| {
                        (b - a).abs() / f.laser.speed + (10.0 / f.lines_per_cm) / f.laser.speed
                    })
                    .sum(),
                Block::Dots(d) => {
                    let pitch = 25.4 / d.dpi;
                    let dots = d.bitmap.bits.iter().filter(|&&v| v).count() as f64;
                    dots * (d.dot_us as f64 * 1e-6 + pitch / d.laser.speed.max(1.0))
                }
            }
        };
        let jump = |b: &Block| match b {
            Block::Vector(_) => 20.0 / JUMP,
            _ => 0.0,
        };
        match self.pass_mode {
            PassMode::PerBlock => self
                .blocks
                .iter()
                .map(|b| (block_time(b) + jump(b)) * b.laser().passes as f64)
                .sum(),
            PassMode::Rounds { delay_s } => {
                let rounds = self
                    .blocks
                    .iter()
                    .map(|b| b.laser().passes)
                    .max()
                    .unwrap_or(1);
                let work: f64 = self
                    .blocks
                    .iter()
                    .map(|b| (block_time(b) + jump(b)) * b.laser().passes as f64)
                    .sum();
                work + delay_s * rounds.saturating_sub(1) as f64
            }
        }
    }
}

fn tail(tag: &str) -> Vec<String> {
    [
        "# END",
        &format!("# {tag} TAIL"),
        "G90",
        "G0 S0",
        "G1 F180000",
        "M536 U0",
        "M6 P1",
        "",
    ]
    .iter()
    .map(|s| s.to_string())
    .collect()
}

fn dwell(delay_s: f64) -> Vec<String> {
    let mut ms = (delay_s * 1000.0).round() as u64;
    let mut out = Vec::new();
    while ms > 0 {
        let chunk = ms.min(MAX_DWELL_MS);
        out.push(format!("G4P{chunk}"));
        ms -= chunk;
    }
    out
}

fn source_lines(l: &LaserSettings, vector: bool) -> Vec<String> {
    let mut v = vec![
        "G4P1".to_string(),
        match l.light {
            LightSource::Blue => "G21",
            LightSource::Red => "G22",
        }
        .to_string(),
    ];
    if vector {
        v.push("G90".into());
    }
    if l.light == LightSource::Red {
        v.push(format!("G0H{}", l.pulse_width_ns));
        v.push(format!("G0Q{}", l.frequency_khz));
    }
    v.extend([String::new(), "G4P1".into()]);
    v
}

fn pass_loop(passes: u32, body: Vec<String>) -> Vec<String> {
    if passes <= 1 {
        return body;
    }
    let mut v = vec![
        "# MUST".to_string(),
        format!("G104 P{passes}"),
        String::new(),
    ];
    v.extend(body);
    v.extend([String::new(), "# MUST".into(), "G105".into()]);
    v
}

fn render_block(block: &Block, tag: &str, passes: u32) -> Vec<String> {
    match block {
        Block::Vector(b) => render_vector(b, tag, passes),
        Block::Fill(b) => render_fill(b, tag, passes),
        Block::Dots(b) => render_dots(b, tag, passes),
    }
}

fn render_vector(b: &VectorBlock, tag: &str, passes: u32) -> Vec<String> {
    let pts = b.path.emitted_points();
    if pts.len() < 2 {
        return Vec::new(); // nothing to burn
    }
    let l = &b.laser;
    let mut body = vec![
        format!(
            "# blockConfig={{\"powerFactor\":{},\"isVector\":true,\"crossDot\":false}}",
            js_rounded(l.power / 100.0, 6)
        ),
        format!("G0X{}Y{}", trim3(pts[0].x), trim3(pts[0].y)),
    ];
    for (i, p) in pts[1..].iter().enumerate() {
        let mut line = format!("G1X{}Y{}", trim3(p.x), trim3(p.y));
        if i == 0 {
            line.push_str(&format!("S{}F{}", l.s_value(), l.f_value()));
        }
        body.push(line);
    }
    let mut v = vec![format!("# {tag} VECTOR HEAD"), "# motion_start".into()];
    v.extend(source_lines(l, true));
    v.extend(["M523P40".into(), String::new(), String::new()]);
    v.extend(pass_loop(passes, body));
    v.extend([
        String::new(),
        format!("# {tag} VECTOR TAIL"),
        "# motion_end".into(),
    ]);
    v
}

/// Scanline spans inside the polygons (even-odd) at height `y`.
fn scan_spans(polygons: &[Vec<Point>], y: f64) -> Vec<(f64, f64)> {
    let mut xs: Vec<f64> = Vec::new();
    for poly in polygons {
        let n = poly.len();
        for i in 0..n {
            let (a, b) = (poly[i], poly[(i + 1) % n]);
            if (a.y <= y && y < b.y) || (b.y <= y && y < a.y) {
                xs.push(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
            }
        }
    }
    xs.sort_by(f64::total_cmp);
    xs.chunks_exact(2)
        .map(|c| (c[0], c[1]))
        .filter(|(a, b)| b - a > 1e-6)
        .collect()
}

/// Every burn segment of a fill, in emission order: (y, x_from, x_to).
fn fill_lines(b: &FillBlock) -> Vec<(f64, f64, f64)> {
    let step = 10.0 / b.lines_per_cm;
    let ys = b.polygons.iter().flatten().map(|p| p.y);
    let (Some(y0), Some(y1)) = (ys.clone().reduce(f64::min), ys.reduce(f64::max)) else {
        return Vec::new();
    };
    let n_lines = ((y1 - y0) / step + 1e-9).floor() as usize;
    let mut out = Vec::new();
    let mut forward = true;
    for i in 0..n_lines {
        let y = y0 + i as f64 * step;
        let mut spans = scan_spans(&b.polygons, y + 1e-9);
        if spans.is_empty() {
            continue;
        }
        if !forward {
            spans = spans.into_iter().rev().map(|(a, b)| (b, a)).collect();
        }
        out.extend(spans.into_iter().map(|(a, b)| (y, a, b)));
        forward = !forward;
    }
    out
}

fn bitmap_cfg(power_factor: f64, density: f64, power: [i64; 2], mode: &str) -> String {
    format!(
        "# blockConfig={{\"powerFactor\":{},\"overDist\":0,\"accel\":5000,\"density\":{},\"power\":[{},{}],\"bitmapMode\":\"{}\",\"crossDot\":false,\"startX\":0,\"isVector\":false}}",
        js_rounded(power_factor, 6),
        js_rounded(density, 4),
        power[0],
        power[1],
        mode
    )
}

fn bitmap_head(tag: &str, l: &LaserSettings, mode_code: &str) -> Vec<String> {
    let mut v = vec![
        format!("# {tag} BITMAP HEAD"),
        "# motion_start".into(),
        String::new(),
        String::new(),
        String::new(),
    ];
    v.extend(source_lines(l, false));
    v.extend([mode_code.to_string(), String::new(), String::new()]);
    v
}

fn bitmap_tail(tag: &str) -> [String; 4] {
    [
        String::new(),
        format!("# {tag} BITMAP TAIL"),
        "# motion_end".into(),
        String::new(),
    ]
}

fn render_fill(b: &FillBlock, tag: &str, passes: u32) -> Vec<String> {
    let segs = fill_lines(b);
    if segs.is_empty() {
        return Vec::new();
    }
    let l = &b.laser;
    let s = l.s_value();
    let mut body = vec![
        bitmap_cfg(l.power / 100.0, b.lines_per_cm / 10.0, [0, s], "grayscale"),
        String::new(),
        String::new(),
        format!("G1F{}S0", l.f_value()),
    ];
    let mut last_x: Option<String> = None;
    for (y, a, bx) in segs {
        let (ya, xa) = (fixed3(y), fixed3(a));
        match &last_x {
            None => body.push(format!("G0X{xa}Y{ya}")),
            Some(lx) if *lx == xa => body.push(format!("G1Y{ya}S0")),
            Some(_) => body.push(format!("G1X{xa}Y{ya}S0")),
        }
        let xb = fixed3(bx);
        body.push(format!("G1X{xb}S{s}"));
        last_x = Some(xb);
    }
    let mut v = bitmap_head(tag, l, "M523P40");
    v.extend(pass_loop(passes, body));
    v.extend(bitmap_tail(tag));
    v
}

fn render_dots(b: &DotBlock, tag: &str, passes: u32) -> Vec<String> {
    let l = &b.laser;
    let pitch = 25.4 / b.dpi;
    let mut body = vec![
        bitmap_cfg(
            l.power / 100.0,
            b.dpi / 25.4,
            [(b.min_power * 10.0).round() as i64, l.s_value()],
            &b.mode,
        ),
        String::new(),
        String::new(),
        format!("G1F{}S0", l.f_value()),
        format!("G25T{}", b.dot_us / 10),
    ];
    let mut first = true;
    let mut forward = true;
    let mut last_x = String::new();
    for r in 0..b.bitmap.height {
        let mut cols: Vec<usize> = (0..b.bitmap.width)
            .filter(|&c| b.bitmap.get(c, r))
            .collect();
        if cols.is_empty() {
            continue;
        }
        if !forward {
            cols.reverse();
        }
        let y = fixed3(b.origin.y + r as f64 * pitch);
        for (k, &c) in cols.iter().enumerate() {
            let x = fixed3(b.origin.x + c as f64 * pitch);
            if first {
                body.push(format!("G0X{x}Y{y}"));
                body.push(String::new());
                body.push(format!("G25X{x}Y{y}S{}", l.s_value()));
                first = false;
            } else if k == 0 {
                body.push(if x == last_x {
                    format!("G25Y{y}")
                } else {
                    format!("G25X{x}Y{y}")
                });
            } else {
                body.push(format!("G25X{x}"));
            }
            last_x = x;
        }
        forward = !forward;
    }
    if first {
        return Vec::new(); // blank bitmap
    }
    let mut v = bitmap_head(tag, l, "M523P10");
    v.extend(pass_loop(passes, body));
    v.extend(bitmap_tail(tag));
    v
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::machine::F2_ULTRA;

    fn square(x: f64, y: f64, s: f64) -> Polyline {
        Polyline::closed([(x, y), (x + s, y), (x + s, y + s), (x, y + s)])
    }

    fn vjob(paths: Vec<Polyline>, laser: LaserSettings) -> Job {
        Job::new(
            F2_ULTRA,
            paths
                .into_iter()
                .map(|path| {
                    Block::Vector(VectorBlock {
                        path,
                        laser: laser.clone(),
                    })
                })
                .collect(),
        )
    }

    #[test]
    fn rejects_empty_and_out_of_bounds() {
        let l = LaserSettings::blue(70.0, 40.0);
        assert_eq!(Job::new(F2_ULTRA, vec![]).render(), Err(JobError::Empty));
        assert_eq!(
            vjob(vec![Polyline::open([(5.0, 5.0)])], l.clone()).render(),
            Err(JobError::Empty)
        );
        assert!(matches!(
            vjob(vec![square(210.0, 10.0, 20.0)], l.clone()).render(),
            Err(JobError::OutsideWorkArea { .. })
        ));
        assert!(matches!(
            vjob(vec![square(-1.0, 10.0, 5.0)], l.clone()).render(),
            Err(JobError::OutsideWorkArea { .. })
        ));
        // Exactly on the field edge is fine.
        assert!(vjob(vec![square(200.0, 200.0, 20.0)], l).render().is_ok());
    }

    #[test]
    fn rounds_mode_runs_whole_job_per_pass_with_mixed_passes() {
        let cut = LaserSettings::blue(70.0, 40.0).with_passes(3);
        let once = LaserSettings::blue(20.0, 400.0);
        let job = Job::new(
            F2_ULTRA,
            vec![
                Block::Vector(VectorBlock {
                    path: square(10.0, 10.0, 5.0),
                    laser: once,
                }),
                Block::Vector(VectorBlock {
                    path: square(30.0, 10.0, 5.0),
                    laser: cut,
                }),
            ],
        )
        .with_pass_mode(PassMode::Rounds { delay_s: 1.5 });
        let g = job.render().expect("valid");
        assert!(
            !g.contains("G104"),
            "rounds mode must not use per-block loops"
        );
        assert_eq!(g.matches("VECTOR HEAD").count(), 1 + 3);
        assert_eq!(g.matches("G4P1500").count(), 2);
        // Round 2 contains only the 3-pass square.
        let r2 = &g
            [g.find("# pass 2 of 3").expect("round 2")..g.find("# pass 3 of 3").expect("round 3")];
        assert!(r2.contains("G0X30Y10") && !r2.contains("G0X10Y10"));
    }

    #[test]
    fn long_pauses_are_chunked_and_zero_pause_emits_nothing() {
        assert_eq!(dwell(125.0), vec!["G4P60000", "G4P60000", "G4P5000"]);
        assert!(dwell(0.0).is_empty());
        let l = LaserSettings::blue(70.0, 40.0).with_passes(2);
        let g = vjob(vec![square(10.0, 10.0, 5.0)], l.clone())
            .with_pass_mode(PassMode::Rounds { delay_s: 0.0 })
            .render()
            .expect("valid");
        assert!(!g.contains("# pass 2"));
        assert!(
            vjob(vec![square(10.0, 10.0, 5.0)], l)
                .with_pass_mode(PassMode::Rounds { delay_s: -1.0 })
                .render()
                .is_err()
        );
    }

    #[test]
    fn fill_handles_holes_and_skips_degenerate_spans() {
        let outer = vec![
            Point::new(10.0, 10.0),
            Point::new(30.0, 10.0),
            Point::new(30.0, 30.0),
            Point::new(10.0, 30.0),
        ];
        let hole = vec![
            Point::new(15.0, 15.0),
            Point::new(25.0, 15.0),
            Point::new(25.0, 25.0),
            Point::new(15.0, 25.0),
        ];
        let b = FillBlock {
            polygons: vec![outer, hole],
            laser: LaserSettings::blue(50.0, 500.0),
            lines_per_cm: 10.0,
        };
        let segs = fill_lines(&b);
        // Lines through the hole have two spans.
        let at20: Vec<_> = segs
            .iter()
            .filter(|(y, _, _)| (*y - 20.0).abs() < 1e-9)
            .collect();
        assert_eq!(at20.len(), 2);
        // A zero-area polygon yields nothing.
        let flat = FillBlock {
            polygons: vec![vec![Point::new(10.0, 10.0), Point::new(20.0, 10.0)]],
            laser: LaserSettings::blue(50.0, 500.0),
            lines_per_cm: 10.0,
        };
        assert!(fill_lines(&flat).is_empty());
    }

    #[test]
    fn blank_bitmap_is_empty_job() {
        let d = DotBlock {
            bitmap: Bitmap::new(10, 10),
            origin: Point::new(10.0, 10.0),
            laser: LaserSettings::blue(90.0, 80.0),
            dpi: 180.0,
            dot_us: 200,
            min_power: 1.0,
            mode: "Jarvis".into(),
        };
        assert_eq!(
            Job::new(F2_ULTRA, vec![Block::Dots(d)]).render(),
            Err(JobError::Empty)
        );
    }
}
