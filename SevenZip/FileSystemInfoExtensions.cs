using System;
using System.IO;

namespace SevenZip
{
    internal static class FileSystemInfoExtensions
    {
        private static DateTime _default = DateTime.FromFileTime(0);

        internal static DateTime GetSafeLastAccessTime(this FileSystemInfo info)
        {
            try
            {
                return info.LastAccessTime;
            }
            catch
            {
                return _default;
            }
        }

        internal static DateTime GetSafeCreationTime(this FileSystemInfo info)
        {
            try
            {
                return info.CreationTime;
            }
            catch
            {
                return _default;
            }
        }

        internal static DateTime GetSafeLastWriteTime(this FileSystemInfo info)
        {
            try
            {
                return info.LastWriteTime;
            }
            catch
            {
                return _default;
            }
        }
    }
}
