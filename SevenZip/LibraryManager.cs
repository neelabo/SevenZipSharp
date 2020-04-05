/*  This file is part of SevenZipSharp.

    SevenZipSharp is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    SevenZipSharp is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with SevenZipSharp.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
#if !WINCE && !MONO
using System.Configuration;
using System.Diagnostics;
using System.Security.Permissions;
#endif
#if WINCE
using OpenNETCF.Diagnostics;
#endif
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
#if MONO
using SevenZip.Mono.COM;
#endif

namespace SevenZip
{
#if UNMANAGED
    /// <summary>
    /// 7-zip library low-level wrapper.
    /// </summary>
    internal static class SevenZipLibraryManager
    {
        /// <summary>
        /// Synchronization root for all locking.
        /// </summary>
        private static readonly object _syncRoot = new object();

#if !WINCE && !MONO
        /// <summary>
        /// Path to the 7-zip dll.
        /// </summary>
        /// <remarks>7zxa.dll supports only decoding from .7z archives.
        /// Features of 7za.dll: 
        ///     - Supporting 7z format;
        ///     - Built encoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption.
        ///     - Built decoders: LZMA, PPMD, BCJ, BCJ2, COPY, AES-256 Encryption, BZip2, Deflate.
        /// 7z.dll (from the 7-zip distribution) supports every InArchiveFormat for encoding and decoding.
        /// </remarks>
        private static string _libraryFileName =
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "7z.dll");
#endif
#if WINCE 		
        private static string _libraryFileName =
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetName().CodeBase), "7z.dll");
#endif
        /// <summary>
        /// 7-zip library handle.
        /// </summary>
        private static IntPtr _modulePtr;

        private static Dictionary<object, Dictionary<InArchiveFormat, IInArchive>> _inArchives;
#if COMPRESS
        private static Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>> _outArchives;
#endif
        private static int _totalUsers;

        // private static string _LibraryVersion;
        private static bool? _modifyCapabale;

        private static void InitUserInFormat(object user, InArchiveFormat format)
        {
            if (!_inArchives.ContainsKey(user))
            {
                _inArchives.Add(user, new Dictionary<InArchiveFormat, IInArchive>());
            }
            if (!_inArchives[user].ContainsKey(format))
            {
                _inArchives[user].Add(format, null);
                _totalUsers++;
            }
        }

#if COMPRESS
        private static void InitUserOutFormat(object user, OutArchiveFormat format)
        {
            if (!_outArchives.ContainsKey(user))
            {
                _outArchives.Add(user, new Dictionary<OutArchiveFormat, IOutArchive>());
            }
            if (!_outArchives[user].ContainsKey(format))
            {
                _outArchives[user].Add(format, null);
                _totalUsers++;
            }
        }
#endif

        private static void Init()
        {
            _inArchives = new Dictionary<object, Dictionary<InArchiveFormat, IInArchive>>();
#if COMPRESS
            _outArchives = new Dictionary<object, Dictionary<OutArchiveFormat, IOutArchive>>();
#endif
        }

        /// <summary>
        /// Loads the 7-zip library if necessary and adds user to the reference list
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void LoadLibrary(object user, Enum format)
        {
            lock (_syncRoot)
            {
                if (_inArchives == null
#if COMPRESS
                    || _outArchives == null
#endif
                    )
                {
                    Init();
                }
#if !WINCE && !MONO
                if (_modulePtr == IntPtr.Zero)
                {
                    if (!File.Exists(_libraryFileName))
                    {
                        throw new SevenZipLibraryException("DLL file does not exist.");
                    }
                    if ((_modulePtr = NativeMethods.LoadLibrary(_libraryFileName)) == IntPtr.Zero)
                    {
                        throw new SevenZipLibraryException("failed to load library.");
                    }
                    if (NativeMethods.GetProcAddress(_modulePtr, "GetHandlerProperty") == IntPtr.Zero)
                    {
                        NativeMethods.FreeLibrary(_modulePtr);
                        throw new SevenZipLibraryException("library is invalid.");
                    }
                }
#endif
                if (format is InArchiveFormat)
                {
                    InitUserInFormat(user, (InArchiveFormat)format);
                    return;
                }
#if COMPRESS
                if (format is OutArchiveFormat)
                {
                    InitUserOutFormat(user, (OutArchiveFormat)format);
                    return;
                }
#endif
                throw new ArgumentException(
                    "Enum " + format + " is not a valid archive format attribute!");
            }
        }

        /*/// <summary>
        /// Gets the native 7zip library version string.
        /// </summary>
        public static string LibraryVersion
        {
            get
            {
                if (String.IsNullOrEmpty(_LibraryVersion))
                {
                    FileVersionInfo dllVersionInfo = FileVersionInfo.GetVersionInfo(_libraryFileName);
                    _LibraryVersion = String.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        "{0}.{1}",
                        dllVersionInfo.FileMajorPart, dllVersionInfo.FileMinorPart);
                }
                return _LibraryVersion;
            }
        }*/

        /// <summary>
        /// Gets the value indicating whether the library supports modifying archives.
        /// </summary>
        public static bool ModifyCapable
        {
            get
            {
                lock (_syncRoot)
                {
                    if (!_modifyCapabale.HasValue)
                    {
#if !WINCE && !MONO
                        FileVersionInfo dllVersionInfo = FileVersionInfo.GetVersionInfo(_libraryFileName);
                        _modifyCapabale = dllVersionInfo.FileMajorPart >= 9;
#else
                    _modifyCapabale = true;
#endif
                    }
                    return _modifyCapabale.Value;
                }
            }
        }

        /// <summary>
        /// Removes user from reference list and frees the 7-zip library if it becomes empty
        /// </summary>
        /// <param name="user">Caller of the function</param>
        /// <param name="format">Archive format</param>
        public static void FreeLibrary(object user, Enum format)
        {
#if !WINCE && !MONO
            var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
            sp.Demand();
#endif
            lock (_syncRoot)
			{
                if (_modulePtr != IntPtr.Zero)
            {
                if (format is InArchiveFormat)
                {
                    if (_inArchives != null && _inArchives.ContainsKey(user) &&
                        _inArchives[user].ContainsKey((InArchiveFormat) format) &&
                        _inArchives[user][(InArchiveFormat) format] != null)
                    {
                        try
                        {                            
                            Marshal.ReleaseComObject(_inArchives[user][(InArchiveFormat) format]);
                        }
                        catch (InvalidComObjectException) {}
                        _inArchives[user].Remove((InArchiveFormat) format);
                        _totalUsers--;
                        if (_inArchives[user].Count == 0)
                        {
                            _inArchives.Remove(user);
                        }
                    }
                }
#if COMPRESS
                if (format is OutArchiveFormat)
                {
                    if (_outArchives != null && _outArchives.ContainsKey(user) &&
                        _outArchives[user].ContainsKey((OutArchiveFormat) format) &&
                        _outArchives[user][(OutArchiveFormat) format] != null)
                    {
                        try
                        {
                            Marshal.ReleaseComObject(_outArchives[user][(OutArchiveFormat) format]);
                        }
                        catch (InvalidComObjectException) {}
                        _outArchives[user].Remove((OutArchiveFormat) format);
                        _totalUsers--;
                        if (_outArchives[user].Count == 0)
                        {
                            _outArchives.Remove(user);
                        }
                    }
                }
#endif
                if ((_inArchives == null || _inArchives.Count == 0)
#if COMPRESS
                    && (_outArchives == null || _outArchives.Count == 0)
#endif
                    )
                {
                    _inArchives = null;
#if COMPRESS
                    _outArchives = null;
#endif
                    if (_totalUsers == 0)
                    {
#if !WINCE && !MONO
                        NativeMethods.FreeLibrary(_modulePtr);

#endif
                        _modulePtr = IntPtr.Zero;
                    }
                }
            }
			}
        }

        /// <summary>
        /// Gets IInArchive interface to extract 7-zip archives.
        /// </summary>
        /// <param name="format">Archive format.</param>
        /// <param name="user">Archive format user.</param>
        public static IInArchive InArchive(InArchiveFormat format, object user)
        {
            lock (_syncRoot)
            {
                if (_inArchives[user][format] == null)
                {
#if !WINCE && !MONO
                    var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                    sp.Demand();

                    if (_modulePtr == IntPtr.Zero)
                    {
                        LoadLibrary(user, format);
                        if (_modulePtr == IntPtr.Zero)
                        {
                            throw new SevenZipLibraryException();
                        }
                    }
                    var createObject = (NativeMethods.CreateObjectDelegate)
                        Marshal.GetDelegateForFunctionPointer(
                            NativeMethods.GetProcAddress(_modulePtr, "CreateObject"),
                            typeof(NativeMethods.CreateObjectDelegate));
                    if (createObject == null)
                    {
                        throw new SevenZipLibraryException();
                    }
#endif
                    object result;
                    Guid interfaceId =
#if !WINCE && !MONO
 typeof(IInArchive).GUID;
#else
                new Guid(((GuidAttribute)typeof(IInArchive).GetCustomAttributes(typeof(GuidAttribute), false)[0]).Value);
#endif
                    Guid classID = Formats.InFormatGuids[format];
                    try
                    {
#if !WINCE && !MONO
                        createObject(ref classID, ref interfaceId, out result);
#elif !MONO
                    	NativeMethods.CreateCOMObject(ref classID, ref interfaceId, out result);
#else
						result = SevenZip.Mono.Factory.CreateInterface<IInArchive>(user, classID, interfaceId);
#endif
                    }
                    catch (Exception)
                    {
                        throw new SevenZipLibraryException("Your 7-zip library does not support this archive type.");
                    }
                    InitUserInFormat(user, format);									
                    _inArchives[user][format] = result as IInArchive;
                }
                return _inArchives[user][format];
#if !WINCE && !MONO
            }
#endif
        }

#if COMPRESS
        /// <summary>
        /// Gets IOutArchive interface to pack 7-zip archives.
        /// </summary>
        /// <param name="format">Archive format.</param>  
        /// <param name="user">Archive format user.</param>
        public static IOutArchive OutArchive(OutArchiveFormat format, object user)
        {
            lock (_syncRoot)
            {
                if (_outArchives[user][format] == null)
                {
#if !WINCE && !MONO
                    var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                    sp.Demand();
                    if (_modulePtr == IntPtr.Zero)
                    {
                        throw new SevenZipLibraryException();
                    }
                    var createObject = (NativeMethods.CreateObjectDelegate)
                        Marshal.GetDelegateForFunctionPointer(
                            NativeMethods.GetProcAddress(_modulePtr, "CreateObject"),
                            typeof(NativeMethods.CreateObjectDelegate));
                    if (createObject == null)
                    {
                        throw new SevenZipLibraryException();
                    }
#endif
                    object result;
                    Guid interfaceId =
#if !WINCE && !MONO
 typeof(IOutArchive).GUID;
#else
                    new Guid(((GuidAttribute)typeof(IOutArchive).GetCustomAttributes(typeof(GuidAttribute), false)[0]).Value);
#endif
                    Guid classID = Formats.OutFormatGuids[format];
                    try
                    {
#if !WINCE && !MONO
                        createObject(ref classID, ref interfaceId, out result);
#elif !MONO
                    	NativeMethods.CreateCOMObject(ref classID, ref interfaceId, out result);
#else
						result = SevenZip.Mono.Factory.CreateInterface<IOutArchive>(classID, interfaceId, user);
#endif
                    }
                    catch (Exception)
                    {
                        throw new SevenZipLibraryException("Your 7-zip library does not support this archive type.");
                    }
                    InitUserOutFormat(user, format);
                    _outArchives[user][format] = result as IOutArchive;
                }
                return _outArchives[user][format];
#if !WINCE && !MONO
            }
#endif

        }
#endif
#if !WINCE && !MONO
        public static void SetLibraryPath(string libraryPath)
        {
            if (_modulePtr != IntPtr.Zero && !Path.GetFullPath(libraryPath).Equals( 
                Path.GetFullPath(_libraryFileName), StringComparison.OrdinalIgnoreCase))
            {
                throw new SevenZipLibraryException(
                    "can not change the library path while the library \"" + _libraryFileName + "\" is being used.");
            }
            if (!File.Exists(libraryPath))
            {
                throw new SevenZipLibraryException(
                    "can not change the library path because the file \"" + libraryPath + "\" does not exist.");
            }
            _libraryFileName = libraryPath;
        }
#endif
    }
#endif
}