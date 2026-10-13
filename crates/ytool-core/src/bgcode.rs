//! Decoder for xTool's tokenized binary G-code (`motion.bgcode`).
//!
//! - lines end with `0x0A`
//! - `0xC3` starts a comment; the rest of the line is UTF-8 (`# ...`)
//! - `0xE1..=0xFA` are the letters A–Z
//! - `0xC0` space, `0xCE` `.`, `0xCD` `-`, `0xC2` `"`
//! - other bytes are packed BCD digit pairs (`0x43` → `43`)
//!
//! Numbers come out zero-padded (`X0143.6740`); [`tidy`] normalises them.

/// Raw decode. Unknown bytes appear as `<xx>` so new tokens stand out.
pub fn decode(data: &[u8]) -> String {
    let mut out = String::with_capacity(data.len() * 2);
    let mut i = 0;
    while i < data.len() {
        let b = data[i];
        match b {
            0x0A => out.push('\n'),
            0xC3 => {
                let end = data[i..]
                    .iter()
                    .position(|&c| c == 0x0A)
                    .map_or(data.len(), |p| i + p);
                out.push('#');
                out.push_str(&String::from_utf8_lossy(&data[i + 1..end]));
                i = end;
                continue;
            }
            0xE1..=0xFA => out.push((b'A' + (b - 0xE1)) as char),
            0xC0 => out.push(' '),
            0xCE => out.push('.'),
            0xCD => out.push('-'),
            0xC2 => out.push('"'),
            _ if (b >> 4) <= 9 && (b & 0xF) <= 9 => {
                out.push((b'0' + (b >> 4)) as char);
                out.push((b'0' + (b & 0xF)) as char);
            }
            _ => out.push_str(&format!("<{b:02x}>")),
        }
        i += 1;
    }
    out
}

/// Strip BCD padding from every number outside comments (including inside
/// quoted `G198` sub-commands):
/// `X0143.6740` → `X143.674`, `G00` → `G0`.
pub fn tidy(text: &str) -> String {
    text.split('\n')
        .map(tidy_line)
        .collect::<Vec<_>>()
        .join("\n")
}

fn tidy_line(line: &str) -> String {
    if line.starts_with('#') {
        return line.to_string();
    }
    let mut out = String::with_capacity(line.len());
    let chars: Vec<char> = line.chars().collect();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        out.push(c);
        i += 1;
        if c.is_ascii_uppercase() {
            let start = i;
            while i < chars.len()
                && (chars[i].is_ascii_digit() || chars[i] == '.' || chars[i] == '-')
            {
                i += 1;
            }
            if i > start {
                let num: String = chars[start..i].iter().collect();
                out.push_str(&normalise(&num));
            }
        }
    }
    out
}

fn normalise(num: &str) -> String {
    let (neg, n) = match num.strip_prefix('-') {
        Some(rest) => (true, rest),
        None => (false, num),
    };
    let body = match n.split_once('.') {
        Some((whole, frac)) => {
            let whole = whole.trim_start_matches('0');
            let frac = frac.trim_end_matches('0');
            let whole = if whole.is_empty() { "0" } else { whole };
            if frac.is_empty() {
                whole.to_string()
            } else {
                format!("{whole}.{frac}")
            }
        }
        None => {
            let w = n.trim_start_matches('0');
            if w.is_empty() {
                "0".to_string()
            } else {
                w.to_string()
            }
        }
    };
    if neg && body != "0" {
        format!("-{body}")
    } else {
        body
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn decodes_tokens() {
        let raw: &[u8] = b"\xc3 GS004-4 HEAD\n\xe7\x00\xf8\x01\x43\xce\x67\x40\xf9\x01\x04\xce\x87\x40\n\xe7\x01\xf3\x07\x00\xe6\x24\x00\n\xed\x05\x23\xf0\x40\n\xe7\x01\x98\xc0\xf0\x84\xc0\xc2\xed\x90\x33\xc0\xf6\x01\x00\xc2\n";
        assert_eq!(
            tidy(&decode(raw)).lines().collect::<Vec<_>>(),
            vec![
                "# GS004-4 HEAD",
                "G0X143.674Y104.874",
                "G1S700F2400",
                "M523P40",
                "G198 P84 \"M9033 V100\""
            ]
        );
    }

    #[test]
    fn unknown_bytes_are_visible() {
        assert_eq!(decode(b"\xe7\xab"), "G<ab>");
    }
}
