"""Render a seamless rain-and-embers loop from a static menu background plate."""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageOps


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--deps", type=Path, required=True)
    parser.add_argument("--width", type=int, default=1920)
    parser.add_argument("--height", type=int, default=1080)
    parser.add_argument("--fps", type=int, default=24)
    parser.add_argument("--seconds", type=float, default=6.0)
    parser.add_argument("--preview", type=Path)
    return parser.parse_args()


def periodic(value: float) -> float:
    return value - math.floor(value)


def fit_plate(path: Path, size: tuple[int, int]) -> Image.Image:
    with Image.open(path) as source:
        source = ImageOps.exif_transpose(source).convert("RGB")
        return ImageOps.fit(source, size, Image.Resampling.LANCZOS).convert("RGBA")


def warm_mask(base: Image.Image) -> tuple[Image.Image, tuple[float, float]]:
    rgb = np.asarray(base.convert("RGB"), dtype=np.int16)
    height, width = rgb.shape[:2]
    red, green, blue = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    yy, xx = np.ogrid[:height, :width]
    lower_scene = yy > int(height * 0.56)
    warm = lower_scene & (red > 90) & (red > green * 1.28) & (green > blue * 1.14)
    mask_data = np.where(warm, 255, 0).astype(np.uint8)
    if warm.any():
        weights = np.maximum(red - green, 1) * warm
        total = float(weights.sum())
        center_x = float((weights * xx).sum() / total)
        center_y = float((weights * yy).sum() / total)
    else:
        center_x, center_y = width * 0.63, height * 0.80
    mask = Image.fromarray(mask_data, "L")
    return mask, (center_x, center_y)


def make_rain(rng: np.random.Generator, width: int, height: int, count: int, layer: int):
    drops = []
    span = height + 180
    for _ in range(count):
        drops.append(
            {
                "x": float(rng.uniform(-80, width + 80)),
                "y": float(rng.uniform(0, span)),
                "cycles": int(rng.integers(2 + layer, 5 + layer)),
                "length": float(rng.uniform(12 + layer * 8, 27 + layer * 13)),
                "alpha": int(rng.integers(22 + layer * 14, 48 + layer * 24)),
                "width": int(1 + (layer == 2 and rng.random() > 0.6)),
                "pulse": int(rng.integers(1, 4)),
                "phase": float(rng.random()),
            }
        )
    return drops


def draw_rain(
    size: tuple[int, int], drops: list[dict[str, float]], t: float, blur: float
) -> Image.Image:
    width, height = size
    span = height + 180
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for drop in drops:
        y = periodic(drop["y"] / span + drop["cycles"] * t) * span - 90
        x = drop["x"] + 10.0 * math.sin(2.0 * math.pi * (t + drop["phase"]))
        intensity = 0.72 + 0.28 * math.sin(
            2.0 * math.pi * (drop["pulse"] * t + drop["phase"])
        )
        alpha = max(0, int(drop["alpha"] * intensity))
        length = drop["length"]
        draw.line(
            (x, y, x - length * 0.22, y + length),
            fill=(174, 199, 220, alpha),
            width=int(drop["width"]),
        )
    return layer.filter(ImageFilter.GaussianBlur(blur)) if blur else layer


def make_ripples(rng: np.random.Generator, width: int, height: int):
    ripples = []
    for _ in range(22):
        y = float(rng.uniform(height * 0.55, height * 0.94))
        road_half_width = (y / height) ** 1.7 * width * 0.34
        x = float(rng.uniform(width * 0.51 - road_half_width, width * 0.51 + road_half_width))
        ripples.append(
            {
                "x": x,
                "y": y,
                "phase": float(rng.random()),
                "cycles": int(rng.integers(2, 5)),
                "radius": float(rng.uniform(9, 24)),
                "alpha": int(rng.integers(22, 54)),
            }
        )
    return ripples


def draw_ripples(size: tuple[int, int], ripples: list[dict[str, float]], t: float) -> Image.Image:
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    for ripple in ripples:
        phase = periodic(ripple["phase"] + ripple["cycles"] * t)
        visibility = math.sin(math.pi * phase) ** 2
        radius = 2.0 + ripple["radius"] * phase
        alpha = int(ripple["alpha"] * visibility)
        box = (
            ripple["x"] - radius,
            ripple["y"] - radius * 0.25,
            ripple["x"] + radius,
            ripple["y"] + radius * 0.25,
        )
        draw.ellipse(box, outline=(158, 183, 204, alpha), width=1)
    return layer.filter(ImageFilter.GaussianBlur(0.35))


def draw_fire(
    size: tuple[int, int],
    mask: Image.Image,
    center: tuple[float, float],
    t: float,
    ember_specs: list[dict[str, float]],
) -> Image.Image:
    width, height = size
    cx, cy = center
    fire = Image.new("RGBA", size, (0, 0, 0, 0))

    flicker = (
        0.52
        + 0.23 * math.sin(2.0 * math.pi * 3.0 * t)
        + 0.15 * math.sin(2.0 * math.pi * 5.0 * t + 0.8)
        + 0.10 * math.sin(2.0 * math.pi * 7.0 * t + 2.1)
    )
    glow_alpha = mask.filter(ImageFilter.GaussianBlur(width * 0.012))
    glow_alpha = glow_alpha.point(lambda value: int(value * (0.18 + 0.18 * flicker)))
    glow = Image.new("RGBA", size, (255, 69, 12, 0))
    glow.putalpha(glow_alpha)
    fire = Image.alpha_composite(fire, glow)

    tongues = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(tongues)
    for index, (dx, scale, phase) in enumerate(((-25, 0.75, 0.1), (0, 1.0, 0.47), (24, 0.66, 0.78))):
        sway = math.sin(2.0 * math.pi * (4.0 * t + phase))
        pulse = 0.78 + 0.22 * math.sin(2.0 * math.pi * ((3 + index) * t + phase))
        base_y = cy + 9
        flame_height = (42 + 23 * scale) * pulse
        flame_width = 10 + 10 * scale
        x = cx + dx + sway * 5
        points = [
            (x - flame_width, base_y),
            (x - flame_width * 0.45, base_y - flame_height * 0.55),
            (x + sway * 6, base_y - flame_height),
            (x + flame_width * 0.42, base_y - flame_height * 0.48),
            (x + flame_width, base_y),
        ]
        draw.polygon(points, fill=(255, 72, 8, int(78 + 50 * flicker)))
        draw.ellipse(
            (x - flame_width * 0.43, base_y - flame_height * 0.62,
             x + flame_width * 0.43, base_y + 2),
            fill=(255, 184, 54, int(45 + 55 * flicker)),
        )
    tongues = tongues.filter(ImageFilter.GaussianBlur(2.4))
    fire = Image.alpha_composite(fire, tongues)

    embers = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(embers)
    for ember in ember_specs:
        phase = periodic(ember["phase"] + ember["cycles"] * t)
        visibility = math.sin(math.pi * phase) ** 2
        x = cx + ember["dx"] + ember["drift"] * (phase - 0.5)
        x += ember["wiggle"] * math.sin(2.0 * math.pi * (phase + ember["phase"]))
        y = cy - ember["rise"] * phase
        radius = ember["radius"] * (0.65 + 0.35 * visibility)
        alpha = int(230 * visibility)
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=(255, 116, 20, alpha))
    fire = Image.alpha_composite(fire, embers.filter(ImageFilter.GaussianBlur(0.65)))

    smoke = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(smoke)
    for index in range(7):
        phase = periodic(index / 7.0 + 1.0 * t)
        visibility = math.sin(math.pi * phase) ** 2
        x = cx + 18 * math.sin(2.0 * math.pi * (phase + index * 0.17))
        y = cy - 18 - 125 * phase
        rx = 13 + 28 * phase
        ry = 8 + 19 * phase
        alpha = int(18 * visibility)
        draw.ellipse((x - rx, y - ry, x + rx, y + ry), fill=(96, 104, 112, alpha))
    fire = Image.alpha_composite(fire, smoke.filter(ImageFilter.GaussianBlur(14)))
    return fire


def main() -> int:
    args = parse_args()
    sys.path.insert(0, str(args.deps.resolve()))
    import imageio_ffmpeg

    size = (args.width, args.height)
    frame_count = max(2, round(args.fps * args.seconds))
    base = fit_plate(args.input, size)
    mask, fire_center = warm_mask(base)

    rng = np.random.default_rng(1942)
    rain_back = make_rain(rng, *size, count=120, layer=0)
    rain_mid = make_rain(rng, *size, count=85, layer=1)
    rain_front = make_rain(rng, *size, count=42, layer=2)
    ripples = make_ripples(rng, *size)
    ember_specs = [
        {
            "phase": float(rng.random()),
            "cycles": int(rng.integers(1, 4)),
            "dx": float(rng.uniform(-55, 55)),
            "drift": float(rng.uniform(-35, 35)),
            "wiggle": float(rng.uniform(4, 15)),
            "rise": float(rng.uniform(55, 150)),
            "radius": float(rng.uniform(1.0, 2.6)),
        }
        for _ in range(22)
    ]

    args.output.parent.mkdir(parents=True, exist_ok=True)
    writer = imageio_ffmpeg.write_frames(
        str(args.output),
        size,
        pix_fmt_in="rgb24",
        pix_fmt_out="yuv420p",
        fps=args.fps,
        quality=8,
        codec="libx264",
        macro_block_size=2,
        ffmpeg_log_level="warning",
        output_params=[
            "-preset", "medium",
            "-movflags", "+faststart",
            "-metadata", "title=Unconquered Land - Rain and Fire Loop",
            "-metadata", "comment=Seamless loop; no audio",
        ],
    )
    writer.send(None)

    sample_frames: list[Image.Image] = []
    try:
        for frame_index in range(frame_count):
            t = frame_index / frame_count
            frame = base.copy()
            frame = Image.alpha_composite(frame, draw_rain(size, rain_back, t, 0.8))
            frame = Image.alpha_composite(frame, draw_ripples(size, ripples, t))
            frame = Image.alpha_composite(frame, draw_fire(size, mask, fire_center, t, ember_specs))
            frame = Image.alpha_composite(frame, draw_rain(size, rain_mid, t, 0.45))
            frame = Image.alpha_composite(frame, draw_rain(size, rain_front, t, 1.15))
            rgb = frame.convert("RGB")
            writer.send(np.asarray(rgb, dtype=np.uint8).tobytes())

            if args.preview and frame_index in {
                0,
                frame_count // 4,
                frame_count // 2,
                (frame_count * 3) // 4,
            }:
                sample_frames.append(rgb.resize((640, 360), Image.Resampling.LANCZOS))
            if frame_index % max(args.fps, 1) == 0:
                print(f"Rendered {frame_index}/{frame_count} frames", flush=True)
    finally:
        writer.close()

    if args.preview and sample_frames:
        sheet = Image.new("RGB", (1280, 720), (0, 0, 0))
        for index, sample in enumerate(sample_frames[:4]):
            sheet.paste(sample, ((index % 2) * 640, (index // 2) * 360))
        args.preview.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(args.preview, quality=92)

    print(f"Wrote {args.output.resolve()}")
    print(f"Detected fire center: {fire_center[0]:.1f}, {fire_center[1]:.1f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
