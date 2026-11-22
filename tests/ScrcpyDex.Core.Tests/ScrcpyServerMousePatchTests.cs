using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using ScrcpyDex.Services;
using Xunit;

namespace ScrcpyDex.Core.Tests
{
    public class ScrcpyServerMousePatchTests
    {
        private static readonly byte[] Pattern =
        {
            0x31, 0x10, 0x02, 0x0D,
            0x39, 0x10, 0x1B, 0x00,
            0x12, 0x72,
            0x32, 0x21, 0x04, 0x00,
            0x38, 0x09, 0x16, 0x00,
            0x54, 0x02, 0x3A, 0x01
        };

        [Fact]
        public void TryPatchDex_RewritesMouseBranchAndUpdatesDexIntegrity()
        {
            byte[] dex = new byte[160];
            "dex\n035\0"u8.CopyTo(dex);
            Pattern.CopyTo(dex, 64);

            bool patched = ScrcpyServerMousePatch.TryPatchDex(dex, out byte[] result);

            Assert.True(patched);
            Assert.Equal(0x28, result[72]);
            Assert.Equal(0x05, result[73]);
            Assert.All(result[74..82], value => Assert.Equal(0, value));

            byte[] expectedSignature = SHA1.HashData(result.AsSpan(32));
            Assert.Equal(expectedSignature, result[12..32]);

            uint expectedChecksum = ScrcpyServerMousePatch.ComputeAdler32(result.AsSpan(12));
            uint actualChecksum = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(8, 4));
            Assert.Equal(expectedChecksum, actualChecksum);
        }

        [Fact]
        public void TryPatchDex_RejectsUnknownLayout()
        {
            byte[] dex = new byte[128];
            "dex\n035\0"u8.CopyTo(dex);

            Assert.False(ScrcpyServerMousePatch.TryPatchDex(dex, out _));
        }

        [Fact]
        public void TryPatchDex_RejectsAmbiguousLayout()
        {
            byte[] dex = new byte[192];
            "dex\n035\0"u8.CopyTo(dex);
            Pattern.CopyTo(dex, 48);
            Pattern.CopyTo(dex, 112);

            Assert.False(ScrcpyServerMousePatch.TryPatchDex(dex, out _));
        }

        [Fact]
        public void TryCreatePatchedServer_RewritesClassesDexInsideArchive()
        {
            string sourcePath = Path.Combine(Path.GetTempPath(), $"scrcpy-server-test-{Guid.NewGuid():N}");
            string? patchedPath = null;
            try
            {
                byte[] dex = new byte[160];
                "dex\n035\0"u8.CopyTo(dex);
                Pattern.CopyTo(dex, 64);

                using (FileStream stream = File.Create(sourcePath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry dexEntry = archive.CreateEntry("classes.dex");
                    using Stream dexStream = dexEntry.Open();
                    dexStream.Write(dex);
                }

                bool patched = ScrcpyServerMousePatch.TryCreatePatchedServer(sourcePath, out patchedPath!, out string? error);

                Assert.True(patched, error);
                Assert.True(File.Exists(patchedPath));
                using var patchedArchive = ZipFile.OpenRead(patchedPath);
                using Stream patchedDexStream = patchedArchive.GetEntry("classes.dex")!.Open();
                using var buffer = new MemoryStream();
                patchedDexStream.CopyTo(buffer);
                byte[] patchedDex = buffer.ToArray();
                Assert.Equal(0x28, patchedDex[72]);
                Assert.Equal(0x05, patchedDex[73]);
            }
            finally
            {
                File.Delete(sourcePath);
                if (!string.IsNullOrWhiteSpace(patchedPath))
                {
                    File.Delete(patchedPath);
                }
            }
        }
    }
}
