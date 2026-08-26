using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using VintageDrive.Core.Native;

namespace VintageDrive.Core.Disks
{
    /// <summary>
    /// Verrouille et démonte tous les volumes d'un disque physique le temps d'une opération
    /// destructive (test de capacité, formatage) : Windows bloque sinon les écritures brutes
    /// sur les zones couvertes par un volume monté. Dispose() déverrouille, Windows remonte seul.
    /// </summary>
    public sealed class VolumeLocker : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = new List<SafeFileHandle>();

        public static VolumeLocker LockDisk(PhysicalDisk disk)
        {
            var locker = new VolumeLocker();
            try
            {
                foreach (var vol in disk.Volumes)
                {
                    var h = DeviceIo.TryOpen($@"\\.\{vol.Letter}",
                        NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, 0, out int err);
                    if (h == null)
                        throw new IOException($"Impossible d'ouvrir le volume {vol.Letter} (code Win32 {err}). Droits admin requis.");
                    locker._handles.Add(h);

                    bool locked = false;
                    for (int attempt = 0; attempt < 10 && !locked; attempt++)
                    {
                        locked = DeviceIo.Control(h, NativeMethods.FSCTL_LOCK_VOLUME);
                        if (!locked) Thread.Sleep(300);
                    }
                    if (!locked)
                    {
                        // dernier recours : démontage forcé puis nouvel essai de verrou
                        DeviceIo.Control(h, NativeMethods.FSCTL_DISMOUNT_VOLUME);
                        locked = DeviceIo.Control(h, NativeMethods.FSCTL_LOCK_VOLUME);
                    }
                    if (!locked)
                        throw new IOException($"Volume {vol.Letter} occupé (fenêtre d'explorateur ouverte ? programme qui l'utilise ?). Ferme tout ce qui touche {vol.Letter} et relance.");

                    DeviceIo.Control(h, NativeMethods.FSCTL_DISMOUNT_VOLUME);
                }
                return locker;
            }
            catch
            {
                locker.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var h in _handles)
            {
                DeviceIo.Control(h, NativeMethods.FSCTL_UNLOCK_VOLUME);
                h.Dispose();
            }
            _handles.Clear();
        }
    }
}
