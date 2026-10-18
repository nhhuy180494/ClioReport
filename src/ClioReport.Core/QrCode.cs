using System.Text;

namespace ClioReport;

/// <summary>
/// QR Code (Model 2) encoder: UTF-8 byte mode, error correction level M, versions 1-40, automatic mask choice.
/// Algorithm and tables follow Project Nayuki's QR Code generator library (MIT License,
/// Copyright (c) Project Nayuki, https://www.nayuki.io/page/qr-code-generator-library).
/// </summary>
public static class QrCode
{
    private static readonly int[] EccPerBlock =
        { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 };

    private static readonly int[] NumBlocks =
        { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 };

    /// <summary>Encodes the text; the result is [row, column], true = dark module. Throws if the text does not fit version 40-M.</summary>
    public static bool[,] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = Encoding.UTF8.GetBytes(text);

        var version = 1;
        while (true)
        {
            if (version > 40) throw new ArgumentException("Text is too long for a QR code.", nameof(text));
            var countBits = version < 10 ? 8 : 16;
            if (data.Length < (1 << countBits) && 4 + countBits + data.Length * 8 <= DataCodewords(version) * 8) break;
            version++;
        }

        var bits = new List<bool>();
        Append(bits, 0b0100, 4);
        Append(bits, data.Length, version < 10 ? 8 : 16);
        foreach (var b in data) Append(bits, b, 8);

        var capacity = DataCodewords(version) * 8;
        Append(bits, 0, Math.Min(4, capacity - bits.Count));
        Append(bits, 0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);

        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++)
            if (bits[i]) codewords[i >> 3] |= (byte)(0x80 >> (i & 7));

        return new Symbol(version, AddEccAndInterleave(version, codewords)).Modules;
    }

    private static void Append(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }

    private static int RawModules(int ver)
    {
        var result = (16 * ver + 128) * ver + 64;
        if (ver >= 2)
        {
            var align = ver / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int ver) => RawModules(ver) / 8 - EccPerBlock[ver] * NumBlocks[ver];

    private static byte[] AddEccAndInterleave(int ver, byte[] data)
    {
        var numBlocks = NumBlocks[ver];
        var eccLen = EccPerBlock[ver];
        var raw = RawModules(ver) / 8;
        var shortBlocks = numBlocks - raw % numBlocks;
        var shortLen = raw / numBlocks;

        var blocks = new List<byte[]>();
        var divisor = ReedSolomonDivisor(eccLen);
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLen = shortLen - eccLen + (i < shortBlocks ? 0 : 1);
            var block = new byte[shortLen + 1];
            Array.Copy(data, k, block, 0, datLen);
            var ecc = ReedSolomonRemainder(data.AsSpan(k, datLen), divisor);
            Array.Copy(ecc, 0, block, shortLen + 1 - eccLen, eccLen);
            k += datLen;
            blocks.Add(block);
        }

        var result = new List<byte>(raw);
        for (var i = 0; i < shortLen + 1; i++)
            for (var j = 0; j < numBlocks; j++)
                if (i != shortLen - eccLen || j >= shortBlocks) // skip the padding byte of short blocks
                    result.Add(blocks[j][i]);
        return result.ToArray();
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < divisor.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static int Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }

    private sealed class Symbol
    {
        private readonly int _version;
        private readonly int _size;
        private readonly bool[,] _m;
        private readonly bool[,] _function;

        public bool[,] Modules => _m;

        public Symbol(int version, byte[] codewords)
        {
            _version = version;
            _size = version * 4 + 17;
            _m = new bool[_size, _size];
            _function = new bool[_size, _size];

            DrawFunctionPatterns();
            DrawCodewords(codewords);

            var best = 0;
            var minPenalty = int.MaxValue;
            for (var mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                var penalty = Penalty();
                if (penalty < minPenalty) { best = mask; minPenalty = penalty; }
                ApplyMask(mask); // XOR undoes it
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        private void Set(int x, int y, bool dark)
        {
            _m[y, x] = dark;
            _function[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            for (var i = 0; i < _size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(_size - 4, 3);
            DrawFinder(3, _size - 4);

            var pos = AlignmentPositions();
            var n = pos.Length;
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    if (!((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)))
                        DrawAlignment(pos[i], pos[j]);

            DrawFormatBits(0);
            DrawVersion();
        }

        private void DrawFormatBits(int mask)
        {
            const int levelM = 0; // format bits for error correction level M
            var data = levelM << 3 | mask;
            var rem = data;
            for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            var bits = (data << 10 | rem) ^ 0x5412;

            for (var i = 0; i <= 5; i++) Set(8, i, Bit(bits, i));
            Set(8, 7, Bit(bits, 6));
            Set(8, 8, Bit(bits, 7));
            Set(7, 8, Bit(bits, 8));
            for (var i = 9; i < 15; i++) Set(14 - i, 8, Bit(bits, i));

            for (var i = 0; i < 8; i++) Set(_size - 1 - i, 8, Bit(bits, i));
            for (var i = 8; i < 15; i++) Set(8, _size - 15 + i, Bit(bits, i));
            Set(8, _size - 8, true);
        }

        private void DrawVersion()
        {
            if (_version < 7) return;
            var rem = _version;
            for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = _version << 12 | rem;
            for (var i = 0; i < 18; i++)
            {
                var a = _size - 11 + i % 3;
                var b = i / 3;
                Set(a, b, Bit(bits, i));
                Set(b, a, Bit(bits, i));
            }
        }

        private void DrawFinder(int x, int y)
        {
            for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < _size && yy >= 0 && yy < _size)
                    {
                        var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        Set(xx, yy, dist != 2 && dist != 4);
                    }
                }
        }

        private void DrawAlignment(int x, int y)
        {
            for (var dy = -2; dy <= 2; dy++)
                for (var dx = -2; dx <= 2; dx++)
                    Set(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] AlignmentPositions()
        {
            if (_version == 1) return Array.Empty<int>();
            var n = _version / 7 + 2;
            var step = _version == 32 ? 26 : (_version * 4 + n * 2 + 1) / (n * 2 - 2) * 2;
            var result = new int[n];
            result[0] = 6;
            for (var i = n - 1; i >= 1; i--) result[i] = _size - 7 - (n - 1 - i) * step;
            return result;
        }

        private void DrawCodewords(byte[] data)
        {
            var i = 0;
            for (var right = _size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (var vert = 0; vert < _size; vert++)
                    for (var j = 0; j < 2; j++)
                    {
                        var x = right - j;
                        var upward = ((right + 1) & 2) == 0;
                        var y = upward ? _size - 1 - vert : vert;
                        if (!_function[y, x] && i < data.Length * 8)
                        {
                            _m[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                            i++;
                        }
                    }
            }
        }

        private void ApplyMask(int mask)
        {
            for (var y = 0; y < _size; y++)
                for (var x = 0; x < _size; x++)
                {
                    var invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (x / 3 + y / 2) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                    };
                    _m[y, x] ^= invert && !_function[y, x];
                }
        }

        private int Penalty()
        {
            var result = 0;
            for (var pass = 0; pass < 2; pass++)
                for (var a = 0; a < _size; a++)
                {
                    var runColor = false;
                    var run = 0;
                    var history = new int[7];
                    for (var b = 0; b < _size; b++)
                    {
                        var cell = pass == 0 ? _m[a, b] : _m[b, a];
                        if (cell == runColor)
                        {
                            run++;
                            if (run == 5) result += 3;
                            else if (run > 5) result++;
                        }
                        else
                        {
                            AddHistory(run, history);
                            if (!runColor) result += CountPatterns(history) * 40;
                            runColor = cell;
                            run = 1;
                        }
                    }
                    result += TerminateAndCount(runColor, run, history) * 40;
                }

            for (var y = 0; y < _size - 1; y++)
                for (var x = 0; x < _size - 1; x++)
                {
                    var c = _m[y, x];
                    if (c == _m[y, x + 1] && c == _m[y + 1, x] && c == _m[y + 1, x + 1]) result += 3;
                }

            var dark = 0;
            foreach (var cell in _m) if (cell) dark++;
            var total = _size * _size;
            var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + k * 10;
        }

        private int CountPatterns(int[] h)
        {
            var n = h[1];
            var core = n > 0 && h[2] == n && h[3] == n * 3 && h[4] == n && h[5] == n;
            return (core && h[0] >= n * 4 && h[6] >= n ? 1 : 0) + (core && h[6] >= n * 4 && h[0] >= n ? 1 : 0);
        }

        private int TerminateAndCount(bool runColor, int run, int[] h)
        {
            if (runColor)
            {
                AddHistory(run, h);
                run = 0;
            }
            run += _size;
            AddHistory(run, h);
            return CountPatterns(h);
        }

        private void AddHistory(int run, int[] h)
        {
            if (h[0] == 0) run += _size;
            Array.Copy(h, 0, h, 1, h.Length - 1);
            h[0] = run;
        }

        private static bool Bit(int value, int i) => ((value >> i) & 1) != 0;
    }
}
