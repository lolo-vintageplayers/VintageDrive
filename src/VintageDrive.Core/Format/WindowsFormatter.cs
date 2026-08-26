using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Format
{
    public enum TargetFs { Fat32, ExFat, Ntfs }

    /// <summary>
    /// Formatage exFAT / NTFS via le formateur de Windows lui-même (fmifs.dll : FormatEx,
    /// l'API derrière la boîte de dialogue de l'Explorateur). On pose d'abord la partition MBR
    /// nous-mêmes, Windows la monte en RAW avec une lettre, puis on lui demande d'y écrire le
    /// système de fichiers. Pas de bride de taille ici : Windows ne limite que le FAT32.
    /// </summary>
    public static class WindowsFormatter
    {
        private delegate byte FmifsCallback(uint command, uint subAction, IntPtr actionInfo);

        [DllImport("fmifs.dll", CharSet = CharSet.Unicode)]
        private static extern void FormatEx(string driveRoot, uint mediaFlag, string fileSystem,
            string label, [MarshalAs(UnmanagedType.Bool)] bool quickFormat, uint clusterSize, FmifsCallback callback);

        private const uint FMIFS_HARDDISK = 0x0C;
        private const uint CB_PROGRESS = 0x00;
        private const uint CB_INSUFFICIENT_RIGHTS = 0x06;
        private const uint CB_FS_NOT_SUPPORTED = 0x07;
        private const uint CB_VOLUME_IN_USE = 0x08;
        private const uint CB_CANT_QUICK_FORMAT = 0x09;
        private const uint CB_DONE = 0x0B;
        private const uint CB_OUTPUT = 0x0E;
        private const uint CB_STRUCTURE_PROGRESS = 0x0F;
        private const uint CB_CLUSTER_TOO_SMALL = 0x10;

        /// <summary>Partitionne le disque (MBR, type 0x07) puis le fait formater en exFAT ou NTFS par Windows.</summary>
        public static TimeSpan PrepareAndFormatDisk(PhysicalDisk disk, TargetFs fs, string label, int clusterBytes, Action<string>? log, CancellationToken ct)
        {
            if (fs == TargetFs.Fat32)
                throw new ArgumentException("Le FAT32 passe par Fat32Formatter (implémentation maison), pas par Windows.");
            log ??= _ => { };
            if (disk.IsSystemDisk) throw new InvalidOperationException("Refus : disque système.");
            if (disk.SizeBytes < 64L << 20) throw new InvalidOperationException("Disque trop petit ou taille inconnue.");

            int bps = disk.BytesPerSector > 0 ? disk.BytesPerSector : 512;
            long totalSec = disk.SizeBytes / bps;
            long partStart = (1L << 20) / bps;
            long partSec = totalSec - partStart;
            if (partSec > uint.MaxValue)
                throw new InvalidOperationException("Au-delà de 2 To il faut une table GPT — pas encore gérée par VintageDrive (prévu).");

            var chrono = Stopwatch.StartNew();

            // ── Partition MBR posée par nos soins
            using (var handle = DeviceIo.TryOpen(disk.DevicePath,
                       NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                       NativeMethods.FILE_FLAG_NO_BUFFERING,
                       out int err) ?? throw new IOException($"Ouverture de {disk.DevicePath} impossible (code Win32 {err}). Admin requis."))
            {
                log($"Verrouillage des volumes du disque {disk.Index}…");
                var locker = VolumeLocker.LockDisk(disk);
                try
                {
                    using var io = new AlignedBuffer(1 << 20);
                    var zero = new byte[1 << 20];
                    log("Nettoyage des extrémités + partition MBR (type 0x07)…");
                    DeviceIo.WriteAt(handle, 0, io, zero, 1 << 20);
                    DeviceIo.WriteAt(handle, totalSec * bps - (1L << 20), io, zero, 1 << 20);
                    var mbr = new byte[bps];
                    Mbr.Build(mbr, partStart, partSec, PartitionTypes.Ifs);
                    DeviceIo.WriteAt(handle, 0, io, mbr, bps);
                    NativeMethods.FlushFileBuffers(handle);
                }
                finally
                {
                    locker.Dispose();
                }
                DeviceIo.Control(handle, NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES);
            }

            // ── Attendre que Windows monte la partition RAW et lui donne une lettre
            log("Attente de la lettre de lecteur…");
            string? letter = null;
            for (int i = 0; i < 30 && letter == null; i++)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(500);
                var d = DiskEnumerator.GetDisks().FirstOrDefault(x => x.Index == disk.Index);
                letter = d != null && d.Volumes.Count > 0 ? d.Volumes[0].Letter : null;
            }
            if (letter == null)
                throw new IOException("Windows n'a pas monté la nouvelle partition (pas de lettre). Débranche/rebranche le support puis réessaie.");
            Thread.Sleep(1000); // laisse le montage se stabiliser avant de formater

            // ── FormatEx : le formateur de Windows fait le système de fichiers
            string fsName = fs == TargetFs.ExFat ? "EXFAT" : "NTFS";
            string lbl = (label ?? "").Trim();
            int maxLabel = fs == TargetFs.ExFat ? 11 : 32;
            if (lbl.Length > maxLabel) lbl = lbl.Substring(0, maxLabel);

            log($"Formatage {fsName} de {letter} par Windows…");
            bool done = false, success = false;
            string? failure = null;
            var output = new List<string>();
            int lastPct = -10;

            FmifsCallback cb = (command, subAction, actionInfo) =>
            {
                switch (command)
                {
                    case CB_PROGRESS:
                        int pct = actionInfo != IntPtr.Zero ? Marshal.ReadInt32(actionInfo) : 0;
                        if (pct >= lastPct + 10) { lastPct = pct; log($"  {pct} %…"); }
                        break;
                    case CB_DONE:
                        done = true;
                        success = actionInfo != IntPtr.Zero && Marshal.ReadByte(actionInfo) != 0;
                        break;
                    case CB_OUTPUT:
                        // TEXTOUTPUT { ULONG Lines; PCHAR Output; } : les vrais messages du formateur
                        if (actionInfo != IntPtr.Zero)
                        {
                            IntPtr strPtr = Marshal.ReadIntPtr(actionInfo, IntPtr.Size);
                            string? txt = strPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(strPtr) : null;
                            if (!string.IsNullOrWhiteSpace(txt))
                            {
                                output.Add(txt!.Trim());
                                log($"  fmifs : {txt!.Trim()}");
                            }
                        }
                        break;
                    case CB_CANT_QUICK_FORMAT: failure = "Windows refuse le formatage rapide de ce volume (fmifs 0x9)"; break;
                    case CB_VOLUME_IN_USE: failure = $"volume {letter} occupé (ferme ce qui l'utilise)"; break;
                    case CB_INSUFFICIENT_RIGHTS: failure = "droits insuffisants"; break;
                    case CB_FS_NOT_SUPPORTED: failure = $"{fsName} non disponible sur ce Windows"; break;
                    case CB_CLUSTER_TOO_SMALL: failure = "taille de cluster trop petite pour ce volume"; break;
                    case CB_STRUCTURE_PROGRESS: break; // avancement interne, sans intérêt
                    default:
                        log($"  (fmifs : commande 0x{command:X}, sous-code 0x{subAction:X})");
                        break;
                }
                return 1; // continuer
            };

            // Type de média RÉEL : l'Explorateur passe « amovible » pour une clé USB, et fmifs
            // répond CANT_QUICK_FORMAT (0x9) quand on lui ment en déclarant « disque fixe ».
            uint media = disk.MediaType > 0 ? (uint)disk.MediaType : FMIFS_HARDDISK;

            bool TryFormat(string root)
            {
                done = false; success = false; lastPct = -10; failure = null; output.Clear();
                FormatEx(root, media, fsName, lbl, true, (uint)clusterBytes, cb);
                return done && success;
            }

            bool ok = TryFormat(letter + "\\");
            if (!ok)
            {
                string volGuid = GetVolumeGuidPath(letter);
                if (volGuid.Length > 0)
                {
                    log($"  Nouvel essai via le chemin de volume {volGuid}…");
                    ok = TryFormat(volGuid);
                }
            }
            GC.KeepAlive(cb);

            if (!ok)
            {
                string detail = failure ?? (output.Count > 0 ? string.Join(" / ", output) : "cause non précisée par FormatEx");
                throw new IOException($"Échec du formatage Windows : {detail}.");
            }

            return chrono.Elapsed;
        }

        /// <summary>Chemin GUID du volume (« \\?\Volume{…} », sans barre finale) — vide si introuvable.</summary>
        private static string GetVolumeGuidPath(string letter)
        {
            var sb = new System.Text.StringBuilder(64);
            if (!NativeMethods.GetVolumeNameForVolumeMountPointW(letter + "\\", sb, sb.Capacity))
                return "";
            return sb.ToString().TrimEnd('\\');
        }
    }
}
