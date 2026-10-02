namespace Humen.Core;

/// <summary>
/// 3D Simplex 噪声（Ken Perlin 2001 / Stefan Gustavson 2005 的经典实现）。
///
/// <b>为什么是 3D 而不是 2D</b>：§7.3/§7.4 里的 <c>p</c> 是<b>球面上的点</b>。
/// 若在 <c>(lat, lon)</c> 二维参数空间里取噪声，会同时踩两个坑 ——
/// 经度 180° 处出现一条<b>接缝</b>（噪声不连续），两极处<b>捏缩</b>（经度方向被无限压缩）。
/// 用单位球面上的 <c>(x,y,z)</c> 取噪声，二者自动消失。
///
/// <b>确定性</b>：置换表由 <see cref="Rng"/> 洗牌生成，故同 seed 逐位可复现
/// （INV-12/34）。刻意<b>不用</b> <c>System.Random</c>，理由见 <see cref="Rng"/>。
/// </summary>
public sealed class Simplex3
{
    private readonly byte[] _p = new byte[512];

    /// <summary>12 个梯度向量（平坦存储，避免交错数组的间接寻址开销）。</summary>
    private static readonly sbyte[] Grad3 =
    {
         1,  1,  0,   -1,  1,  0,    1, -1,  0,   -1, -1,  0,
         1,  0,  1,   -1,  0,  1,    1,  0, -1,   -1,  0, -1,
         0,  1,  1,    0, -1,  1,    0,  1, -1,    0, -1, -1,
    };

    private const double F3 = 1.0 / 3.0;
    private const double G3 = 1.0 / 6.0;

    public Simplex3(long seed)
    {
        var perm = new byte[256];
        for (int i = 0; i < 256; i++) perm[i] = (byte)i;

        var rng = new Rng(seed);
        rng.Shuffle(perm);

        for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
    }

    /// <summary>单层噪声，值域约 [−1, 1]。</summary>
    public double Noise(double xin, double yin, double zin)
    {
        // 1) 斜切到单纯形网格
        double s = (xin + yin + zin) * F3;
        int i = FastFloor(xin + s), j = FastFloor(yin + s), k = FastFloor(zin + s);
        double t = (i + j + k) * G3;
        double x0 = xin - (i - t), y0 = yin - (j - t), z0 = zin - (k - t);

        // 2) 判定点落在四面体的哪个角（决定第二、第三个顶点的偏移）
        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0)      { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else               { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0)       { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0)  { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else               { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        double x1 = x0 - i1 + G3,        y1 = y0 - j1 + G3,        z1 = z0 - k1 + G3;
        double x2 = x0 - i2 + 2 * G3,    y2 = y0 - j2 + 2 * G3,    z2 = z0 - k2 + 2 * G3;
        double x3 = x0 - 1 + 3 * G3,     y3 = y0 - 1 + 3 * G3,     z3 = z0 - 1 + 3 * G3;

        // 3) 四个顶点各自的径向衰减核
        int ii = i & 255, jj = j & 255, kk = k & 255;
        double n = 0;
        n += Corner(ii      + _p[jj      + _p[kk     ]], x0, y0, z0);
        n += Corner(ii + i1 + _p[jj + j1 + _p[kk + k1]], x1, y1, z1);
        n += Corner(ii + i2 + _p[jj + j2 + _p[kk + k2]], x2, y2, z2);
        n += Corner(ii + 1  + _p[jj + 1  + _p[kk + 1 ]], x3, y3, z3);
        return 32.0 * n;
    }

    private static double Corner(int h, double x, double y, double z)
    {
        double t = 0.6 - x * x - y * y - z * z;
        if (t < 0) return 0.0;
        t *= t;
        int g = (h % 12) * 3;
        return t * t * (Grad3[g] * x + Grad3[g + 1] * y + Grad3[g + 2] * z);
    }

    /// <summary>
    /// 分形叠加（fBm），§7.4 的 <c>fbm(p × freq, octaves = 6)</c>。
    /// 返回 [−1, 1]（按振幅和归一化，故不随层数漂移）。
    /// </summary>
    public double Fbm(double x, double y, double z, int octaves,
                      double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(x * freq, y * freq, z * freq);
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return norm > 0 ? sum / norm : 0.0;
    }

    /// <summary>fBm 的 [0,1] 版本。</summary>
    public double Fbm01(double x, double y, double z, int octaves,
                        double lacunarity = 2.0, double gain = 0.5)
        => 0.5 + 0.5 * Fbm(x, y, z, octaves, lacunarity, gain);

    /// <summary>
    /// 脊状分形：<c>1 − |noise|</c> 多层叠加。用于海岸线的<b>峡湾</b>感 ——
    /// 普通 fBm 给出圆钝的凸起，脊状噪声给出尖锐的锯齿状切割。
    /// </summary>
    public double Ridged(double x, double y, double z, int octaves,
                         double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, freq = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            double n = 1.0 - Math.Abs(Noise(x * freq, y * freq, z * freq));
            sum += amp * n;
            norm += amp;
            amp *= gain;
            freq *= lacunarity;
        }
        return norm > 0 ? sum / norm : 0.0;   // [0,1]
    }

    private static int FastFloor(double x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }
}
