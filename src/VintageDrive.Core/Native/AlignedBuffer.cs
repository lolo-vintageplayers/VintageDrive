using System;
using System.Runtime.InteropServices;

namespace VintageDrive.Core.Native
{
    /// <summary>
    /// Tampon natif aligné (4096 octets) requis par FILE_FLAG_NO_BUFFERING :
    /// Windows exige des adresses et des tailles alignées sur le secteur pour l'E/S sans cache.
    /// </summary>
    internal sealed class AlignedBuffer : IDisposable
    {
        private IntPtr _raw;
        internal IntPtr Pointer { get; }
        internal int Size { get; }

        internal AlignedBuffer(int size, int alignment = 4096)
        {
            Size = size;
            _raw = Marshal.AllocHGlobal(size + alignment);
            long p = _raw.ToInt64();
            Pointer = new IntPtr((p + alignment - 1) & ~(long)(alignment - 1));
        }

        internal void CopyIn(byte[] source, int count) => Marshal.Copy(source, 0, Pointer, count);
        internal void CopyOut(byte[] destination, int count) => Marshal.Copy(Pointer, destination, 0, count);

        public void Dispose()
        {
            if (_raw != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_raw);
                _raw = IntPtr.Zero;
            }
        }
    }
}
