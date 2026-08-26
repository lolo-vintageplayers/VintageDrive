#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Disks
{
    /// <summary>Une partition telle qu'inscrite dans la table (montée ou non, visible ou non).</summary>
    public sealed class PartitionEntry
    {
        public int Number { get; internal set; }
        public long OffsetBytes { get; internal set; }
        public long LengthBytes { get; internal set; }
        public string TypeName { get; internal set; } = "";
        public string GptName { get; internal set; } = "";
        public bool IsBootFlagged { get; internal set; }
        public string Letter { get; internal set; } = ""; // lettre du volume monté correspondant, sinon vide
    }

    /// <summary>Détails d'un volume monté (lettre).</summary>
    public sealed class VolumeDetails
    {
        public string Letter { get; internal set; } = "";
        public string FileSystem { get; internal set; } = "";
        public string Label { get; internal set; } = "";
        public string SerialHex { get; internal set; } = "";
        public int ClusterBytes { get; internal set; }
        public long TotalBytes { get; internal set; }
        public long FreeBytes { get; internal set; }
    }

    public sealed class DiskDetails
    {
        public string PartitionStyle { get; internal set; } = "RAW";
        public List<PartitionEntry> Partitions { get; } = new List<PartitionEntry>();
        public List<VolumeDetails> Volumes { get; } = new List<VolumeDetails>();
    }

    /// <summary>
    /// Inspection détaillée d'un disque : table de partitions COMPLÈTE (y compris les partitions
    /// non montées — le piège classique des clés multi-partitions avec les loaders consoles),
    /// et détails des volumes (numéro de série, taille de cluster, occupé/libre).
    /// Lecture seule, sans droits admin.
    /// </summary>
    public static class DiskInspector
    {
        public static DiskDetails GetDetails(PhysicalDisk disk)
        {
            var details = new DiskDetails();

            using (var handle = DeviceIo.TryOpen(disk.DevicePath, 0, out _))
            {
                if (handle != null)
                {
                    var buf = DeviceIo.Query(handle, NativeMethods.IOCTL_DISK_GET_DRIVE_LAYOUT_EX, null, 0x8000);
                    if (buf != null) ParseLayout(buf, details);
                }
            }

            foreach (var v in disk.Volumes)
            {
                details.Volumes.Add(ReadVolumeDetails(v));
                var extent = GetExtent(v.Letter);
                if (extent.disk == disk.Index)
                    foreach (var p in details.Partitions)
                        if (p.OffsetBytes == extent.offset)
                            p.Letter = v.Letter;
            }
            return details;
        }

        // ── DRIVE_LAYOUT_INFORMATION_EX : en-tête 48 octets, entrées de 144 octets ──
        private static void ParseLayout(byte[] b, DiskDetails d)
        {
            int style = BitConverter.ToInt32(b, 0);
            int count = BitConverter.ToInt32(b, 4);
            d.PartitionStyle = style == 0 ? "MBR" : style == 1 ? "GPT" : "RAW";

            const int HeaderSize = 48;
            const int Stride = 144;
            for (int i = 0; i < count; i++)
            {
                int at = HeaderSize + i * Stride;
                if (at + Stride > b.Length) break;

                int pStyle = BitConverter.ToInt32(b, at);
                long offset = BitConverter.ToInt64(b, at + 8);
                long length = BitConverter.ToInt64(b, at + 16);
                if (length <= 0) continue;

                var entry = new PartitionEntry
                {
                    Number = BitConverter.ToInt32(b, at + 24),
                    OffsetBytes = offset,
                    LengthBytes = length,
                };

                if (pStyle == 0) // MBR
                {
                    byte type = b[at + 32];
                    if (type == 0) continue; // entrée vide
                    entry.IsBootFlagged = b[at + 33] != 0;
                    entry.TypeName = MbrTypeName(type);
                }
                else if (pStyle == 1) // GPT
                {
                    var typeGuid = new Guid(SubArray(b, at + 32, 16));
                    entry.TypeName = GptTypeName(typeGuid);
                    entry.GptName = Encoding.Unicode.GetString(b, at + 72, 72).TrimEnd('\0').Trim();
                }
                else
                {
                    entry.TypeName = "inconnue";
                }
                d.Partitions.Add(entry);
            }
        }

        private static (int disk, long offset) GetExtent(string letter)
        {
            using (var h = DeviceIo.TryOpen($@"\\.\{letter}", 0, out _))
            {
                if (h == null) return (-1, -1);
                var buf = DeviceIo.Query(h, NativeMethods.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0x400);
                if (buf == null || BitConverter.ToInt32(buf, 0) < 1) return (-1, -1);
                return (BitConverter.ToInt32(buf, 8), BitConverter.ToInt64(buf, 16));
            }
        }

        private static VolumeDetails ReadVolumeDetails(VolumeInfo v)
        {
            var det = new VolumeDetails
            {
                Letter = v.Letter,
                FileSystem = v.IsReady ? v.FileSystem : "RAW / non formaté",
                Label = v.Label,
                TotalBytes = v.TotalBytes,
                FreeBytes = v.FreeBytes,
            };
            string root = v.Letter + "\\";
            var volName = new StringBuilder(261);
            var fsName = new StringBuilder(261);
            if (NativeMethods.GetVolumeInformationW(root, volName, volName.Capacity,
                out uint serial, out _, out _, fsName, fsName.Capacity))
            {
                det.SerialHex = $"{serial >> 16:X4}-{serial & 0xFFFF:X4}";
                if (det.Label.Length == 0) det.Label = volName.ToString();
                if (fsName.Length > 0) det.FileSystem = fsName.ToString();
            }
            if (NativeMethods.GetDiskFreeSpaceW(root, out uint spc, out uint bps, out _, out _))
                det.ClusterBytes = (int)(spc * bps);
            return det;
        }

        private static byte[] SubArray(byte[] b, int at, int len)
        {
            var r = new byte[len];
            Array.Copy(b, at, r, 0, len);
            return r;
        }

        private static string MbrTypeName(byte type)
        {
            switch (type)
            {
                case 0x01: return "FAT12";
                case 0x04: return "FAT16 (< 32 Mo)";
                case 0x05: return "Étendue";
                case 0x06: return "FAT16";
                case 0x07: return "NTFS / exFAT";
                case 0x0B: return "FAT32";
                case 0x0C: return "FAT32 (LBA)";
                case 0x0E: return "FAT16 (LBA)";
                case 0x0F: return "Étendue (LBA)";
                case 0x27: return "Récupération Windows";
                case 0x82: return "Linux swap";
                case 0x83: return "Linux";
                case 0x8E: return "Linux LVM";
                case 0xAB:
                case 0xAF: return "Apple / macOS";
                case 0xEE: return "GPT (protectrice)";
                case 0xEF: return "EFI (FAT)";
                default: return $"type 0x{type:X2}";
            }
        }

        private static string GptTypeName(Guid g)
        {
            string s = g.ToString().ToLowerInvariant();
            switch (s)
            {
                case "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7": return "Données (Basic Data)";
                case "c12a7328-f81f-11d2-ba4b-00a0c93ec93b": return "Système EFI";
                case "e3c9e316-0b5c-4db8-817d-f92df00215ae": return "Réservée Microsoft (MSR)";
                case "de94bba4-06d1-4d40-a16a-bfd50179d6ac": return "Récupération Windows";
                case "0fc63daf-8483-4772-8e79-3d69d8477de4": return "Linux";
                case "0657fd6d-a4ab-43c4-84e5-0933c84b4f4f": return "Linux swap";
                case "48465300-0000-11aa-aa11-00306543ecac": return "Apple HFS+";
                case "7c3457ef-0000-11aa-aa11-00306543ecac": return "Apple APFS";
                default: return "GPT " + s.Substring(0, 8);
            }
        }
    }
}
