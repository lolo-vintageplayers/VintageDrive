using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Capacity
{
    /// <summary>
    /// Test complet façon H2testw : écrit 100 % de la surface avec des pages signées,
    /// puis relit tout. Long (dépend de la vitesse du support), mais définitif :
    /// le total d'octets vérifiés EST la capacité réellement utilisable.
    /// DESTRUCTIF : écrase tout, table de partitions comprise.
    /// </summary>
    public static class FullSurfaceTest
    {
        private const int Chunk = 8 << 20; // 8 Mio par E/S

        public static FullTestResult Run(PhysicalDisk disk, Action<string>? log, CancellationToken ct, Action<ProbeProgress>? progress = null)
        {
            log ??= _ => { };
            progress ??= _ => { };
            if (disk.IsSystemDisk) throw new InvalidOperationException("Refus : disque système.");
            long size = disk.SizeBytes;
            if (size < 64L << 20) throw new InvalidOperationException("Disque trop petit ou taille inconnue.");

            var r = new FullTestResult { ClaimedBytes = size };
            var chrono = Stopwatch.StartNew();
            ulong seed = (ulong)Guid.NewGuid().GetHashCode() << 32 ^ (ulong)Stopwatch.GetTimestamp();
            long usable = size - size % PageCodec.PageSize;

            log($"Verrouillage des volumes du disque {disk.Index}…");
            using var locker = VolumeLocker.LockDisk(disk);
            using var handle = DeviceIo.TryOpen(disk.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_FLAG_NO_BUFFERING,
                out int err) ?? throw new IOException($"Ouverture de {disk.DevicePath} impossible (code Win32 {err}). Admin requis.");

            using var io = new AlignedBuffer(Chunk);
            var staging = new byte[Chunk];

            // ── Passe 1 : écriture de toute la surface
            log($"Passe 1/2 : écriture de {usable >> 30} Gio…");
            var sw = Stopwatch.StartNew();
            long pos = 0, lastLog = 0;
            while (pos < usable)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Chunk, usable - pos);
                n -= n % PageCodec.PageSize;
                if (n == 0) break;

                for (int p = 0; p * PageCodec.PageSize < n; p++)
                    PageCodec.Build(staging, p * PageCodec.PageSize, seed, pos, p);
                try { DeviceIo.WriteAt(handle, pos, io, staging, n); }
                catch (IOException) { r.WriteErrors++; }

                pos += n;
                progress(new ProbeProgress { Phase = "Écriture de 100 % de la surface (1/2)", Done = (int)(pos >> 20), Total = (int)(usable >> 20) });
                if (pos - lastLog >= 1L << 30)
                {
                    lastLog = pos;
                    log($"  écrit {pos >> 30}/{usable >> 30} Gio — {pos / 1e6 / sw.Elapsed.TotalSeconds:F1} Mo/s de moyenne");
                }
            }
            NativeMethods.FlushFileBuffers(handle);
            r.AvgWriteMBps = pos / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds);

            // ── Passe 2 : relecture et vérification de chaque page
            log("Passe 2/2 : relecture et vérification…");
            sw.Restart();
            pos = 0; lastLog = 0;
            long good = 0;
            while (pos < usable)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Chunk, usable - pos);
                n -= n % PageCodec.PageSize;
                if (n == 0) break;

                bool readOk = true;
                try { DeviceIo.ReadAt(handle, pos, io, staging, n); }
                catch (IOException)
                {
                    r.ReadErrors++;
                    if (r.FirstMismatch < 0) r.FirstMismatch = pos;
                    readOk = false;
                }

                if (readOk)
                {
                    for (int p = 0; p * PageCodec.PageSize < n; p++)
                    {
                        var kind = PageCodec.Inspect(staging, p * PageCodec.PageSize, seed, pos, p, out _);
                        if (kind == PageCodec.PageKind.Self) good += PageCodec.PageSize;
                        else if (r.FirstMismatch < 0) r.FirstMismatch = pos + (long)p * PageCodec.PageSize;
                    }
                }

                pos += n;
                progress(new ProbeProgress { Phase = "Relecture et vérification (2/2)", Done = (int)(pos >> 20), Total = (int)(usable >> 20) });
                if (pos - lastLog >= 1L << 30)
                {
                    lastLog = pos;
                    log($"  vérifié {pos >> 30}/{usable >> 30} Gio — {good >> 30} Gio conformes pour l'instant");
                }
            }
            r.AvgReadMBps = pos / 1e6 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
            r.GoodBytes = good;
            r.Duration = chrono.Elapsed;
            return r;
        }
    }
}
