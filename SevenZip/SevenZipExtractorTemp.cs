using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SevenZip
{
    public sealed partial class SevenZipExtractor
#if UNMANAGED
        : SevenZipBase, IDisposable
#endif
    {
        /// <summary>
        /// for temporary extractor.
        /// filename is all temp: ex. "000000.txt".
        /// not create folder.
        /// </summary>
        /// <param name="directory">output folder</param>
        public void ExtractArchiveTemp(string directory)
        {
            DisposedCheck();
            ClearExceptions();
            InitArchiveFileData(true);

            IInStream archiveStream;
            using ((archiveStream = GetArchiveStream(true)) as IDisposable)
            {
                var openCallback = GetArchiveOpenCallback();
                if (!OpenArchive(archiveStream, openCallback))
                {
                    return;
                }

                Extract(afi => afi.IsDirectory ? null : Path.Combine(directory, afi.GetTempFileName()));
            }
        }


        //
        private void Extract(Func<ArchiveFileInfo, string> getOutputPath)
        {
            IList<Stream> fileStreams = new List<Stream>();

            try
            {
                foreach (ArchiveFileInfo afi in _archiveFileData)
                {
                    string outputPath = getOutputPath(afi);

                    if (outputPath == null)
                    {
                        fileStreams.Add(null);
                    }
                    else if (afi.IsDirectory)
                    {
                        Directory.CreateDirectory(outputPath);
                        fileStreams.Add(null);
                    }
                    else
                    {
                        string directoryName = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrWhiteSpace(directoryName))
                        {
                            Directory.CreateDirectory(directoryName);
                        }
                        fileStreams.Add(File.Create(outputPath));
                    }
                }

                _archive.Extract(null, UInt32.MaxValue, 0, new ArchiveStreamCallback(fileStreams));
            }
            finally
            {
                foreach (Stream stream in fileStreams)
                {
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
                }
            }
        }


        //
        private class ArchiveStreamCallback : IArchiveExtractCallback
        {
            private readonly IList<Stream> _streams;

            public ArchiveStreamCallback(IList<Stream> streams)
            {
                _streams = streams;
            }

            public void SetTotal(ulong total)
            {
            }

            public void SetCompleted(ref ulong completeValue)
            {
            }

            public int GetStream(uint index, out ISequentialOutStream outStream, AskMode askExtractMode)
            {
                var stream = _streams?.ElementAt((int)index);

                if (askExtractMode != AskMode.Extract || stream == null)
                {
                    outStream = null;
                    return 0;
                }

                outStream = new OutStreamWrapper(stream, true);
                return 0;
            }

            public void PrepareOperation(AskMode askExtractMode)
            {
            }

            public void SetOperationResult(OperationResult operationResult)
            {
            }
        }
    }
}
