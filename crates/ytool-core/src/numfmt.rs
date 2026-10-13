//! Number formatting that matches what xTool's generator writes.

/// Vector coordinates: up to 3 decimals, trailing zeros trimmed (`100`, `154.101`).
pub fn trim3(v: f64) -> String {
    let s = format!("{v:.3}");
    let s = s.trim_end_matches('0').trim_end_matches('.');
    match s {
        "-0" | "" => "0".to_string(),
        _ => s.to_string(),
    }
}

/// Fill/bitmap coordinates: always 3 decimals (`100.000`).
pub fn fixed3(v: f64) -> String {
    let s = format!("{v:.3}");
    if s == "-0.000" {
        "0.000".to_string()
    } else {
        s
    }
}

/// A number as JavaScript's `JSON.stringify` prints it: whole values without
/// `.0`, others in shortest round-trip form.
pub fn js_number(v: f64) -> String {
    if v.is_finite() && v.fract() == 0.0 && v.abs() < 1e15 {
        format!("{}", v as i64)
    } else {
        serde_json::Number::from_f64(v)
            .map(|n| n.to_string())
            .unwrap_or_else(|| "null".into())
    }
}

/// Round to `digits` decimals the way the prototype did (`round(x, 6)`), then
/// print JS-style.
pub fn js_rounded(v: f64, digits: i32) -> String {
    let p = 10f64.powi(digits);
    js_number((v * p).round() / p)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats() {
        assert_eq!(trim3(100.0), "100");
        assert_eq!(trim3(154.1010), "154.101");
        assert_eq!(trim3(-0.0001), "0");
        assert_eq!(trim3(0.5), "0.5");
        assert_eq!(fixed3(100.0), "100.000");
        assert_eq!(fixed3(-0.0001), "0.000");
        assert_eq!(js_number(10.0), "10");
        assert_eq!(js_number(0.7), "0.7");
        assert_eq!(js_rounded(7.086614173228346, 4), "7.0866");
        assert_eq!(js_rounded(0.064, 6), "0.064");
    }
}
