using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VintageDrive.Core.Native
{
    /// <summary>P/Invoke bruts vers kernel32 (accès disque bas niveau).</summary>
    internal static class NativeMethods
    {
        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 0x00000001;
        internal const uint FILE_SHARE_WRITE = 0x00000002;
        internal const uint OPEN_EXISTING = 3;

        // E/S directe sans cache Windows : indispensable pour un test de capacité honnête
        // (sinon on relit le cache RAM et tout « vérifie » même sur un support falsifié)
        internal const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        internal const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

        internal const int ERROR_FILE_NOT_FOUND = 2;
        internal const int ERROR_PATH_NOT_FOUND = 3;
        internal const int ERROR_ACCESS_DENIED = 5;

        // winioctl.h — codes CTL_CODE précalculés
        internal const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        internal const uint IOCTL_DISK_GET_PARTITION_INFO_EX = 0x00070048;
        internal const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
        internal const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        internal const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
        internal const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140; // force Windows à relire la table de partitions
        internal const uint FSCTL_LOCK_VOLUME = 0x00090018;
        internal const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        internal const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            byte[]? lpInBuffer,
            int nInBufferSize,
            byte[]? lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteFile(SafeFileHandle hFile, IntPtr lpBuffer, int nBytes, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadFile(SafeFileHandle hFile, IntPtr lpBuffer, int nBytes, out int read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFilePointerEx(SafeFileHandle hFile, long distance, out long newPosition, uint moveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumeNameForVolumeMountPointW(
            string lpszVolumeMountPoint, System.Text.StringBuilder lpszVolumeName, int cchBufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVolumeInformationW(
            string lpRootPathName, System.Text.StringBuilder lpVolumeNameBuffer, int nVolumeNameSize,
            out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags,
            System.Text.StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetDiskFreeSpaceW(
            string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);
    }

    /// <summary>Petits utilitaires au-dessus de CreateFile / DeviceIoControl.</summary>
    internal static class DeviceIo
    {
        /// <summary>
        /// Ouvre un périphérique (\\.\PhysicalDriveN ou \\.\X:).
        /// Accès 0 = requêtes de métadonnées seulement, pas besoin de droits admin.
        /// </summary>
        internal static SafeFileHandle? TryOpen(string devicePath, uint access, out int lastError)
            => TryOpen(devicePath, access, 0, out lastError);

        internal static SafeFileHandle? TryOpen(string devicePath, uint access, uint flags, out int lastError)
        {
            var handle = NativeMethods.CreateFileW(
                devicePath, access,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeMethods.OPEN_EXISTING, flags, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                lastError = Marshal.GetLastWin32Error();
                handle.Dispose();
                return null;
            }
            lastError = 0;
            return handle;
        }

        /// <summary>DeviceIoControl avec buffer de sortie ; renvoie null si l'IOCTL échoue.</summary>
        internal static byte[]? Query(SafeFileHandle handle, uint ioctl, byte[]? input, int outputSize)
        {
            var output = new byte[outputSize];
            bool ok = NativeMethods.DeviceIoControl(
                handle, ioctl,
                input, input?.Length ?? 0,
                output, output.Length,
                out _, IntPtr.Zero);
            return ok ? output : null;
        }

        /// <summary>IOCTL sans entrée ni sortie (verrous, démontage…).</summary>
        internal static bool Control(SafeFileHandle handle, uint ioctl)
            => NativeMethods.DeviceIoControl(handle, ioctl, null, 0, null, 0, out _, IntPtr.Zero);

        /// <summary>Écrit <paramref name="count"/> octets de <paramref name="data"/> à un offset absolu du périphérique.</summary>
        internal static void WriteAt(SafeFileHandle handle, long offset, AlignedBuffer io, byte[] data, int count)
        {
            io.CopyIn(data, count);
            if (!NativeMethods.SetFilePointerEx(handle, offset, out _, 0))
                throw Win32Failure("positionnement", offset);
            if (!NativeMethods.WriteFile(handle, io.Pointer, count, out int written, IntPtr.Zero) || written != count)
                throw Win32Failure("écriture", offset);
        }

        /// <summary>Lit <paramref name="count"/> octets à un offset absolu du périphérique dans <paramref name="data"/>.</summary>
        internal static void ReadAt(SafeFileHandle handle, long offset, AlignedBuffer io, byte[] data, int count)
        {
            if (!NativeMethods.SetFilePointerEx(handle, offset, out _, 0))
                throw Win32Failure("positionnement", offset);
            if (!NativeMethods.ReadFile(handle, io.Pointer, count, out int read, IntPtr.Zero) || read != count)
                throw Win32Failure("lecture", offset);
            io.CopyOut(data, count);
        }

        private static IOException Win32Failure(string operation, long offset)
            => new IOException($"Échec {operation} à l'offset {offset} (code Win32 {Marshal.GetLastWin32Error()})");

        /// <summary>Lit une chaîne ANSI terminée par 0 à un offset d'un buffer (STORAGE_DEVICE_DESCRIPTOR).</summary>
        internal static string ReadAnsiString(byte[] buffer, int offset)
        {
            if (offset <= 0 || offset >= buffer.Length) return string.Empty;
            int end = offset;
            while (end < buffer.Length && buffer[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
        }
    }
}
