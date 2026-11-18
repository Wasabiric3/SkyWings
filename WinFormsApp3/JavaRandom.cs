namespace SkyWings;

/// <summary>
/// Reimplementation of java.util.Random so the demo seed data (Random(42)) is identical to the Java version.
/// </summary>
public sealed class JavaRandom
{
    private long _seed;
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    public JavaRandom(long seed) => _seed = (seed ^ Multiplier) & Mask;

    private int Next(int bits)
    {
        _seed = unchecked(_seed * Multiplier + Addend) & Mask;
        return unchecked((int)(_seed >>> (48 - bits)));
    }

    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        int r = Next(31);
        int m = bound - 1;
        if ((bound & m) == 0)
            return (int)((bound * (long)r) >> 31);
        for (int u = r; unchecked(u - (r = u % bound) + m) < 0; u = Next(31)) { }
        return r;
    }
}
