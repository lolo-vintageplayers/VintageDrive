using System;

namespace VintageDrive.Core.Capacity
{
    /// <summary>Générateur pseudo-aléatoire déterministe (splitmix64) : reproductible depuis une graine.</summary>
    internal static class Prng
    {
        internal static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Encode/décode une « page » de 4096 octets : en-tête signé (magie, session, offset prévu)
    /// + remplissage pseudo-aléatoire vérifiable. C'est la preuve du test de capacité :
    /// si une page écrite à l'offset X est retrouvée ailleurs (ou pas retrouvée), le support ment.
    /// </summary>
    internal static class PageCodec
    {
        internal const int PageSize = 4096;
        private const ulong Magic = 0x3130305041434456UL; // « VDCAP001 » en petit-boutiste
        private const int HeaderSize = 32;

        internal enum PageKind
        {
            Self,     // la page attendue, intacte
            Foreign,  // une page valide de la même session… écrite pour un AUTRE offset (= bouclage)
            Garbage,  // contenu invalide (écriture jetée, secteur mort, autre session)
        }

        internal static void Build(byte[] buf, int at, ulong sessionSeed, long blockOffset, int pageIndex)
        {
            PutU64(buf, at + 0, Magic);
            PutU64(buf, at + 8, sessionSeed);
            PutU64(buf, at + 16, (ulong)blockOffset);
            PutU64(buf, at + 24, (uint)pageIndex | (1UL << 32)); // index de page + version 1
            ulong seed = Prng.Mix(sessionSeed ^ (ulong)(blockOffset + (long)pageIndex * PageSize));
            for (int i = at + HeaderSize; i < at + PageSize; i += 8)
            {
                seed = Prng.Mix(seed);
                PutU64(buf, i, seed);
            }
        }

        internal static PageKind Inspect(byte[] buf, int at, ulong sessionSeed, long expectedBlockOffset, int expectedPageIndex, out long claimedBlockOffset)
        {
            claimedBlockOffset = -1;
            if (GetU64(buf, at) != Magic) return PageKind.Garbage;
            if (GetU64(buf, at + 8) != sessionSeed) return PageKind.Garbage; // autre session = pas une preuve

            long bo = (long)GetU64(buf, at + 16);
            int pi = (int)(GetU64(buf, at + 24) & 0xFFFFFFFF);

            // le contenu pseudo-aléatoire doit correspondre à l'en-tête, sinon c'est du bruit
            ulong seed = Prng.Mix(sessionSeed ^ (ulong)(bo + (long)pi * PageSize));
            for (int i = at + HeaderSize; i < at + PageSize; i += 8)
            {
                seed = Prng.Mix(seed);
                if (GetU64(buf, i) != seed) return PageKind.Garbage;
            }

            claimedBlockOffset = bo;
            return bo == expectedBlockOffset && pi == expectedPageIndex ? PageKind.Self : PageKind.Foreign;
        }

        private static void PutU64(byte[] b, int o, ulong v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
            b[o + 4] = (byte)(v >> 32);
            b[o + 5] = (byte)(v >> 40);
            b[o + 6] = (byte)(v >> 48);
            b[o + 7] = (byte)(v >> 56);
        }

        private static ulong GetU64(byte[] b, int o)
        {
            return (ulong)b[o]
                 | ((ulong)b[o + 1] << 8)
                 | ((ulong)b[o + 2] << 16)
                 | ((ulong)b[o + 3] << 24)
                 | ((ulong)b[o + 4] << 32)
                 | ((ulong)b[o + 5] << 40)
                 | ((ulong)b[o + 6] << 48)
                 | ((ulong)b[o + 7] << 56);
        }
    }
}
