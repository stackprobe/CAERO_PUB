using Raylib_cs;

namespace Caero;

internal static unsafe class SurfaceTextures
{
    public static Texture2D WaterNoise()
    {
        const int size = 256, cells = 32;
        var random = new Xor128(Sector.SeedAt(1701, 0, 0));
        var values = new float[cells * cells];
        for (int i = 0; i < values.Length; i++) values[i] = random.NextSingle();
        float Value(int x, int y) => values[(y % cells) * cells + x % cells];
        var image = Raylib.GenImageColor(size, size, Color.White);
        try
        {
            var pixels = (Color*)image.Data;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x * (float)cells / size, py = y * (float)cells / size;
                    int ix = (int)px, iy = (int)py;
                    float u = px - ix, v = py - iy;
                    u *= u * (3 - 2 * u); v *= v * (3 - 2 * v);
                    float a = Value(ix, iy) * (1 - u) + Value(ix + 1, iy) * u;
                    float b = Value(ix, iy + 1) * (1 - u) + Value(ix + 1, iy + 1) * u;
                    byte value = (byte)Math.Clamp((int)((a * (1 - v) + b * v) * 255), 0, 255);
                    pixels[y * size + x] = new Color(value, value, value, (byte)255);
                }
            Raylib.ImageMipmaps(ref image);
            var texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Repeat);
            return texture;
        }
        finally { Raylib.UnloadImage(image); }
    }

    // Four authored, seamless detail layers: limestone pores, fractured rock,
    // leaf/needle veins and masonry joints. Packed into a single small image.
    public static Texture2D SurfaceDetail()
    {
        const int size = 512;
        float Hash(int x, int y)
        {
            uint value = unchecked((uint)x * 0x1f123bb5u ^ (uint)y * 0x5f356495u ^ 1701u);
            value ^= value >> 16; value *= 0x7feb352du; value ^= value >> 15;
            value *= 0x846ca68bu; value ^= value >> 16;
            return (value & 65535) / 65535f;
        }
        float Noise(int x, int y, int cells)
        {
            float px = x * (float)cells / size, py = y * (float)cells / size;
            int ix = (int)px, iy = (int)py;
            float u = px - ix, v = py - iy;
            u *= u * (3 - 2 * u); v *= v * (3 - 2 * v);
            float a = Hash(ix % cells, iy % cells) * (1 - u) + Hash((ix + 1) % cells, iy % cells) * u;
            float b = Hash(ix % cells, (iy + 1) % cells) * (1 - u) + Hash((ix + 1) % cells, (iy + 1) % cells) * u;
            return a * (1 - v) + b * v;
        }
        byte Byte(float value) => (byte)Math.Clamp((int)(value * 255), 0, 255);
        var image = Raylib.GenImageColor(size, size, Color.White);
        try
        {
            var pixels = (Color*)image.Data;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float coarse = Noise(x, y, 8), medium = Noise(x, y, 64), fine = Noise(x, y, 256);
                    float stone = coarse * .20f + medium * .45f + fine * .35f;
                    // Broader pits survive mip filtering, rather than isolated
                    // single-pixel specks that disappear a step from the wall.
                    float pit = Math.Clamp((medium - .65f) / .18f, 0, 1);
                    float crack = 1 - Math.Clamp((Math.Abs(coarse - .5f) - .008f) / .026f, 0, 1);
                    crack *= Math.Clamp((Noise(x, y, 16) - .35f) / .25f, 0, 1);
                    stone = Math.Clamp((stone - .5f) * 1.65f + .5f - pit * .40f - crack * .40f, 0, 1);
                    float rock = Noise(x, y, 16) * .6f + medium * .4f;
                    rock = Math.Clamp((rock - .5f) * 1.6f + .5f - pit * .25f, 0, 1);
                    float veins = .5f + .5f * MathF.Cos(x * MathF.Tau * 32 / size
                        + MathF.Sin(y * MathF.Tau * 8 / size) * .7f);
                    float foliage = medium * .65f + veins * .35f;
                    float row = (y + .5f) * 2 / size;
                    float column = (x + .5f) * 2 / size + ((int)row % 2) * .5f;
                    float fx = column - MathF.Floor(column), fy = row - MathF.Floor(row);
                    float edge = Math.Min(Math.Min(fx, 1 - fx), Math.Min(fy, 1 - fy));
                    float joint = Math.Clamp((edge - .002f) / .007f, 0, 1);
                    joint *= joint * (3 - 2 * joint);
                    pixels[y * size + x] = new Color(Byte(stone), Byte(rock), Byte(foliage), Byte(.62f + .38f * joint));
                }
            Raylib.ImageMipmaps(ref image);
            var texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            Raylib.SetTextureWrap(texture, TextureWrap.Repeat);
            return texture;
        }
        finally { Raylib.UnloadImage(image); }
    }
}
