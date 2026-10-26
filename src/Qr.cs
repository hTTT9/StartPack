// QR-код (байтовый режим, коррекция ошибок M, версии 1–10) — по алгоритму Project Nayuki, без внешних библиотек.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace StartPack
{
    public static class Qr
    {
        // Уровень M. Индекс — номер версии (0 не используется).
        static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[] EccBlocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
        const int FormatBitsM = 0;
        const int MaxVersion = 10;

        public static bool[,] Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            int ver = 1;
            for (; ver <= MaxVersion; ver++)
            {
                int bits = 4 + (ver <= 9 ? 8 : 16) + data.Length * 8;
                if (bits <= DataCodewords(ver) * 8) break;
            }
            if (ver > MaxVersion) throw new ArgumentException("слишком длинный текст для QR");

            // биты: режим «байты», длина, данные, терминатор, выравнивание, заполнители
            var bb = new List<bool>();
            Append(bb, 4, 4);
            Append(bb, data.Length, ver <= 9 ? 8 : 16);
            foreach (byte b in data) Append(bb, b, 8);
            int cap = DataCodewords(ver) * 8;
            Append(bb, 0, Math.Min(4, cap - bb.Count));
            Append(bb, 0, (8 - bb.Count % 8) % 8);
            for (int pad = 0xEC; bb.Count < cap; pad ^= 0xEC ^ 0x11) Append(bb, pad, 8);
            var codewords = new byte[bb.Count / 8];
            for (int i = 0; i < bb.Count; i++) if (bb[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            byte[] all = AddEcc(codewords, ver);
            int size = ver * 4 + 17;
            var m = new bool[size, size];      // [y, x]
            var fn = new bool[size, size];
            DrawFunctionPatterns(m, fn, ver, size);
            DrawCodewords(m, fn, all, size);

            int best = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(m, fn, mask, size);
                DrawFormat(m, fn, mask, size);
                int p = Penalty(m, size);
                if (p < bestPenalty) { bestPenalty = p; best = mask; }
                ApplyMask(m, fn, mask, size);   // снять маску (XOR)
            }
            ApplyMask(m, fn, best, size);
            DrawFormat(m, fn, best, size);
            return m;
        }

        // Картинка: тёмные модули на белом с полями в 4 модуля.
        public static Bitmap Render(string text, int pixels)
        {
            bool[,] m = Encode(text);
            int n = m.GetLength(0), total = n + 8;
            int scale = Math.Max(1, pixels / total);
            var bmp = new Bitmap(total * scale, total * scale);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (m[y, x]) g.FillRectangle(Brushes.Black, (x + 4) * scale, (y + 4) * scale, scale, scale);
            }
            return bmp;
        }

        static void Append(List<bool> bb, int val, int len)
        {
            for (int i = len - 1; i >= 0; i--) bb.Add(((val >> i) & 1) != 0);
        }

        static int RawDataModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int na = ver / 7 + 2;
                r -= (25 * na - 10) * na - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }

        static int DataCodewords(int ver) { return RawDataModules(ver) / 8 - EccPerBlock[ver] * EccBlocks[ver]; }

        static byte[] AddEcc(byte[] data, int ver)
        {
            int numBlocks = EccBlocks[ver], eccLen = EccPerBlock[ver];
            int raw = RawDataModules(ver) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            byte[] div = RsDivisor(eccLen);
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int len = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                var block = new byte[shortLen + 1];
                Array.Copy(dat, block, len);
                byte[] ecc = RsRemainder(dat, div);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks.Add(block);
            }
            var result = new List<byte>();
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        static byte[] RsDivisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < degree) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        static byte[] RsRemainder(byte[] data, byte[] div)
        {
            var r = new byte[div.Length];
            foreach (byte b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
            }
            return r;
        }

        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark)
        {
            m[y, x] = dark;
            fn[y, x] = true;
        }

        static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int ver, int size)
        {
            for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
            Finder(m, fn, 3, 3, size); Finder(m, fn, size - 4, 3, size); Finder(m, fn, 3, size - 4, size);
            int[] pos = AlignPositions(ver, size);
            int na = pos.Length;
            for (int i = 0; i < na; i++)
                for (int j = 0; j < na; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == na - 1) || (i == na - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(m, fn, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            DrawFormat(m, fn, 0, size);
            if (ver >= 7)
            {
                int rem = ver;
                for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                long bits = ((long)ver << 12) | (long)(rem & 0xFFF);
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = size - 11 + i % 3, b = i / 3;
                    Set(m, fn, a, b, bit);
                    Set(m, fn, b, a, bit);
                }
            }
        }

        static void Finder(bool[,] m, bool[,] fn, int x, int y, int size)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < size && yy >= 0 && yy < size) Set(m, fn, xx, yy, d != 2 && d != 4);
                }
        }

        static int[] AlignPositions(int ver, int size)
        {
            if (ver == 1) return new int[0];
            int na = ver / 7 + 2;
            int step = (ver * 4 + na * 2 + 1) / (na * 2 - 2) * 2;
            var r = new int[na];
            r[0] = 6;
            for (int i = na - 1, p = size - 7; i >= 1; i--, p -= step) r[i] = p;
            return r;
        }

        static void DrawFormat(bool[,] m, bool[,] fn, int mask, int size)
        {
            int data = FormatBitsM << 3 | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;
            for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, Bit(bits, i));
            Set(m, fn, 8, 7, Bit(bits, 6));
            Set(m, fn, 8, 8, Bit(bits, 7));
            Set(m, fn, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, Bit(bits, i));
            for (int i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, Bit(bits, i));
            Set(m, fn, 8, size - 8, true);
        }

        static bool Bit(int v, int i) { return ((v >> i) & 1) != 0; }

        static void DrawCodewords(bool[,] m, bool[,] fn, byte[] data, int size)
        {
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (!fn[y, x] && i < data.Length * 8)
                        {
                            m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
            }
        }

        static void ApplyMask(bool[,] m, bool[,] fn, int mask, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (fn[y, x]) continue;
                    bool inv;
                    switch (mask)
                    {
                        case 0: inv = (x + y) % 2 == 0; break;
                        case 1: inv = y % 2 == 0; break;
                        case 2: inv = x % 3 == 0; break;
                        case 3: inv = (x + y) % 3 == 0; break;
                        case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                        case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (inv) m[y, x] = !m[y, x];
                }
        }

        // Упрощённая оценка (правила 1, 2 и 4 стандарта): выбирает маску поаккуратнее. Любая маска допустима.
        static int Penalty(bool[,] m, int size)
        {
            int p = 0, dark = 0;
            for (int a = 0; a < size; a++)
            {
                int runR = 1, runC = 1;
                for (int b = 1; b < size; b++)
                {
                    if (m[a, b] == m[a, b - 1]) { runR++; if (runR == 5) p += 3; else if (runR > 5) p++; } else runR = 1;
                    if (m[b, a] == m[b - 1, a]) { runC++; if (runC == 5) p += 3; else if (runC > 5) p++; } else runC = 1;
                }
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (m[y, x]) dark++;
                    if (x < size - 1 && y < size - 1 && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) p += 3;
                }
            int total = size * size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return p + Math.Max(0, k) * 10;
        }
    }
}
