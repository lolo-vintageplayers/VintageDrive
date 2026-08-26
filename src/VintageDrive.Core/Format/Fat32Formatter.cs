using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Format
{
    public sealed class Fat32FormatOptions
    {
        /// <summary>Taille de cluster en octets (puissance de 2, du secteur à 64 Kio). 32 Kio = le réglage consoles.</summary>
        public int ClusterBytes { get; set; } = 32 << 10;

        /// <summary>Étiquette de volume (11 caractères max, mise en majuscules).</summary>
        public string Label { get; set; } = "";

        /// <summary>Écrase le premier et le dernier Mio du disque (restes de MBR/GPT) avant de partitionner.</summary>
        public bool WipeEnds { get; set; } = true;
    }

    public sealed class Fat32FormatReport
    {
        public long PartitionOffsetBytes { get; internal set; }
        public long PartitionBytes { get; internal set; }
        public long ClusterCount { get; internal set; }
        public int ClusterBytes { get; internal set; }
        public long FatSectors { get; internal set; }
        public TimeSpan Duration { get; internal set; }
    }

    /// <summary>
    /// Formateur FAT32 sans la limite artificielle des 32 Go (jusqu'à 2 To en secteurs de 512 o).
    /// Implémentation originale d'après la « Microsoft FAT32 File System Specification »
    /// (aucun code repris de fat32format/guiformat). Produit : MBR + partition unique type 0x0C
    /// alignée sur 1 Mio, secteur de boot FAT32 + FSInfo (+ copies de secours au secteur 6/7),
    /// deux FAT miroir, répertoire racine avec étiquette. La zone de données est elle aussi
    /// alignée sur 1 Mio (la taille des FAT est rembourrée pour ça) : perfs optimales sur flash.
    /// </summary>
    public static class Fat32Formatter
    {
        private const int ReservedSectors = 32; // standard FAT32

        public static Fat32FormatReport FormatDisk(PhysicalDisk disk, Fat32FormatOptions? options, Action<string>? log, CancellationToken ct)
        {
            var opt = options ?? new Fat32FormatOptions();
            log ??= _ => { };
            if (disk.IsSystemDisk) throw new InvalidOperationException("Refus : disque système.");

            int bps = disk.BytesPerSector > 0 ? disk.BytesPerSector : 512;
            if (bps < 512 || bps > 4096 || (bps & (bps - 1)) != 0)
                throw new InvalidOperationException($"Taille de secteur inattendue : {bps} octets.");
            if (opt.ClusterBytes < bps || opt.ClusterBytes > 64 << 10 || (opt.ClusterBytes & (opt.ClusterBytes - 1)) != 0)
                throw new InvalidOperationException("Taille de cluster invalide : puissance de 2, entre la taille de secteur et 64 Kio.");
            if (disk.SizeBytes < 128L << 20)
                throw new InvalidOperationException("Disque trop petit pour du FAT32 (128 Mio minimum).");

            int spc = opt.ClusterBytes / bps;
            long totalSec = disk.SizeBytes / bps;
            long partStart = (1L << 20) / bps; // partition alignée sur 1 Mio
            long partSec = totalSec - partStart;
            if (partSec > uint.MaxValue)
                throw new InvalidOperationException("Au-delà de 2 To, FAT32/MBR est impossible (limite du format) : utilise exFAT.");

            // Taille de FAT : point fixe (la taille des FAT dépend du nombre de clusters qui dépend
            // de la place restante), puis rembourrage pour aligner la zone de données sur 1 Mio.
            long fatSz = 0;
            for (int i = 0; i < 8; i++)
            {
                long c = (partSec - ReservedSectors - 2 * fatSz) / spc;
                long needed = ((c + 2) * 4 + bps - 1) / bps;
                if (needed == fatSz) break;
                fatSz = needed;
            }
            long align = (1L << 20) / bps;
            while ((ReservedSectors + 2 * fatSz) % align != 0) fatSz++;

            long clusterCount = (partSec - ReservedSectors - 2 * fatSz) / spc;
            if (clusterCount < 65525)
                throw new InvalidOperationException($"Trop peu de clusters ({clusterCount}) pour du FAT32 valide : choisis des clusters plus petits.");
            if (clusterCount > 0x0FFFFFF4)
                throw new InvalidOperationException("Trop de clusters pour FAT32 : choisis des clusters plus gros.");

            var report = new Fat32FormatReport
            {
                PartitionOffsetBytes = partStart * bps,
                PartitionBytes = partSec * bps,
                ClusterBytes = opt.ClusterBytes,
                ClusterCount = clusterCount,
                FatSectors = fatSz,
            };
            var chrono = Stopwatch.StartNew();

            using var handle = DeviceIo.TryOpen(disk.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_FLAG_NO_BUFFERING,
                out int err) ?? throw new IOException($"Ouverture de {disk.DevicePath} impossible (code Win32 {err}). Admin requis.");

            log($"Verrouillage des volumes du disque {disk.Index}…");
            var locker = VolumeLocker.LockDisk(disk);
            try
            {
                using var io = new AlignedBuffer(8 << 20);
                var zero = new byte[8 << 20];
                var sector = new byte[bps];

                void Put(long offsetBytes, byte[] data) => DeviceIo.WriteAt(handle, offsetBytes, io, data, data.Length);

                if (opt.WipeEnds)
                {
                    log("Nettoyage des extrémités du disque (anciens MBR/GPT)…");
                    DeviceIo.WriteAt(handle, 0, io, zero, 1 << 20);
                    long tail = totalSec * bps - (1L << 20);
                    DeviceIo.WriteAt(handle, tail, io, zero, 1 << 20);
                }

                log($"Partition MBR (départ 1 Mio) + structures FAT32 (clusters {opt.ClusterBytes >> 10} Kio)…");

                // ── MBR
                Array.Clear(sector, 0, bps);
                Mbr.Build(sector, partStart, partSec, PartitionTypes.Fat32Lba);
                Put(0, sector);

                // ── Zone réservée à zéro, puis boot + FSInfo (+ copies au secteur 6/7)
                DeviceIo.WriteAt(handle, partStart * bps, io, zero, ReservedSectors * bps);
                var boot = new byte[bps];
                BuildBootSector(boot, bps, spc, partStart, partSec, fatSz, opt.Label);
                var fsi = new byte[bps];
                BuildFsInfo(fsi, clusterCount);
                Put(partStart * bps, boot);
                Put((partStart + 1) * bps, fsi);
                Put((partStart + 6) * bps, boot);
                Put((partStart + 7) * bps, fsi);

                // ── Les deux FAT : zéros partout, puis le premier secteur (entrées 0, 1 et racine)
                long fat1 = partStart + ReservedSectors;
                long fat2 = fat1 + fatSz;
                log($"Écriture des tables FAT (2 × {fatSz * bps / (1 << 20)} Mio)…");
                ZeroRange(handle, io, zero, fat1 * bps, fatSz * bps, ct);
                ZeroRange(handle, io, zero, fat2 * bps, fatSz * bps, ct);
                Array.Clear(sector, 0, bps);
                PutU32(sector, 0, 0x0FFFFFF8); // FAT[0] : octet média 0xF8
                PutU32(sector, 4, 0x0FFFFFFF); // FAT[1] : fin de chaîne + drapeaux « arrêt propre »
                PutU32(sector, 8, 0x0FFFFFFF); // FAT[2] : répertoire racine (cluster 2), fin de chaîne
                Put(fat1 * bps, sector);
                Put(fat2 * bps, sector);

                // ── Répertoire racine : cluster 2 à zéro + entrée d'étiquette de volume
                long dataStart = partStart + ReservedSectors + 2 * fatSz;
                ZeroRange(handle, io, zero, dataStart * bps, (long)spc * bps, ct);
                string label = NormalizeLabel(opt.Label);
                if (label.Length > 0)
                {
                    Array.Clear(sector, 0, bps);
                    for (int i = 0; i < 11; i++) sector[i] = (byte)(i < label.Length ? label[i] : ' ');
                    sector[11] = 0x08; // ATTR_VOLUME_ID
                    Put(dataStart * bps, sector);
                }

                NativeMethods.FlushFileBuffers(handle);
            }
            finally
            {
                locker.Dispose();
            }

            log("Notification à Windows (relecture de la table de partitions)…");
            DeviceIo.Control(handle, NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES);

            report.Duration = chrono.Elapsed;
            return report;
        }

        private static void ZeroRange(SafeFileHandle handle, AlignedBuffer io, byte[] zero, long offset, long count, CancellationToken ct)
        {
            while (count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(zero.Length, count);
                DeviceIo.WriteAt(handle, offset, io, zero, n);
                offset += n;
                count -= n;
            }
        }

        private static void BuildBootSector(byte[] bs, int bps, int spc, long partStart, long partSec, long fatSz, string rawLabel)
        {
            bs[0] = 0xEB; bs[1] = 0x58; bs[2] = 0x90;         // jmp court + nop
            WriteAscii(bs, 3, "VINTAGE", 8);                 // OEM
            PutU16(bs, 11, (ushort)bps);
            bs[13] = (byte)spc;
            PutU16(bs, 14, ReservedSectors);
            bs[16] = 2;                                        // deux FAT
            // RootEntCnt(17), TotSec16(19), FATSz16(22) : zéro en FAT32
            bs[21] = 0xF8;                                     // média « disque fixe »
            PutU16(bs, 24, 63);                                // géométrie legacy (ignorée en LBA)
            PutU16(bs, 26, 255);
            PutU32(bs, 28, (uint)partStart);                   // secteurs cachés = départ de partition
            PutU32(bs, 32, (uint)partSec);
            PutU32(bs, 36, (uint)fatSz);
            // ExtFlags(40)=0 : FAT en miroir ; FSVer(42)=0
            PutU32(bs, 44, 2);                                 // répertoire racine = cluster 2
            PutU16(bs, 48, 1);                                 // FSInfo au secteur 1
            PutU16(bs, 50, 6);                                 // copie du boot au secteur 6
            bs[64] = 0x80;                                     // n° de lecteur BIOS
            bs[66] = 0x29;                                     // signature BPB étendu
            PutU32(bs, 67, NewVolumeId());
            string label = NormalizeLabel(rawLabel);
            WriteAscii(bs, 71, label.Length > 0 ? label : "NO NAME", 11);
            WriteAscii(bs, 82, "FAT32", 8);
            bs[510] = 0x55; bs[511] = 0xAA;
        }

        private static void BuildFsInfo(byte[] fsi, long clusterCount)
        {
            PutU32(fsi, 0, 0x41615252);                        // « RRaA »
            PutU32(fsi, 484, 0x61417272);                      // « rrAa »
            PutU32(fsi, 488, (uint)(clusterCount - 1));        // clusters libres (la racine en occupe un)
            PutU32(fsi, 492, 3);                               // prochain cluster libre probable
            fsi[510] = 0x55; fsi[511] = 0xAA;
        }

        internal static string NormalizeLabel(string label)
        {
            var sb = new StringBuilder();
            foreach (char c in (label ?? "").ToUpperInvariant())
            {
                if (sb.Length == 11) break;
                if ((char.IsLetterOrDigit(c) && c < 128) || c == ' ' || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.ToString().TrimEnd();
        }

        private static uint NewVolumeId()
        {
            long t = DateTime.Now.Ticks;
            return (uint)t ^ (uint)(t >> 32);
        }

        private static void WriteAscii(byte[] b, int at, string text, int width)
        {
            for (int i = 0; i < width; i++)
                b[at + i] = (byte)(i < text.Length ? text[i] : ' ');
        }

        private static void PutU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
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
