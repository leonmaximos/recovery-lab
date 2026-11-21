using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RecoveryLab
{
    internal static class RawDiskReader
    {

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("kernel32.dll",
            EntryPoint = "CreateFileW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAcess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        public static byte[] ReadBytes(long offset, int count) => ReadBytes('E', offset, count);

        public static byte[] ReadBytes(char volumeLetter, long offset, int count)
        {
            if (!char.IsAsciiLetter(volumeLetter))
                throw new ArgumentException("A letra do volume é inválida.", nameof(volumeLetter));

            using SafeFileHandle Handle = CreateFile(
                $@"\\.\{char.ToUpperInvariant(volumeLetter)}:",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero
            );
            
            if (Handle.IsInvalid)
            {

                throw new Win32Exception(
                    Marshal.GetLastWin32Error()
                );

            }

            using (FileStream stream = new FileStream(Handle, FileAccess.Read))
            {

                stream.Position = offset;

                byte[] buffer = new byte[count];

                stream.ReadExactly(buffer, 0, buffer.Length);

                return buffer;

            } 
            
        }

        public static byte[] ReadBootSector()
        {

            return ReadBytes(0, 512);

        }

    }
}
