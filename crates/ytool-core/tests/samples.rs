//! Compare our G-code with real xTool Studio exports in `captures/gc/`.
//! Those files are the user's and are not in git; tests skip if absent.

use std::collections::HashSet;
use std::path::PathBuf;

use ytool_core::gcode::{Bitmap, Block, DotBlock, FillBlock, Job, PassMode, Preamble, VectorBlock};
use ytool_core::machine::F2_ULTRA;
use ytool_core::{LaserSettings, Point, Polyline};

fn sample(name: &str) -> Option<String> {
    let p = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../captures/gc")
        .join(name);
    match std::fs::read_to_string(&p) {
        Ok(s) => Some(s),
        Err(_) => {
            eprintln!("skipping: {} not found", p.display());
            None
        }
    }
}

fn norm(s: &str) -> Vec<&str> {
    s.lines()
        .filter(|l| !l.trim().is_empty() && !l.starts_with("# date="))
        .collect()
}

fn assert_same(ours: &str, theirs: &str) {
    let (a, b) = (norm(ours), norm(theirs));
    for (i, (x, y)) in a.iter().zip(&b).enumerate() {
        assert_eq!(x, y, "first difference at line {i}");
    }
    assert_eq!(a.len(), b.len(), "line count differs");
}

fn square() -> Polyline {
    Polyline::closed([
        (100.0, 100.0),
        (120.0, 100.0),
        (120.0, 120.0),
        (100.0, 120.0),
    ])
}

#[test]
fn blue_cut_1pass() {
    let Some(theirs) = sample("blue_cut_70p_40mms_1pass.gc") else {
        return;
    };
    let job = Job::new(
        F2_ULTRA,
        vec![Block::Vector(VectorBlock {
            path: square(),
            laser: LaserSettings::blue(70.0, 40.0),
        })],
    );
    assert_same(&job.render().expect("valid"), &theirs);
}

#[test]
fn red_cut_8pass() {
    let Some(theirs) = sample("red_cut_70p_40mms_8pass_65khz_200ns.gc") else {
        return;
    };
    let laser = LaserSettings::red(70.0, 40.0, 200, 65).with_passes(8);
    let job = Job::new(
        F2_ULTRA,
        vec![Block::Vector(VectorBlock {
            path: square(),
            laser,
        })],
    )
    .with_pass_mode(PassMode::PerBlock);
    assert_same(&job.render().expect("valid"), &theirs);
}

#[test]
fn blue_fill() {
    let Some(theirs) = sample("blue_fill_90p_1250mms_100lpcm.gc") else {
        return;
    };
    let poly = vec![
        Point::new(100.0, 100.0),
        Point::new(120.0, 100.0),
        Point::new(120.0, 120.0),
        Point::new(100.0, 120.0),
    ];
    let job = Job::new(
        F2_ULTRA,
        vec![Block::Fill(FillBlock {
            polygons: vec![poly],
            laser: LaserSettings::blue(90.0, 1250.0),
            lines_per_cm: 100.0,
        })],
    );
    assert_eq!(job.preamble, Preamble::Engrave);
    assert_same(&job.render().expect("valid"), &theirs);
}

#[test]
fn dot_bitmap_emission() {
    let Some(theirs) = sample("blue_image_90p_200us_180dpi_jarvis.gc") else {
        return;
    };
    let pitch = 25.4 / 180.0;
    let (mut x, mut y) = (0.0, 0.0);
    let mut dots = Vec::new();
    for l in theirs
        .lines()
        .filter(|l| l.starts_with("G25X") || l.starts_with("G25Y"))
    {
        let rest = &l[3..];
        let mut it = rest
            .char_indices()
            .filter(|(_, c)| *c == 'X' || *c == 'Y' || *c == 'S')
            .peekable();
        while let Some((i, c)) = it.next() {
            let end = it.peek().map_or(rest.len(), |(j, _)| *j);
            let v: f64 = rest[i + 1..end].parse().expect("number");
            match c {
                'X' => x = v,
                'Y' => y = v,
                _ => {}
            }
        }
        dots.push((x, y));
    }
    let ox = dots.iter().map(|d| d.0).fold(f64::INFINITY, f64::min);
    let oy = dots.iter().map(|d| d.1).fold(f64::INFINITY, f64::min);
    let cells: HashSet<(usize, usize)> = dots
        .iter()
        .map(|(x, y)| {
            (
                ((y - oy) / pitch).round() as usize,
                ((x - ox) / pitch).round() as usize,
            )
        })
        .collect();
    let rows = cells.iter().map(|c| c.0).max().expect("dots") + 1;
    let cols = cells.iter().map(|c| c.1).max().expect("dots") + 1;
    let mut bitmap = Bitmap::new(cols, rows);
    for &(r, c) in &cells {
        bitmap.set(c, r, true);
    }
    let job = Job::new(
        F2_ULTRA,
        vec![Block::Dots(DotBlock {
            bitmap,
            origin: Point::new(ox, oy),
            laser: LaserSettings::blue(90.0, 80.0),
            dpi: 180.0,
            dot_us: 200,
            min_power: 1.0,
            mode: "Jarvis".into(),
        })],
    );
    assert_same(&job.render().expect("valid"), &theirs);
}
