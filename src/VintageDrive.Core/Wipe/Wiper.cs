using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Wipe
{
    public sealed class WipeReport
    {
        public long BytesWritten { get; internal set; }
        public int WriteErrors { get; internal set; }
        public double AvgWriteMBps { get; internal set; }
        public TimeSpan Duration { get; internal set; }
    }

    /// <summary>
    /// Effacements par remplissage de zéros — une seule passe : sur de la flash, les rituels
    /// « 7 passes / 35 passes » hérités des disques magnétiques des années 90 n'effacent pas
    /// mieux et usent les cellules pour rien.
    /// </summary>
    public static class Wiper
    {
        private const int Chunk = 8 << 20; // 8 Mio par E/S

        /// <summary>
        /// « Nettoyage rapide » : zéros sur les premiers et derniers 8 Mio. Détruit MBR/GPT
        /// (y compris le GPT de secours en fin de disque), secteurs de boot et débuts de FAT :
        /// c'est le geste qui débloque les supports au partitionnement récalcitrant.
        /// </summary>
        public static WipeReport QuickClean(PhysicalDisk disk, Action<string>? log, CancellationToken ct)
            => Run(disk, log, ct, quick: true);

        /// <summary>Effacement complet (« bas niveau ») : zéros sur 100 % de la surface.</summary>
        public static WipeReport FullWipe(PhysicalDisk disk, Action<string>? log, CancellationToken ct)
            => Run(disk, log, ct, quick: false);

        private static WipeReport Run(PhysicalDisk disk, Action<string>? log, CancellationToken ct, bool quick)
        {
            log ??= _ => { };
            if (disk.IsSystemDisk) throw new InvalidOperationException("Refus : disque système.");
            if (disk.SizeBytes < 32L << 20) throw new InvalidOperationException("Disque trop petit ou taille inconnue.");

            int bps = disk.BytesPerSector > 0 ? disk.BytesPerSector : 512;
            long size = disk.SizeBytes - disk.SizeBytes % bps;

            var r = new WipeReport();
            var chrono = Stopwatch.StartNew();

            using var handle = DeviceIo.TryOpen(disk.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_FLAG_NO_BUFFERING,
                out int err) ?? throw new IOException($"Ouverture de {disk.DevicePath} impossible (code Win32 {err}). Admin requis.");

            log($"Verrouillage des volumes du disque {disk.Index}…");
            var locker = VolumeLocker.LockDisk(disk);
            try
            {
                using var io = new AlignedBuffer(Chunk);
                var zero = new byte[Chunk];

                if (quick)
                {
                    log("Nettoyage rapide : zéros sur les premiers et derniers 8 Mio…");
                    WriteZeros(handle, io, zero, 0, Math.Min(Chunk, size), r, ct);
                    if (size > 2L * Chunk)
                        WriteZeros(handle, io, zero, size - Chunk, Chunk, r, ct);
                }
                else
                {
                    log($"Effacement complet : {size >> 30} Gio à zéro (une passe)…");
                    long pos = 0, lastLog = 0;
                    var sw = Stopwatch.StartNew();
                    while (pos < size)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = (int)Math.Min(Chunk, size - pos);
                        try { DeviceIo.WriteAt(handle, pos, io, zero, n); }
                        catch (IOException) { r.WriteErrors++; }
                        pos += n;
                        r.BytesWritten = pos;
                        if (pos - lastLog >= 2L << 30)
                        {
                            lastLog = pos;
                            double mbps = pos / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                            double etaMin = (size - pos) / 1e6 / Math.Max(1, mbps) / 60;
                            log($"  {pos >> 30}/{size >> 30} Gio — {mbps:F1} Mo/s — reste ~{etaMin:F0} min");
                        }
                    }
                }
                NativeMethods.FlushFileBuffers(handle);
            }
            finally
            {
                locker.Dispose();
            }
            DeviceIo.Control(handle, NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES);

            r.AvgWriteMBps = r.BytesWritten / 1e6 / Math.Max(0.001, chrono.Elapsed.TotalSeconds);
            r.Duration = chrono.Elapsed;
            return r;
        }

        private static void WriteZeros(SafeFileHandle handle, AlignedBuffer io, byte[] zero, long offset, long count, WipeReport r, CancellationToken ct)
        {
            while (count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(zero.Length, count);
                try { DeviceIo.WriteAt(handle, offset, io, zero, n); }
                catch (IOException) { r.WriteErrors++; }
                offset += n;
                count -= n;
                r.BytesWritten += n;
            }
        }
    }
}
