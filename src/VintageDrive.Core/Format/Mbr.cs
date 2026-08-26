using System;

namespace VintageDrive.Core.Format
{
    /// <summary>Types de partition MBR utilisés par VintageDrive.</summary>
    public static class PartitionTypes
    {
        public const byte Fat32Lba = 0x0C;
        public const byte Ifs = 0x07; // NTFS / exFAT
    }

    /// <summary>Construction d'un MBR à partition unique (adressage LBA, champs CHS neutralisés).</summary>
    internal static class Mbr
    {
        internal static void Build(byte[] mbr, long partStartSec, long partSec, byte partitionType)
        {
            // entrée de partition n°1 à l'offset 446
            mbr[446] = 0x00;                                   // non amorçable
            mbr[447] = 0xFE; mbr[448] = 0xFF; mbr[449] = 0xFF; // CHS début (neutralisé)
            mbr[450] = partitionType;
            mbr[451] = 0xFE; mbr[452] = 0xFF; mbr[453] = 0xFF; // CHS fin (neutralisé)
            PutU32(mbr, 454, (uint)partStartSec);
            PutU32(mbr, 458, (uint)partSec);
            mbr[510] = 0x55; mbr[511] = 0xAA;
        }

        private static void PutU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }
    }
}
