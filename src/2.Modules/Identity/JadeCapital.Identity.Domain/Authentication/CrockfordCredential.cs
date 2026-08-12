using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Identity.Domain.Authentication;

/// <summary>
/// Crockford base-32 alphabet used to encode temporary credentials. Omits
/// I, L, O, and U to avoid visual ambiguity in emails and printed text.
/// </summary>
public static class CrockfordCredential
{
    /// <summary>Number of chars produced by <see cref="Generate"/>. 26 × 5 = 130 encoded bits from a 128-bit CSPRNG seed.</summary>
    public const int Length = 26;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Generates a 26-character Crockford credential with 128 bits of
    /// underlying entropy. Source: 16 CSPRNG bytes (128 bits); encoded as 26
    /// Crockford Base32 chars (130 bits of symbol space). No byte truncation:
    /// every seed bit is consumed; the 26th char carries the last 3 bits of
    /// the seed plus 2 deterministic padding bits (always zero).
    /// </summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[Length];

        // Emit 26 chars. Char i takes the next 5 seed bits, MSB-first,
        // starting at seed bit (127 - i*5). When the seed runs out, pad with
        // zeros in the LSB positions of the final char.
        for (var charIdx = 0; charIdx < Length; charIdx++)
        {
            int seedBitStart = 127 - (charIdx * 5); // absolute bit index of the MSB of this 5-bit window
            ulong chunk = 0;
            for (var b = 0; b < 5; b++)
            {
                int absBitPos = seedBitStart - b;
                ulong bit;
                if (absBitPos < 0)
                {
                    bit = 0UL; // padding: zero in the LSB positions of the final char
                }
                else
                {
                    int byteIdx = absBitPos / 8;
                    int bitInByte = 7 - (absBitPos % 8);
                    bit = (ulong)((bytes[byteIdx] >> bitInByte) & 1);
                }

                chunk = (chunk << 1) | bit;
            }

            chars[charIdx] = Alphabet[(int)chunk];
        }

        return new string(chars);
    }

    /// <summary>
    /// Validates a candidate Crockford credential. Accepts the canonical
    /// alphabet and normalizes lowercase letters to uppercase. Outputs the
    /// normalized form when the parse succeeds.
    /// </summary>
    public static bool TryParse(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(value)) return false;
        if (value.Length != Length) return false;

        Span<char> upper = stackalloc char[Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            if (Alphabet.IndexOf(c) < 0) return false;
            upper[i] = c;
        }

        normalized = new string(upper);
        return true;
    }
}
