"""
Sinh 5 tệp âm thanh chiến đấu chuẩn PCM 16-bit mono (22050 Hz, tổng < 500 KB, không dính bản quyền)
cho dự án Death Relapse vào thư mục Assets/Audio/SFX/.
"""

import math
import os
import random
import struct
import wave

SAMPLE_RATE = 22050
OUT_DIR = os.path.join("Assets", "Audio", "SFX")


def write_wav(filename: str, samples: list[float]) -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, filename)
    peak = max((abs(s) for s in samples), default=1.0)
    scale = 0.92 / peak if peak > 1e-6 else 1.0

    pcm_frames = bytearray()
    for s in samples:
        val = max(-1.0, min(1.0, s * scale))
        pcm_frames.extend(struct.pack("<h", int(val * 32767)))

    with wave.open(path, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        wf.writeframes(pcm_frames)

    return os.path.getsize(path)


def gen_sword_slash(duration: float = 0.22) -> list[float]:
    rng = random.Random(101)
    n = int(SAMPLE_RATE * duration)
    out = []
    lp = 0.0
    hp = 0.0
    prev = 0.0
    for i in range(n):
        t = i / SAMPLE_RATE
        u = i / n
        env = math.sin(min(1.0, u / 0.22) * math.pi * 0.5) * math.exp(-4.8 * u)
        noise = rng.uniform(-1.0, 1.0)
        # Bộ lọc quét tần số tạo tiếng vung lưỡi thép rít qua không khí
        alpha = 0.12 + 0.55 * (1.0 - u)
        lp = lp + alpha * (noise - lp)
        hp = 0.82 * (hp + lp - prev)
        prev = lp
        steel_freq = 1650.0 - 950.0 * u
        steel_ring = 0.28 * math.sin(2.0 * math.pi * steel_freq * t) * math.exp(-8.5 * u)
        out.append((hp * 1.35 + steel_ring) * env)
    return out


def gen_dodge_roll(duration: float = 0.28) -> list[float]:
    rng = random.Random(202)
    n = int(SAMPLE_RATE * duration)
    out = []
    lp = 0.0
    for i in range(n):
        t = i / SAMPLE_RATE
        u = i / n
        env = math.sin(math.pi * u) ** 1.2 * math.exp(-1.6 * u)
        noise = rng.uniform(-1.0, 1.0)
        lp = lp + 0.16 * (noise - lp)
        body_thump = 0.25 * math.sin(2.0 * math.pi * (140.0 - 70.0 * u) * t) * math.exp(-5.0 * u)
        out.append((lp * 1.15 + body_thump) * env)
    return out


def gen_player_hurt(duration: float = 0.26) -> list[float]:
    rng = random.Random(303)
    n = int(SAMPLE_RATE * duration)
    out = []
    lp = 0.0
    for i in range(n):
        t = i / SAMPLE_RATE
        u = i / n
        env = math.exp(-7.5 * u)
        thud_freq = 155.0 * math.exp(-3.2 * u) + 42.0
        thud = math.sin(2.0 * math.pi * thud_freq * t)
        crunch = rng.uniform(-1.0, 1.0)
        lp = lp + 0.38 * (crunch - lp)
        out.append((thud * 0.85 + lp * 0.65 * math.exp(-12.0 * u)) * env)
    return out


def gen_boss_windup(duration: float = 0.62) -> list[float]:
    rng = random.Random(404)
    n = int(SAMPLE_RATE * duration)
    out = []
    phase1 = 0.0
    phase2 = 0.0
    lp = 0.0
    for i in range(n):
        u = i / n
        # Âm báo gồng đòn trầm tăng dần cao độ để cả nhóm A lẫn nhóm B nhận biết công bằng
        env = (u ** 1.35) * (1.0 if u < 0.88 else (1.0 - u) / 0.12)
        freq1 = 78.0 + 135.0 * (u ** 1.6)
        freq2 = freq1 * 1.502
        phase1 += 2.0 * math.pi * freq1 / SAMPLE_RATE
        phase2 += 2.0 * math.pi * freq2 / SAMPLE_RATE
        drone = 0.65 * math.sin(phase1) + 0.35 * math.sin(phase2)
        noise = rng.uniform(-1.0, 1.0)
        lp = lp + (0.06 + 0.14 * u) * (noise - lp)
        out.append((drone + lp * 0.45) * env)
    return out


def gen_boss_impact(duration: float = 0.44) -> list[float]:
    rng = random.Random(505)
    n = int(SAMPLE_RATE * duration)
    out = []
    phase = 0.0
    lp = 0.0
    for i in range(n):
        u = i / n
        sub_env = math.exp(-4.2 * u)
        freq = 125.0 * math.exp(-5.0 * u) + 34.0
        phase += 2.0 * math.pi * freq / SAMPLE_RATE
        sub = math.tanh(1.8 * math.sin(phase)) * sub_env
        debris = rng.uniform(-1.0, 1.0)
        lp = lp + (0.45 * (1.0 - 0.7 * u)) * (debris - lp)
        crack = lp * math.exp(-6.5 * u) * 0.85
        out.append(sub + crack)
    return out


def main() -> None:
    clips = [
        ("sfx_sword_slash.wav", gen_sword_slash()),
        ("sfx_dodge_roll.wav", gen_dodge_roll()),
        ("sfx_player_hurt.wav", gen_player_hurt()),
        ("sfx_boss_windup.wav", gen_boss_windup()),
        ("sfx_boss_impact.wav", gen_boss_impact()),
    ]
    total_bytes = 0
    for name, samples in clips:
        size = write_wav(name, samples)
        total_bytes += size
        print(f"Generated {os.path.join(OUT_DIR, name)} ({size} bytes)")
    print(f"Total SFX size: {total_bytes} bytes ({total_bytes / 1024.0:.1f} KB)")


if __name__ == "__main__":
    main()
