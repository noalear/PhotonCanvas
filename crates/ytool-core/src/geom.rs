//! Minimal geometry: points and polylines in machine millimetres
//! (+X right, +Y down, origin at the top-left of the galvo field).

use crate::error::{JobError, Result};

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Point {
    pub x: f64,
    pub y: f64,
}

impl Point {
    pub const fn new(x: f64, y: f64) -> Self {
        Self { x, y }
    }

    pub fn dist(self, o: Point) -> f64 {
        (self.x - o.x).hypot(self.y - o.y)
    }

    pub fn is_finite(self) -> bool {
        self.x.is_finite() && self.y.is_finite()
    }
}

impl From<(f64, f64)> for Point {
    fn from((x, y): (f64, f64)) -> Self {
        Self::new(x, y)
    }
}

/// A sequence of connected points. Closed polylines repeat the first point at
/// the end when emitted, so the cut returns to where it started.
#[derive(Debug, Clone, PartialEq)]
pub struct Polyline {
    pub points: Vec<Point>,
    pub closed: bool,
}

/// Points closer than this are treated as the same point.
pub const SAME_POINT_MM: f64 = 1e-6;

impl Polyline {
    pub fn open(points: impl IntoIterator<Item = impl Into<Point>>) -> Self {
        Self {
            points: points.into_iter().map(Into::into).collect(),
            closed: false,
        }
    }

    /// A closed loop. If the caller already repeated the first point at the
    /// end, it is dropped so it is not emitted twice.
    pub fn closed(points: impl IntoIterator<Item = impl Into<Point>>) -> Self {
        let mut points: Vec<Point> = points.into_iter().map(Into::into).collect();
        if points.len() > 1 && points[0].dist(*points.last().expect("len>1")) <= SAME_POINT_MM {
            points.pop();
        }
        Self {
            points,
            closed: true,
        }
    }

    /// The points the laser visits, with the closing point appended for
    /// closed loops and consecutive duplicates removed.
    pub fn emitted_points(&self) -> Vec<Point> {
        let mut out: Vec<Point> = Vec::with_capacity(self.points.len() + 1);
        for &p in &self.points {
            if out.last().is_none_or(|q| q.dist(p) > SAME_POINT_MM) {
                out.push(p);
            }
        }
        if self.closed && out.len() > 1 && out[0].dist(*out.last().expect("len>1")) > SAME_POINT_MM
        {
            out.push(out[0]);
        }
        out
    }

    /// True if the laser would actually move while firing.
    pub fn has_extent(&self) -> bool {
        self.emitted_points().len() >= 2
    }

    pub fn length(&self) -> f64 {
        self.emitted_points()
            .windows(2)
            .map(|w| w[0].dist(w[1]))
            .sum()
    }

    pub fn validate(&self) -> Result<()> {
        if self.points.iter().any(|p| !p.is_finite()) {
            return Err(JobError::NotFinite {
                what: "path coordinate",
            });
        }
        Ok(())
    }
}

/// Axis-aligned bounds of a set of points: (min_x, min_y, max_x, max_y).
pub fn bounds(points: impl IntoIterator<Item = Point>) -> Option<(f64, f64, f64, f64)> {
    points.into_iter().fold(None, |acc, p| {
        Some(match acc {
            None => (p.x, p.y, p.x, p.y),
            Some((a, b, c, d)) => (a.min(p.x), b.min(p.y), c.max(p.x), d.max(p.y)),
        })
    })
}

/// Points along an elliptical arc (angles in degrees, +Y down), about `step` mm apart.
pub fn arc(cx: f64, cy: f64, rx: f64, ry: f64, a0: f64, a1: f64, step: f64) -> Vec<Point> {
    let span = (a1 - a0).to_radians();
    let n = ((span.abs() * rx.max(ry)) / step).ceil().max(8.0) as usize;
    (0..=n)
        .map(|i| {
            let a = a0.to_radians() + span * i as f64 / n as f64;
            Point::new(cx + rx * a.cos(), cy + ry * a.sin())
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn closed_polyline_emits_closing_point_once() {
        let sq = Polyline::closed([(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 0.0)]);
        assert_eq!(sq.points.len(), 3);
        let e = sq.emitted_points();
        assert_eq!(e.len(), 4);
        assert_eq!(e[0], e[3]);
    }

    #[test]
    fn duplicates_and_degenerates() {
        let p = Polyline::open([(1.0, 1.0), (1.0, 1.0), (1.0, 1.0)]);
        assert!(!p.has_extent());
        let p = Polyline::closed([(1.0, 1.0)]);
        assert_eq!(p.emitted_points().len(), 1);
        assert!(Polyline::open([(f64::NAN, 0.0)]).validate().is_err());
    }
}
