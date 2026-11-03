using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace Nexus.Core
{
    public static class GraphicsTransaction
    {
        public class Write
        {
            public string Path;
            public byte[] Before, After;
        }
        // Locks every destination before writing any. A failure restores every attempted write.
        // Persistent backups are also retained for interruption/power-loss recovery.
        public static void Commit(IList<Write> writes, Action<int> afterWrite = null)
        {
            var streams = new List<FileStream>();
            var created = new List<string>();
            int attempted = 0;
            try
            {
                foreach (var write in writes)
                {
                    if (write.After == null) throw new IOException("A graphics write cannot have empty replacement data.");
                    var stream = new FileStream(write.Path, write.Before == null ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    streams.Add(stream);
                    if (write.Before == null) created.Add(write.Path);
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        if (!copy.ToArray().SequenceEqual(write.Before ?? new byte[0]))
                            throw new IOException("Graphics files changed while Settings was open. Reopen Settings and try again.");
                    }
                }
                for (int index = 0; index < writes.Count; index++)
                {
                    attempted = index + 1;
                    ReplaceContents(streams[index], writes[index].After);
                    afterWrite?.Invoke(index);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                for (int index = attempted - 1; index >= 0; index--)
                    try { if (writes[index].Before != null) ReplaceContents(streams[index], writes[index].Before); }
                    catch (Exception rollback) { errors.Add(rollback); }
                foreach (var stream in streams) stream.Dispose();
                streams.Clear();
                foreach (string path in created) try { File.Delete(path); } catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("Graphics recovery needs attention. Restore the files from NexusGraphicsBackups before starting AO.", errors);
                throw;
            }
            finally { foreach (var stream in streams) stream.Dispose(); }
        }
        private static void ReplaceContents(FileStream stream, byte[] bytes)
        {
            stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.SetLength(bytes.Length); stream.Flush(true);
            stream.Position = 0;
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                if (!copy.ToArray().SequenceEqual(bytes)) throw new IOException("Graphics file verification failed.");
            }
        }
    }
}
