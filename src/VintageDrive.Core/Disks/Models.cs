using System.Collections.Generic;

namespace VintageDrive.Core.Disks
{
    /// <summary>STORAGE_BUS_TYPE (winioctl.h).</summary>
    public enum StorageBus
    {
        Unknown = 0, Scsi = 1, Atapi = 2, Ata = 3, Ieee1394 = 4, Ssa = 5,
        Fibre = 6, Usb = 7, Raid = 8, IScsi = 9, Sas = 10, Sata = 11,
        Sd = 12, Mmc = 13, Virtual = 14, FileBackedVirtual = 15,
        Spaces = 16, Nvme = 17, Scm = 18, Ufs = 19,
    }

    /// <summary>PARTITION_STYLE (winioctl.h).</summary>
    public enum PartStyle { Mbr = 0, Gpt = 1, Raw = 2 }

    /// <summary>Un volume monté (lettre) rattaché à un disque physique.</summary>
    public sealed class VolumeInfo
    {
        public string Letter { get; internal set; } = "";        // "E:"
        public string FileSystem { get; internal set; } = "";    // "FAT32", "NTFS"… vide si non formaté
        public string Label { get; internal set; } = "";
        public long TotalBytes { get; internal set; }
        public long FreeBytes { get; internal set; }
        public bool IsReady { get; internal set; }               // false = RAW / système de fichiers illisible
    }

    /// <summary>Un disque physique (\\.\PhysicalDriveN) et ses volumes.</summary>
    public sealed class PhysicalDisk
    {
        public int Index { get; internal set; }
        public string DevicePath => $@"\\.\PhysicalDrive{Index}";
        public string Model { get; internal set; } = "";
        public string SerialNumber { get; internal set; } = "";
        public string FirmwareRevision { get; internal set; } = "";
        public StorageBus Bus { get; internal set; }
        public bool IsRemovableMedia { get; internal set; }
        public long SizeBytes { get; internal set; }
        public int BytesPerSector { get; internal set; }
        public int MediaType { get; internal set; }   // MEDIA_TYPE : 11 = amovible, 12 = fixe
        public PartStyle PartitionStyle { get; internal set; }
        public bool IsSystemDisk { get; internal set; }
        public List<VolumeInfo> Volumes { get; } = new List<VolumeInfo>();

        /// <summary>Garde-fou : jamais de test destructif ni de formatage sur le disque système.</summary>
        public bool IsSafeTarget => !IsSystemDisk;
    }
}
