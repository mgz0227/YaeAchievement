namespace YaeAchievement.Utilities;

// CRC-32-IEEE 802.3
public static class Crc32 {

    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Crc32Table = new uint[256];

    static Crc32() {
        for (uint i = 0; i < Crc32Table.Length; i++) {
            var v = i;
            for (var j = 0; j < 8; j++) {
                v = (v >> 1) ^ ((v & 1) * Polynomial);
            }
            Crc32Table[i] = v;
        }
    }

    public static uint Compute(Span<byte> buf) {
        var checksum = 0xFFFFFFFF;
        foreach (var b in buf) {
            checksum = (checksum >> 8) ^ Crc32Table[(b ^ checksum) & 0xFF];
        }
        return ~checksum;
    }
}
