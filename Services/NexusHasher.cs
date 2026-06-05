using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SubtitleNexus.Services
{
    
    
    
    
    
    
    
    
    public static class NexusHasher
    {
        private const int Chunk = 65536;

        private static (byte[] head, byte[] tail, long size) ReadEndpoints(string path)
        {
            var info = new FileInfo(path);
            long size = info.Length;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int headLen = (int)Math.Min(Chunk, size);
                byte[] head = new byte[headLen];
                ReadExact(fs, head, headLen);

                byte[] tail;
                if (size > Chunk)
                {
                    fs.Seek(Math.Max(size - Chunk, 0), SeekOrigin.Begin);
                    tail = new byte[Chunk];
                    ReadExact(fs, tail, Chunk);
                }
                else
                {
                    tail = new byte[0];
                }

                return (head, tail, size);
            }
        }

        private static void ReadExact(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) break;
                read += n;
            }
        }

        
        
        
        
        
        public static string OsHash(string path)
        {
            var (head, tail, size) = ReadEndpoints(path);
            ulong h = unchecked((ulong)size);

            foreach (var buf in new[] { head, tail })
            {
                
                
                
                int limit = buf.Length - 7;
                for (int i = 0; i < limit; i += 8)
                {
                    ulong word =
                        (ulong)buf[i] |
                        ((ulong)buf[i + 1] << 8) |
                        ((ulong)buf[i + 2] << 16) |
                        ((ulong)buf[i + 3] << 24) |
                        ((ulong)buf[i + 4] << 32) |
                        ((ulong)buf[i + 5] << 40) |
                        ((ulong)buf[i + 6] << 48) |
                        ((ulong)buf[i + 7] << 56);
                    h = unchecked(h + word);
                }
            }

            return h.ToString("x16");
        }

        
        
        
        public static string Sha256Endpoints(string path)
        {
            var (head, tail, size) = ReadEndpoints(path);
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(head, 0, head.Length, null, 0);
                if (tail.Length > 0)
                {
                    sha.TransformBlock(tail, 0, tail.Length, null, 0);
                }
                byte[] sizeBytes = Encoding.UTF8.GetBytes(size.ToString());
                sha.TransformFinalBlock(sizeBytes, 0, sizeBytes.Length);

                var sb = new StringBuilder(sha.Hash.Length * 2);
                foreach (byte b in sha.Hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
