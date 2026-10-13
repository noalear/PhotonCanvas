//! `.xf` job package: a POSIX ustar archive the F2 Ultra accepts as a job.
//!
//! Members (in this order, mode 0666, uid/gid 0):
//! `preview.jpg`, `border.gcode`, `motion.gcode`, `description.json`.

use std::io::Cursor;

use image::{ImageBuffer, Rgb};
use serde_json::{Value, json};

use crate::error::{JobError, Result};
use crate::geom::{Point, bounds};

pub struct XfParts<'a> {
    pub motion: &'a str,
    pub border: &'a str,
    pub preview_jpeg: &'a [u8],
    /// `"blue"`, `"red"` or `"mixed"`.
    pub light: &'a str,
    /// One entry per processed item (informational; shown on the machine).
    pub params: Vec<Value>,
    /// `YYYY/MM/DD HH:MM:SS`
    pub create_time: &'a str,
    /// Seconds since the epoch, used as the tar member mtime.
    pub mtime: u64,
}

pub fn build(parts: &XfParts) -> Result<Vec<u8>> {
    let desc = json!({
        "mode": "LASER_PLANE",
        "light": parts.light,
        "create_time": parts.create_time,
        "axis": "",
        "processingParams": parts.params,
        "material": 1,
    });
    let desc = serde_json::to_vec(&desc).map_err(|e| JobError::Invalid(e.to_string()))?;
    let members: [(&str, &[u8]); 4] = [
        ("preview.jpg", parts.preview_jpeg),
        ("border.gcode", parts.border.as_bytes()),
        ("motion.gcode", parts.motion.as_bytes()),
        ("description.json", &desc),
    ];
    let mut builder = tar::Builder::new(Vec::new());
    for (name, data) in members {
        let mut h = tar::Header::new_ustar();
        h.set_path(name).map_err(io_err)?;
        h.set_size(data.len() as u64);
        h.set_mode(0o666);
        h.set_uid(0);
        h.set_gid(0);
        h.set_mtime(parts.mtime);
        h.set_cksum();
        builder.append(&h, data).map_err(io_err)?;
    }
    builder.into_inner().map_err(io_err)
}

fn io_err(e: std::io::Error) -> JobError {
    JobError::Invalid(format!("could not build job package: {e}"))
}

/// 200×200 JPEG thumbnail of polylines, black on white.
pub fn preview_jpeg(paths: &[Vec<Point>]) -> Result<Vec<u8>> {
    const SIZE: u32 = 200;
    let mut img: ImageBuffer<Rgb<u8>, Vec<u8>> =
        ImageBuffer::from_pixel(SIZE, SIZE, Rgb([255, 255, 255]));
    if let Some((x0, y0, x1, y1)) = bounds(paths.iter().flatten().copied()) {
        let scale = (SIZE as f64 - 20.0) / (x1 - x0).max(y1 - y0).max(1e-6);
        let to_px = |p: Point| ((p.x - x0) * scale + 10.0, (p.y - y0) * scale + 10.0);
        for path in paths {
            for w in path.windows(2) {
                draw_line(&mut img, to_px(w[0]), to_px(w[1]));
            }
        }
    }
    let mut out = Cursor::new(Vec::new());
    image::codecs::jpeg::JpegEncoder::new_with_quality(&mut out, 85)
        .encode_image(&img)
        .map_err(|e| JobError::Invalid(format!("preview encode failed: {e}")))?;
    Ok(out.into_inner())
}

fn draw_line(img: &mut ImageBuffer<Rgb<u8>, Vec<u8>>, a: (f64, f64), b: (f64, f64)) {
    let steps = ((b.0 - a.0).abs().max((b.1 - a.1).abs()) * 2.0)
        .ceil()
        .max(1.0) as usize;
    for i in 0..=steps {
        let t = i as f64 / steps as f64;
        let (x, y) = (a.0 + (b.0 - a.0) * t, a.1 + (b.1 - a.1) * t);
        for (dx, dy) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
            let (px, py) = (x as i64 + dx, y as i64 + dy);
            if px >= 0 && py >= 0 && (px as u32) < img.width() && (py as u32) < img.height() {
                img.put_pixel(px as u32, py as u32, Rgb([0, 0, 0]));
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn builds_ustar_with_expected_members() {
        let jpg =
            preview_jpeg(&[vec![Point::new(0.0, 0.0), Point::new(10.0, 10.0)]]).expect("jpeg");
        assert_eq!(&jpg[..2], &[0xFF, 0xD8]);
        let xf = build(&XfParts {
            motion: "G0X0Y0",
            border: "G0X0Y0",
            preview_jpeg: &jpg,
            light: "blue",
            params: vec![],
            create_time: "2026/10/12 00:00:00",
            mtime: 1_791_800_000,
        })
        .expect("xf");
        assert_eq!(&xf[257..263], b"ustar\0");
        let mut ar = tar::Archive::new(Cursor::new(xf));
        let names: Vec<String> = ar
            .entries()
            .expect("entries")
            .map(|e| {
                e.expect("entry")
                    .path()
                    .expect("path")
                    .display()
                    .to_string()
            })
            .collect();
        assert_eq!(
            names,
            [
                "preview.jpg",
                "border.gcode",
                "motion.gcode",
                "description.json"
            ]
        );
    }
}
