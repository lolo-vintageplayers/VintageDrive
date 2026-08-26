using System;
using System.Collections.Generic;
using System.IO;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Disks
{
    /// <summary>
    /// Inventaire des disques physiques et de leurs volumes.
    /// Fonctionne sans droits admin (ouvertures en accès 0 : métadonnées seulement).
    /// </summary>
    public static class DiskEnumerator
    {
        private const int MaxDiskIndex = 64;

        public static IReadOnlyList<PhysicalDisk> GetDisks()
        {
            var disks = new List<PhysicalDisk>();
            for (int i = 0; i < MaxDiskIndex; i++)
            {
                var disk = TryReadDisk(i);
                if (disk != null) disks.Add(disk);
                // pas de break : les indices peuvent avoir des trous (USB débranché, etc.)
            }
            AttachVolumes(disks);
            MarkSystemDisks(disks);
            return disks;
        }

        private static PhysicalDisk? TryReadDisk(int index)
        {
            using var handle = DeviceIo.TryOpen($@"\\.\PhysicalDrive{index}", 0, out _);
            if (handle == null) return null;

            var disk = new PhysicalDisk { Index = index };

            // Taille exacte + taille de secteur : DISK_GEOMETRY_EX (BytesPerSector @20, DiskSize @24)
            var geo = DeviceIo.Query(handle, NativeMethods.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, null, 0x200);
            if (geo != null)
            {
                disk.MediaType = BitConverter.ToInt32(geo, 8);
                disk.BytesPerSector = BitConverter.ToInt32(geo, 20);
                disk.SizeBytes = BitConverter.ToInt64(geo, 24);
            }

            // Style de table de partitions : PARTITION_INFORMATION_EX.PartitionStyle @0
            var part = DeviceIo.Query(handle, NativeMethods.IOCTL_DISK_GET_PARTITION_INFO_EX, null, 0x100);
            disk.PartitionStyle = part != null ? (PartStyle)BitConverter.ToInt32(part, 0) : PartStyle.Raw;

            // Modèle / n° de série / bus / amovible : STORAGE_DEVICE_DESCRIPTOR
            // STORAGE_PROPERTY_QUERY { PropertyId=0 (StorageDeviceProperty), QueryType=0 } = 12 octets à zéro
            var query = new byte[12];
            var desc = DeviceIo.Query(handle, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY, query, 0x400);
            if (desc != null)
            {
                disk.IsRemovableMedia = desc[10] != 0;
                string vendor = DeviceIo.ReadAnsiString(desc, BitConverter.ToInt32(desc, 12));
                string product = DeviceIo.ReadAnsiString(desc, BitConverter.ToInt32(desc, 16));
                disk.SerialNumber = DeviceIo.ReadAnsiString(desc, BitConverter.ToInt32(desc, 24));
                disk.FirmwareRevision = DeviceIo.ReadAnsiString(desc, BitConverter.ToInt32(desc, 20));
                disk.Model = string.IsNullOrEmpty(vendor) ? product : (vendor + " " + product).Trim();
                disk.Bus = (StorageBus)BitConverter.ToInt32(desc, 28);
            }

            return disk;
        }

        private static void AttachVolumes(List<PhysicalDisk> disks)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    continue;

                string letter = drive.Name.TrimEnd('\\');   // "E:"
                int diskNumber = GetDiskNumberForVolume(letter);
                if (diskNumber < 0) continue;

                var vol = new VolumeInfo { Letter = letter };
                try
                {
                    if (drive.IsReady)
                    {
                        vol.IsReady = true;
                        vol.FileSystem = drive.DriveFormat;
                        vol.Label = drive.VolumeLabel;
                        vol.TotalBytes = drive.TotalSize;
                        vol.FreeBytes = drive.TotalFreeSpace;
                    }
                }
                catch (IOException) { }                 // volume RAW ou éjecté entre-temps
                catch (UnauthorizedAccessException) { }

                disks.Find(d => d.Index == diskNumber)?.Volumes.Add(vol);
            }
        }

        /// <summary>N° de disque physique hébergeant un chemin quelconque. -1 si indéterminé.</summary>
        public static int GetDiskNumberForPath(string path)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root!.Length < 2 || root[1] != ':') return -1;
                return GetDiskNumberForVolume(root.Substring(0, 2));
            }
            catch (ArgumentException) { return -1; }
        }

        /// <summary>VOLUME_DISK_EXTENTS : DiskNumber du premier extent (@8). -1 si introuvable.</summary>
        private static int GetDiskNumberForVolume(string letter)
        {
            using var handle = DeviceIo.TryOpen($@"\\.\{letter}", 0, out _);
            if (handle == null) return -1;

            var buf = DeviceIo.Query(handle, NativeMethods.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0x400);
            if (buf == null || BitConverter.ToInt32(buf, 0) < 1) return -1;
            return BitConverter.ToInt32(buf, 8);
        }

        private static void MarkSystemDisks(List<PhysicalDisk> disks)
        {
            string? sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(sysRoot)) return;

            int sysDisk = GetDiskNumberForVolume(sysRoot!.TrimEnd('\\'));
            foreach (var d in disks)
                if (d.Index == sysDisk) d.IsSystemDisk = true;
        }
    }
}
