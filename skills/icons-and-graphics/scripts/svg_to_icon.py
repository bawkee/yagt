#!/usr/bin/env python3
"""Convert a filled SVG icon into an Avalonia StreamGeometry for src/Resources/Icons.axaml.

Example:
  python skills/icons-and-graphics/scripts/svg_to_icon.py prune.svg --name Icons.Worktree.Prune --insert

* Flattens path/rect/circle/ellipse/polygon/polyline (with nested transforms) into one
  geometry, fitted to a 1024 box (aspect kept), integer coordinates, relative commands.
* Converts fills only. Stroke-only shapes are rejected: outline them first
  (Inkscape: Path > Stroke to Path) or use the icon set's filled variant.
* SVG defaults to the nonzero fill rule and StreamGeometry to EvenOdd, so nonzero
  output gets an "F1 " prefix. Separate nonzero shapes are re-wound so overlaps
  union as in the SVG; arcs that rounding would distort are emitted as cubics.
* Single color only: differently filled (e.g. light "cutout") shapes are merged and warned about.
* Prints the <StreamGeometry> line. --insert adds it to Icons.axaml in sorted
  position (--replace overwrites an existing key).
"""

import argparse
import math
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ICONS = ROOT / "src" / "Resources" / "Icons.axaml"
RE_NAME = re.compile(r"^Icons(\.[A-Z][A-Za-z0-9]*)+$")
RE_ICON_LINE = re.compile(r'^\s*<StreamGeometry x:Key="([^"]+)">')
RE_NUM = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
CONTAINERS = {"svg", "g", "a", "switch"}
SKIPPED = {"defs", "clipPath", "mask", "symbol", "title", "desc", "metadata", "style",
           "linearGradient", "radialGradient", "pattern", "filter", "marker"}
UNSUPPORTED = {"use", "text", "image", "foreignObject"}
STYLE_KEYS = ("fill", "fill-rule", "stroke", "display", "visibility")
NO_PAINT = ("none", "transparent")
IDENTITY = (1.0, 0.0, 0.0, 1.0, 0.0, 0.0)


class IconError(Exception):
    pass


# --- affine matrices (a, b, c, d, e, f): x' = a*x + c*y + e, y' = b*x + d*y + f -----------

def mul(m, n):
    a1, b1, c1, d1, e1, f1 = m
    a2, b2, c2, d2, e2, f2 = n
    return (a1 * a2 + c1 * b2, b1 * a2 + d1 * b2, a1 * c2 + c1 * d2, b1 * c2 + d1 * d2,
            a1 * e2 + c1 * f2 + e1, b1 * e2 + d1 * f2 + f1)


def apply(m, x, y):
    a, b, c, d, e, f = m
    return a * x + c * y + e, b * x + d * y + f


def parse_transform(text):
    m = IDENTITY
    for name, args in re.findall(r"([A-Za-z]+)\s*\(([^)]*)\)", text or ""):
        v = [float(n) for n in RE_NUM.findall(args)]
        if name == "matrix" and len(v) == 6:
            t = tuple(v)
        elif name == "translate" and v:
            t = (1, 0, 0, 1, v[0], v[1] if len(v) > 1 else 0)
        elif name == "scale" and v:
            t = (v[0], 0, 0, v[1] if len(v) > 1 else v[0], 0, 0)
        elif name == "rotate" and v:
            r = math.radians(v[0])
            t = (math.cos(r), math.sin(r), -math.sin(r), math.cos(r), 0, 0)
            if len(v) == 3:
                t = mul(mul((1, 0, 0, 1, v[1], v[2]), t), (1, 0, 0, 1, -v[1], -v[2]))
        elif name == "skewX" and v:
            t = (1, 0, math.tan(math.radians(v[0])), 1, 0, 0)
        elif name == "skewY" and v:
            t = (1, math.tan(math.radians(v[0])), 0, 1, 0, 0)
        else:
            raise IconError(f"unsupported transform: {name}({args})")
        m = mul(m, t)
    return m


# --- path data -> absolute segments ---------------------------------------------------------
# ("M", x, y) ("L", x, y) ("C", x1, y1, x2, y2, x, y) ("A", x0, y0, rx, ry, phi, large, sweep, x, y) ("Z",)

def parse_path(d):
    i, n = 0, len(d)

    def skip():
        nonlocal i
        while i < n and d[i] in " \t\r\n,":
            i += 1

    def number():
        nonlocal i
        skip()
        m = RE_NUM.match(d, i)
        if not m:
            raise IconError(f"bad path data near {d[i:i + 24]!r}")
        i = m.end()
        return float(m.group())

    def flag():
        nonlocal i
        skip()
        if i < n and d[i] in "01":
            i += 1
            return d[i - 1] == "1"
        raise IconError(f"bad arc flag near {d[i:i + 24]!r}")

    segs = []
    cx = cy = sx = sy = 0.0
    cmd, prev, ctrl = None, None, None
    while True:
        skip()
        if i >= n:
            return segs
        if d[i].isalpha():
            cmd = d[i]
            i += 1
            if cmd.upper() not in "MLHVCSQTAZ":
                raise IconError(f"unknown path command {cmd!r}")
        elif cmd is None:
            raise IconError(f"expected a path command near {d[i:i + 24]!r}")

        up, rel = cmd.upper(), cmd.islower()
        ox, oy = (cx, cy) if rel else (0.0, 0.0)
        if prev == "Z" and up not in "MZ":
            segs.append(("M", sx, sy))
        if up == "Z":
            segs.append(("Z",))
            cx, cy, cmd = sx, sy, None
        elif up == "M":
            cx, cy = sx, sy = number() + ox, number() + oy
            segs.append(("M", cx, cy))
            cmd = "l" if rel else "L"
        elif up in "LHV":
            x = number() + ox if up != "V" else cx
            y = number() + oy if up != "H" else cy
            segs.append(("L", x, y))
            cx, cy = x, y
        elif up in "CS":
            if up == "C":
                x1, y1 = number() + ox, number() + oy
            else:
                x1, y1 = (2 * cx - ctrl[0], 2 * cy - ctrl[1]) if prev in ("C", "S") else (cx, cy)
            x2, y2, x, y = number() + ox, number() + oy, number() + ox, number() + oy
            segs.append(("C", x1, y1, x2, y2, x, y))
            ctrl, cx, cy = (x2, y2), x, y
        elif up in "QT":
            if up == "Q":
                qx, qy = number() + ox, number() + oy
            else:
                qx, qy = (2 * cx - ctrl[0], 2 * cy - ctrl[1]) if prev in ("Q", "T") else (cx, cy)
            x, y = number() + ox, number() + oy
            segs.append(("C", cx + 2 / 3 * (qx - cx), cy + 2 / 3 * (qy - cy),
                         x + 2 / 3 * (qx - x), y + 2 / 3 * (qy - y), x, y))
            ctrl, cx, cy = (qx, qy), x, y
        else:
            rx, ry, phi = number(), number(), number()
            large, sweep = flag(), flag()
            x, y = number() + ox, number() + oy
            segs.append(("A", cx, cy, rx, ry, phi, large, sweep, x, y))
            cx, cy = x, y
        prev = up


def arc_center(x0, y0, rx, ry, phi, large, sweep, x, y):
    """Endpoint -> center parameterization (SVG 1.1 notes F.6.5/F.6.6); radii are scaled up if too small."""
    rx, ry = abs(rx), abs(ry)
    cp, sp = math.cos(math.radians(phi)), math.sin(math.radians(phi))
    hx, hy = (x0 - x) / 2, (y0 - y) / 2
    x1p, y1p = cp * hx + sp * hy, -sp * hx + cp * hy
    lam = (x1p / rx) ** 2 + (y1p / ry) ** 2
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
    co = math.sqrt(max(0.0, num / den)) * (-1 if large == sweep else 1)
    cxp, cyp = co * rx * y1p / ry, -co * ry * x1p / rx
    ccx, ccy = cp * cxp - sp * cyp + (x0 + x) / 2, sp * cxp + cp * cyp + (y0 + y) / 2

    def angle(ux, uy, vx, vy):
        return math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)

    ux, uy = (x1p - cxp) / rx, (y1p - cyp) / ry
    t1 = angle(1, 0, ux, uy)
    dt = angle(ux, uy, (-x1p - cxp) / rx, (-y1p - cyp) / ry)
    if not sweep and dt > 0:
        dt -= 2 * math.pi
    elif sweep and dt < 0:
        dt += 2 * math.pi
    return ccx, ccy, rx, ry, cp, sp, t1, dt


def arc_to_curves(x0, y0, rx, ry, phi, large, sweep, x, y):
    """SVG arc -> cubic segments (at most 90 degrees each)."""
    if (x0, y0) == (x, y):
        return []
    if rx == 0 or ry == 0:
        return [("L", x, y)]
    ccx, ccy, rx, ry, cp, sp, t1, dt = arc_center(x0, y0, rx, ry, phi, large, sweep, x, y)
    count = max(1, math.ceil(abs(dt) / (math.pi / 2) - 1e-9))
    step = dt / count
    k = 4 / 3 * math.tan(step / 4)

    def pt(px, py):
        return ccx + rx * cp * px - ry * sp * py, ccy + rx * sp * px + ry * cp * py

    out = []
    for s in range(count):
        a1, a2 = t1 + s * step, t1 + (s + 1) * step
        p1 = pt(math.cos(a1) - k * math.sin(a1), math.sin(a1) + k * math.cos(a1))
        p2 = pt(math.cos(a2) + k * math.sin(a2), math.sin(a2) - k * math.cos(a2))
        p3 = (x, y) if s == count - 1 else pt(math.cos(a2), math.sin(a2))
        out.append(("C", *p1, *p2, *p3))
    return out


def transform_segments(segs, m):
    """Apply m. Arcs survive rotation/uniform scale; anything else turns them into cubics."""
    a, b, c, d = m[:4]
    det = a * d - b * c
    scale = math.sqrt(abs(det))
    tol = 1e-6 * max(scale, 1e-12)
    similar = det > 0 and abs(a - d) <= tol and abs(b + c) <= tol
    rot = math.degrees(math.atan2(b, a))
    out = []
    for seg in segs:
        kind = seg[0]
        if kind == "Z":
            out.append(seg)
        elif kind in ("M", "L"):
            out.append((kind, *apply(m, seg[1], seg[2])))
        elif kind == "C":
            out.append(("C", *apply(m, seg[1], seg[2]), *apply(m, seg[3], seg[4]), *apply(m, seg[5], seg[6])))
        else:
            x0, y0, rx, ry, phi, large, sweep, x, y = seg[1:]
            if (x0, y0) == (x, y):
                continue
            if similar and rx and ry:
                out.append(("A", *apply(m, x0, y0), abs(rx) * scale, abs(ry) * scale,
                            (phi + rot) % 360, large, sweep, *apply(m, x, y)))
            else:
                out.extend(transform_segments(arc_to_curves(*seg[1:]), m))
    return out


def bounds(segs):
    xs, ys = [], []
    for seg in segs:
        if seg[0] == "A":
            seg_pts = [v for c in arc_to_curves(*seg[1:]) for v in c[1:]]
        else:
            seg_pts = seg[1:]
        xs.extend(seg_pts[0::2])
        ys.extend(seg_pts[1::2])
    if not xs:
        raise IconError("no filled geometry found")
    return min(xs), min(ys), max(xs), max(ys)


def subpaths(segs):
    out = []
    for seg in segs:
        if seg[0] == "M" or not out:
            out.append([])
        out[-1].append(seg)
    return out


def signed_area(segs):
    total = 0.0
    for sub in subpaths(segs):
        pts = []
        for seg in sub:
            for p in (arc_to_curves(*seg[1:]) if seg[0] == "A" else [seg]):
                if p[0] == "C" and pts:
                    (x0, y0), (x1, y1, x2, y2, x3, y3) = pts[-1], p[1:]
                    for t in (0.25, 0.5, 0.75):
                        u = 1 - t
                        pts.append((u ** 3 * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t ** 3 * x3,
                                    u ** 3 * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t ** 3 * y3))
                if p[0] != "Z":
                    pts.append(tuple(p[-2:]))
        total += sum(ax * by - bx * ay for (ax, ay), (bx, by) in zip(pts, pts[1:] + pts[:1])) / 2
    return total


def reverse(segs):
    out = []
    for sub in subpaths(segs):
        closed = sub[-1][0] == "Z"
        body = [s for s in sub[1:] if s[0] != "Z"]
        pts = [tuple(sub[0][1:3])] + [tuple(s[-2:]) for s in body]
        if closed and pts[-1] != pts[0]:
            body.append(("L", *pts[0]))
            pts.append(pts[0])
        out.append(("M", *pts[-1]))
        for s, start in zip(reversed(body), reversed(pts[:-1])):
            if s[0] == "L":
                out.append(("L", *start))
            elif s[0] == "C":
                out.append(("C", s[3], s[4], s[1], s[2], *start))
            else:
                out.append(("A", s[8], s[9], s[3], s[4], s[5], s[6], not s[7], *start))
        if closed:
            out.append(("Z",))
    return out


# --- SVG elements -----------------------------------------------------------------------------

def local(tag):
    return tag.rsplit("}", 1)[-1] if isinstance(tag, str) else ""


def declarations(text):
    return {k.strip(): v.strip() for k, _, v in (p.partition(":") for p in text.split(";")) if v.strip()}


def css_class_rules(root):
    rules = {}
    for el in root.iter():
        if local(el.tag) == "style":
            for selectors, body in re.findall(r"([^{}]+)\{([^}]*)\}", el.text or ""):
                for sel in selectors.split(","):
                    sel = sel.strip()
                    if re.fullmatch(r"\.[\w-]+", sel):
                        rules.setdefault(sel[1:], {}).update(declarations(body))
    return rules


def length(el, attr, default=0.0):
    raw = (el.get(attr) or "").strip()
    if not raw:
        return default
    m = re.fullmatch(r"([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)(px)?", raw)
    if not m:
        raise IconError(f"unsupported length {attr}={raw!r} (use plain user units)")
    return float(m.group(1))


def shape_path(tag, el):
    f = lambda attr, default=0.0: length(el, attr, default)  # noqa: E731
    if tag == "path":
        return el.get("d") or ""
    if tag == "rect":
        x, y, w, h = f("x"), f("y"), f("width"), f("height")
        if w <= 0 or h <= 0:
            return ""
        rx, ry = el.get("rx"), el.get("ry")
        rx, ry = (f("rx") if rx else None), (f("ry") if ry else None)
        rx, ry = (rx if rx is not None else ry or 0), (ry if ry is not None else rx or 0)
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        if not rx or not ry:
            return f"M{x} {y}H{x + w}V{y + h}H{x}Z"
        return (f"M{x + rx} {y}H{x + w - rx}A{rx} {ry} 0 0 1 {x + w} {y + ry}V{y + h - ry}"
                f"A{rx} {ry} 0 0 1 {x + w - rx} {y + h}H{x + rx}A{rx} {ry} 0 0 1 {x} {y + h - ry}"
                f"V{y + ry}A{rx} {ry} 0 0 1 {x + rx} {y}Z")
    if tag in ("circle", "ellipse"):
        cx, cy = f("cx"), f("cy")
        rx, ry = (f("r"), f("r")) if tag == "circle" else (f("rx"), f("ry"))
        if rx <= 0 or ry <= 0:
            return ""
        arc = f"A{rx} {ry} 0 0 1"
        return (f"M{cx - rx} {cy}{arc} {cx} {cy - ry}{arc} {cx + rx} {cy}"
                f"{arc} {cx} {cy + ry}{arc} {cx - rx} {cy}Z")
    if tag in ("polygon", "polyline"):
        pts = RE_NUM.findall(el.get("points") or "")
        return f"M{' '.join(pts)}Z" if len(pts) >= 4 else ""
    return None


class Collected:
    def __init__(self):
        self.shapes, self.rules, self.fills, self.strokes, self.warnings = [], set(), set(), [], []


def walk(el, ctm, inherited, rules, out):
    tag = local(el.tag)
    if tag in SKIPPED:
        return
    if tag in UNSUPPORTED:
        raise IconError(f"<{tag}> is not supported; ungroup/flatten the SVG in a vector editor first")

    style = dict(inherited)
    style.update({k: el.get(k) for k in STYLE_KEYS if el.get(k)})
    for cls in (el.get("class") or "").split():
        style.update({k: v for k, v in rules.get(cls, {}).items() if k in STYLE_KEYS})
    style.update({k: v for k, v in declarations(el.get("style") or "").items() if k in STYLE_KEYS})
    if style.get("display") == "none" or style.get("visibility") == "hidden":
        return

    ctm = mul(ctm, parse_transform(el.get("transform")))
    if tag in CONTAINERS:
        for child in el:
            walk(child, ctm, {k: v for k, v in style.items() if k != "display"}, rules, out)
        return

    d = shape_path(tag, el) if tag != "line" else ""
    if d is None:
        out.warnings.append(f"ignored <{tag}>")
        return

    fill = style.get("fill", "black").lower()
    stroked = style.get("stroke", "none").lower() not in NO_PAINT
    if fill.startswith("url("):
        out.warnings.append(f"<{tag}> gradient/pattern fill rendered as a solid fill")
    if fill in NO_PAINT or tag == "line":
        if stroked:
            out.strokes.append(tag)
        return
    if stroked:
        out.warnings.append(f"<{tag}> stroke ignored (only fills are converted)")
    if d:
        out.rules.add(style.get("fill-rule", "nonzero"))
        out.fills.add(fill)
        out.shapes.append(transform_segments(parse_path(d), ctm))


# --- output -----------------------------------------------------------------------------------

def fmt(v, precision):
    r = round(v, precision)
    if r == 0:
        r = 0.0
    s = f"{r:.{precision}f}"
    return s.rstrip("0").rstrip(".") if "." in s else s


def nums(values, precision):
    out = ""
    for v in values:
        s = fmt(v, precision)
        out += s if not out or s.startswith("-") else " " + s
    return out


def emit(segs, precision):
    r = lambda v: round(v, precision)  # noqa: E731
    unit = 10 ** -precision
    parts = []
    cx = cy = sx = sy = 0.0

    def put(seg):
        nonlocal cx, cy, sx, sy
        kind = seg[0]
        if kind == "Z":
            parts.append("z")
            cx, cy = sx, sy
            return
        x, y = r(seg[-2]), r(seg[-1])
        if kind == "M":
            parts.append("M" + nums((x, y), precision))
            sx, sy = x, y
        elif kind == "L":
            if (x, y) == (cx, cy):
                return
            if y == cy:
                parts.append("h" + nums((x - cx,), precision))
            elif x == cx:
                parts.append("v" + nums((y - cy,), precision))
            else:
                parts.append("l" + nums((x - cx, y - cy), precision))
        elif kind == "C":
            parts.append("c" + nums((r(seg[1]) - cx, r(seg[2]) - cy, r(seg[3]) - cx, r(seg[4]) - cy,
                                     x - cx, y - cy), precision))
        else:
            if (x, y) == (cx, cy):
                return
            rx, ry, phi = max(r(seg[3]), unit), max(r(seg[4]), unit), r(seg[5]) % 360
            want = arc_center(*seg[1:])
            got = arc_center(cx, cy, rx, ry, phi, seg[6], seg[7], x, y)
            if max(abs(a - b) for a, b in zip(want[:4], got[:4])) > unit:
                # Rounding moved the arc (typical for near-semicircles): emit exact cubics instead.
                for c in arc_to_curves(*seg[1:]):
                    put(c)
                return
            flags = f" {int(seg[6])} {int(seg[7])} "
            parts.append("a" + nums((rx, ry, phi), precision) + flags + nums((x - cx, y - cy), precision))
        cx, cy = x, y

    for seg in segs:
        put(seg)
    return "".join(parts)


def convert(svg_text, size, precision, fill_rule):
    try:
        root = ET.fromstring(svg_text)
    except ET.ParseError as e:
        raise IconError(f"invalid SVG: {e}") from e

    out = Collected()
    walk(root, IDENTITY, {}, css_class_rules(root), out)
    if out.strokes:
        raise IconError(f"stroke-only shapes ({', '.join(sorted(set(out.strokes)))}) cannot be converted; "
                        "outline strokes first (Inkscape: Path > Stroke to Path) or use a filled icon variant")

    if fill_rule == "auto":
        if len(out.rules) > 1:
            out.warnings.append("mixed fill rules; using nonzero (override with --fill-rule)")
        fill_rule = "evenodd" if out.rules == {"evenodd"} else "nonzero"
    if len(out.fills) > 1:
        out.warnings.append(f"fills {sorted(out.fills)} merged into one color; light 'cutout' shapes will be filled")

    shapes = out.shapes
    if len(shapes) > 1:
        if fill_rule == "nonzero":
            # SVG paints shapes separately (union); give them one winding so overlaps don't cancel out.
            shapes = [reverse(s) if signed_area(s) < 0 else s for s in shapes]
        else:
            out.warnings.append(f"{len(shapes)} shapes merged with evenodd; overlapping shapes cut holes "
                                "(try --fill-rule nonzero)")
    merged = [seg for s in shapes for seg in s]

    x0, y0, x1, y1 = bounds(merged)
    extent = max(x1 - x0, y1 - y0)
    if extent <= 0:
        raise IconError("geometry has zero size")
    k = size / extent
    merged = transform_segments(merged, (k, 0, 0, k, -x0 * k, -y0 * k))

    prefix = "F1 " if fill_rule == "nonzero" else ""
    return prefix + emit(merged, precision), out.warnings


def insert(name, data, replace):
    raw = ICONS.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    entry = f'  <StreamGeometry x:Key="{name}">{data}</StreamGeometry>'

    keys = [(i, m.group(1)) for i, line in enumerate(lines) if (m := RE_ICON_LINE.match(line))]
    existing = [i for i, key in keys if key == name]
    if existing:
        if not replace:
            raise IconError(f"{name} already exists in Icons.axaml (pass --replace to overwrite)")
        lines[existing[0]] = entry
        action = "replaced"
    else:
        at = next((i for i, key in keys if key.lower() > name.lower()), None)
        if at is None:
            at = next(i for i, line in enumerate(lines) if line.strip() == "</ResourceDictionary>")
        lines.insert(at, entry)
        action = "inserted"
    ICONS.write_bytes((("\ufeff" if bom else "") + nl.join(lines)).encode("utf-8"))
    return action


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("svg", help="path to the .svg file ('-' reads stdin)")
    ap.add_argument("--name", default="Icons.New", help="resource key, e.g. Icons.Branch.Add")
    ap.add_argument("--insert", action="store_true", help="add the entry to src/Resources/Icons.axaml")
    ap.add_argument("--replace", action="store_true", help="with --insert, overwrite an existing key")
    ap.add_argument("--fill-rule", choices=("auto", "nonzero", "evenodd"), default="auto")
    ap.add_argument("--size", type=float, default=1024, help="fit box (default 1024)")
    ap.add_argument("--precision", type=int, default=0, help="decimal places (default 0)")
    args = ap.parse_args()

    try:
        if not RE_NAME.match(args.name):
            raise IconError(f"--name must look like Icons.Name or Icons.Group.Variant (PascalCase), got {args.name!r}")
        svg_text = sys.stdin.read() if args.svg == "-" else Path(args.svg).read_text(encoding="utf-8-sig")
        data, warnings = convert(svg_text, args.size, args.precision, args.fill_rule)
        for w in warnings:
            print(f"warning: {w}", file=sys.stderr)
        print(f'  <StreamGeometry x:Key="{args.name}">{data}</StreamGeometry>')
        if args.insert:
            action = insert(args.name, data, args.replace)
            print(f"{action} {args.name} in {ICONS.relative_to(ROOT).as_posix()}", file=sys.stderr)
            print(f'use: <Path Width="14" Height="14" Data="{{StaticResource {args.name}}}"/>', file=sys.stderr)
    except (IconError, OSError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
