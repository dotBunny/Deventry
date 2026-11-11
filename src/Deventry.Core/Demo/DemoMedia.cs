using System.Buffers.Binary;
using System.IO.Compression;

namespace Deventry.Core.Demo;

/// <summary>An image the demo draws: its PNG and its size in pixels.</summary>
internal sealed record DemoImage(byte[] Png, int Width, int Height);

/// <summary>
/// The demo's media, for the Media window (DESIGN.md §18, "Media"), drawn here rather than checked in, and the same every
/// time: the screenshot of Orbit with explosions going off that the user attaches to Pool particle emitters, the frame
/// times Claude's profiling scene plots before and after pooling, and stand-ins for the capture and the sound the
/// conversation names, which only need to be there.
/// </summary>
internal static class DemoMedia
{
    /// <summary>A frame's budget at 60 frames a second, in milliseconds: the chart's dashed line.</summary>
    public const float Budget = 16.7f;

    /// <summary>An MP4's first box and nothing after it: a video file with no video in it.</summary>
    public static byte[] StandInVideo { get; } = [0, 0, 0, 0x18, .. "ftypisom"u8, 0, 0, 2, 0, .. "isomiso2"u8];

    /// <summary>A WAV file of no samples.</summary>
    public static byte[] StandInSound { get; } =
        [.. "RIFF"u8, 36, 0, 0, 0, .. "WAVEfmt "u8, 16, 0, 0, 0, 1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 1, 0, 2, 0, 16, 0, .. "data"u8, 0, 0, 0, 0];

    /// <summary>
    /// The time each of 200 frames took while the profiling scene set off bursts: spikes where a wave of them instanced
    /// their emitters at once, or with the pool, small bumps.
    /// </summary>
    public static float[] FrameTimes(bool pooled)
    {
        var random = new Noise(pooled ? 7u : 3u);
        (int Frame, float Peak)[] waves = pooled ? [(38, 2.6f), (92, 4.2f), (146, 3.1f), (181, 3.6f)] : [(38, 33.8f), (92, 41.0f), (146, 29.5f), (181, 36.4f)];
        var times = new float[200];
        for (var i = 0; i < times.Length; i++)
        {
            var time = 7.2f + 1.4f * random.Next() + 0.5f * MathF.Sin(i / 9f);
            foreach (var (frame, peak) in waves)
            {
                var d = (i - frame) / 1.7f;
                time += peak * MathF.Exp(-d * d);
            }
            times[i] = time;
        }
        return times;
    }

    /// <summary>The frame times plotted as a script would, on white: the time under each frame, red above the budget.</summary>
    public static DemoImage FrameTimeChart(bool pooled)
    {
        const int Width = 640, Height = 360, Left = 52, Right = 616, Top = 24, Bottom = 316;
        const float Scale = 50f;
        var canvas = new Canvas(Width, Height);
        canvas.Fill(Rgb.Hex(0xFFFFFF));
        var times = FrameTimes(pooled);
        float YOf(float ms) => Bottom - ms / Scale * (Bottom - Top);
        float TimeAt(int x)
        {
            var at = (x - Left) / (float)(Right - Left) * (times.Length - 1);
            var i = Math.Min((int)at, times.Length - 2);
            return times[i] + (times[i + 1] - times[i]) * (at - i);
        }

        for (var ms = 10; ms <= 50; ms += 10)
        {
            canvas.FillRect(Left, (int)YOf(ms), Right - Left, 1, Rgb.Hex(0xE6E8EC));
            canvas.FillRect(Left - 5, (int)YOf(ms), 5, 1, Rgb.Hex(0x555B66));
        }
        var budget = (int)YOf(Budget);
        for (var x = Left; x < Right; x++)
        {
            var top = (int)YOf(TimeAt(x));
            for (var y = top; y < Bottom; y++)
            {
                canvas.Blend(x, y, y < budget ? Rgb.Hex(0xF2B8B0) : Rgb.Hex(0xC9DCF2), 1);
            }
        }
        for (var x = Left; x < Right - 1; x++)
        {
            var (a, b) = (YOf(TimeAt(x)), YOf(TimeAt(x + 1)));
            for (var y = (int)MathF.Min(a, b) - 1; y <= (int)MathF.Max(a, b) + 1; y++)
            {
                canvas.Blend(x, y, Rgb.Hex(0x2F6DB5), 1);
            }
        }
        for (var x = Left; x < Right; x += 13)
        {
            canvas.FillRect(x, budget - 1, Math.Min(8, Right - x), 2, Rgb.Hex(0xE8892B));
        }
        canvas.FillRect(Left - 1, Top - 6, 1, Bottom - Top + 6, Rgb.Hex(0x555B66));
        canvas.FillRect(Left - 1, Bottom, Right - Left + 1, 1, Rgb.Hex(0x555B66));
        for (var frame = 0; frame < times.Length; frame += 25)
        {
            canvas.FillRect(Left + frame * (Right - Left) / (times.Length - 1), Bottom, 1, 5, Rgb.Hex(0x555B66));
        }
        return new DemoImage(canvas.Png(), Width, Height);
    }

    /// <summary>Orbit as it plays: a planet with a ship on its orbit, bursts going off around it, and the HUD.</summary>
    public static DemoImage Screenshot()
    {
        const int Width = 640, Height = 400;
        var canvas = new Canvas(Width, Height);
        var random = new Noise(11);
        for (var y = 0; y < Height; y++)
        {
            canvas.FillRect(0, y, Width, 1, Rgb.Lerp(Rgb.Hex(0x0B1028), Rgb.Hex(0x22124A), y / (Height - 1f)));
        }
        canvas.Glow(150, 90, 230, Rgb.Hex(0x6A3FA0), 0.22f);
        canvas.Glow(530, 330, 210, Rgb.Hex(0x1F6F8B), 0.2f);
        for (var i = 0; i < 170; i++)
        {
            var (x, y, light) = (random.Next() * Width, random.Next() * Height, 0.25f + 0.75f * random.Next());
            canvas.Disc(x, y, light > 0.85f ? 1.3f : 0.7f, Rgb.Hex(0xFFFFFF), light);
        }

        // The planet, lit from the top left, and the orbit around it: the far side behind it, the near side in front.
        const float Cx = 330, Cy = 215, Radius = 78, Rx = 190, Ry = 58;
        canvas.Glow(Cx, Cy, Radius + 16, Rgb.Hex(0x6FC3FF), 0.35f);
        canvas.Shade(Cx, Cy, Radius, Rgb.Hex(0x3A8FC4), Rgb.Hex(0x1C3F73));
        for (var t = 0f; t < MathF.Tau; t += 0.004f)
        {
            var (x, y) = (Cx + Rx * MathF.Cos(t), Cy + Ry * MathF.Sin(t));
            var hidden = MathF.Sin(t) < 0 && (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) < Radius * Radius;
            if (!hidden)
            {
                canvas.Disc(x, y, 0.9f, Rgb.Hex(0x9FD8FF), 0.5f);
            }
        }
        var ship = 0.32f * MathF.PI;
        var (sx, sy) = (Cx + Rx * MathF.Cos(ship), Cy + Ry * MathF.Sin(ship));
        var (dx, dy) = (-Rx * MathF.Sin(ship), Ry * MathF.Cos(ship));
        var length = MathF.Sqrt(dx * dx + dy * dy);
        (dx, dy) = (dx / length, dy / length);
        canvas.Glow(sx - dx * 12, sy - dy * 12, 9, Rgb.Hex(0xFF9A3C), 0.9f);
        canvas.Triangle((sx + dx * 11, sy + dy * 11), (sx - dx * 7 - dy * 7, sy - dy * 7 + dx * 7), (sx - dx * 7 + dy * 7, sy - dy * 7 - dx * 7), Rgb.Hex(0xE8F4FF));

        foreach (var (x, y, size) in new (float, float, float)[] { (150, 122, 34), (508, 104, 28), (472, 298, 40), (204, 318, 26), (566, 214, 20), (96, 244, 18) })
        {
            canvas.Glow(x, y, size * 1.7f, Rgb.Hex(0xFF6A1F), 0.75f);
            canvas.Glow(x, y, size * 0.6f, Rgb.Hex(0xFFE9A8), 1f);
            for (var i = 0; i < 26; i++)
            {
                var (angle, reach) = (random.Next() * MathF.Tau, 0.55f + 0.9f * random.Next());
                var spark = Rgb.Lerp(Rgb.Hex(0xFFF1B0), Rgb.Hex(0xFF5A1F), reach / 1.45f);
                canvas.Glow(x + MathF.Cos(angle) * size * reach, y + MathF.Sin(angle) * size * reach, 3.2f, spark, 0.9f);
            }
        }

        // The HUD: health, and the lives left.
        canvas.FillRect(18, 16, 124, 10, Rgb.Hex(0xFFFFFF), 0.25f);
        canvas.FillRect(20, 18, 86, 6, Rgb.Hex(0x5CE1A0));
        for (var i = 0; i < 3; i++)
        {
            canvas.FillRect(Width - 30 - i * 16, 16, 10, 10, Rgb.Hex(0xFFFFFF), 0.7f);
        }
        return new DemoImage(canvas.Png(), Width, Height);
    }

    /// <summary>A color, each channel from 0 to 1.</summary>
    private readonly record struct Rgb(float R, float G, float B)
    {
        public static Rgb Hex(uint hex) => new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    /// <summary>Numbers from 0 to 1 that are the same for the same seed on every system (xorshift).</summary>
    private sealed class Noise(uint seed)
    {
        private uint _state = seed * 2654435761u | 1;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) / (float)(1 << 24);
        }
    }

    /// <summary>Pixels to draw on, written out as a PNG.</summary>
    private sealed class Canvas(int width, int height)
    {
        private readonly float[] _pixels = new float[width * height * 3];

        public void Fill(Rgb color) => FillRect(0, 0, width, height, color);

        public void FillRect(int x, int y, int w, int h, Rgb color, float alpha = 1)
        {
            for (var row = y; row < y + h; row++)
            {
                for (var column = x; column < x + w; column++)
                {
                    Blend(column, row, color, alpha);
                }
            }
        }

        public void Blend(int x, int y, Rgb color, float alpha)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }
            var i = (y * width + x) * 3;
            _pixels[i] += (color.R - _pixels[i]) * alpha;
            _pixels[i + 1] += (color.G - _pixels[i + 1]) * alpha;
            _pixels[i + 2] += (color.B - _pixels[i + 2]) * alpha;
        }

        /// <summary>Light added around a point, fading to nothing at <paramref name="radius"/>.</summary>
        public void Glow(float cx, float cy, float radius, Rgb color, float strength)
        {
            Each(cx, cy, radius, (x, y, d) =>
            {
                var fall = 1 - d / radius;
                var amount = strength * fall * fall;
                var i = (y * width + x) * 3;
                _pixels[i] += color.R * amount;
                _pixels[i + 1] += color.G * amount;
                _pixels[i + 2] += color.B * amount;
            });
        }

        /// <summary>A filled circle with a soft edge.</summary>
        public void Disc(float cx, float cy, float radius, Rgb color, float alpha) =>
            Each(cx, cy, radius + 1, (x, y, d) => Blend(x, y, color, alpha * Math.Clamp(radius + 0.5f - d, 0, 1)));

        /// <summary>A sphere lit from the top left, from <paramref name="lit"/> to <paramref name="dark"/>.</summary>
        public void Shade(float cx, float cy, float radius, Rgb lit, Rgb dark)
        {
            var (lx, ly, lz) = (-0.5f, -0.6f, 0.62f);
            Each(cx, cy, radius + 1, (x, y, d) =>
            {
                var (nx, ny) = ((x - cx) / radius, (y - cy) / radius);
                var nz = MathF.Sqrt(MathF.Max(0, 1 - nx * nx - ny * ny));
                var light = Math.Clamp((nx * lx + ny * ly + nz * lz) / 0.95f, 0, 1);
                Blend(x, y, Rgb.Lerp(dark, lit, light), Math.Clamp(radius + 0.5f - d, 0, 1));
            });
        }

        /// <summary>A filled triangle, its edges smoothed by sampling each pixel 16 times.</summary>
        public void Triangle((float X, float Y) a, (float X, float Y) b, (float X, float Y) c, Rgb color)
        {
            static float Side((float X, float Y) p, (float X, float Y) q, float x, float y) => (q.X - p.X) * (y - p.Y) - (q.Y - p.Y) * (x - p.X);
            var (left, right) = ((int)MathF.Min(a.X, MathF.Min(b.X, c.X)), (int)MathF.Max(a.X, MathF.Max(b.X, c.X)) + 1);
            var (top, bottom) = ((int)MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), (int)MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) + 1);
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    var inside = 0;
                    for (var s = 0; s < 16; s++)
                    {
                        var (px, py) = (x + (s % 4 + 0.5f) / 4, y + (s / 4 + 0.5f) / 4);
                        var (ab, bc, ca) = (Side(a, b, px, py), Side(b, c, px, py), Side(c, a, px, py));
                        if ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0))
                        {
                            inside++;
                        }
                    }
                    Blend(x, y, color, inside / 16f);
                }
            }
        }

        /// <summary>The pixels within <paramref name="radius"/> of a point, with their distance from it.</summary>
        private void Each(float cx, float cy, float radius, Action<int, int, float> draw)
        {
            for (var y = Math.Max(0, (int)(cy - radius)); y <= Math.Min(height - 1, (int)(cy + radius)); y++)
            {
                for (var x = Math.Max(0, (int)(cx - radius)); x <= Math.Min(width - 1, (int)(cx + radius)); x++)
                {
                    var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    if (d < radius)
                    {
                        draw(x, y, d);
                    }
                }
            }
        }

        /// <summary>An 8-bit RGB PNG, each row filtered by the pixel to its left (Sub), which suits gradients and flat color.</summary>
        public byte[] Png()
        {
            using var data = new MemoryStream();
            using (var deflate = new ZLibStream(data, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                var row = new byte[width * 3 + 1];
                for (var y = 0; y < height; y++)
                {
                    row[0] = 1;
                    for (var i = 0; i < width * 3; i++)
                    {
                        var value = (byte)Math.Clamp((int)MathF.Round(_pixels[y * width * 3 + i] * 255), 0, 255);
                        var left = i >= 3 ? (byte)Math.Clamp((int)MathF.Round(_pixels[y * width * 3 + i - 3] * 255), 0, 255) : (byte)0;
                        row[i + 1] = (byte)(value - left);
                    }
                    deflate.Write(row);
                }
            }
            var header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            (header[8], header[9]) = (8, 2);
            using var png = new MemoryStream();
            png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
            Chunk(png, "IHDR"u8, header);
            Chunk(png, "IDAT"u8, data.ToArray());
            Chunk(png, "IEND"u8, []);
            return png.ToArray();
        }

        private static void Chunk(MemoryStream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            png.Write(number);
            png.Write(type);
            png.Write(data);
            BinaryPrimitives.WriteUInt32BigEndian(number, Crc([.. type, .. data]));
            png.Write(number);
        }

        private static uint Crc(ReadOnlySpan<byte> bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return ~crc;
        }
    }
}
