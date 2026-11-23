// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ScrcpyDex.Core.Platform;

namespace ScrcpyDex.Services
{
    internal static class ScrcpyServerMousePatch
    {
        private static readonly byte[] MouseClassificationPattern =
        {
            0x31, 0x10, 0x02, 0x0D,
            0x39, 0x10, 0x1B, 0x00,
            0x12, 0x72,
            0x32, 0x21, 0x04, 0x00,
            0x38, 0x09, 0x16, 0x00,
            0x54, 0x02, 0x3A, 0x01
        };

        private static readonly byte[] MouseClassificationReplacement =
        {
            0x31, 0x10, 0x02, 0x0D,
            0x39, 0x10, 0x1B, 0x00,
            0x28, 0x05,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x54, 0x02, 0x3A, 0x01
        };

        public static bool TryCreatePatchedServer(string sourceServerPath, out string patchedServerPath, out string? error)
        {
            patchedServerPath = string.Empty;
            error = null;

            try
            {
                if (!File.Exists(sourceServerPath))
                {
                    error = $"scrcpy-server not found at '{sourceServerPath}'.";
                    return false;
                }

                byte[] sourceBytes = File.ReadAllBytes(sourceServerPath);
                string sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)).Substring(0, 16);
                string cacheDirectory = Path.Combine(
                    DefaultPlatformPaths.Instance.GetCacheDirectory(),
                    "OnigiriDeX");
                Directory.CreateDirectory(cacheDirectory);
                patchedServerPath = Path.Combine(cacheDirectory, $"scrcpy-server-native-mouse-v1-{sourceHash}");

                if (File.Exists(patchedServerPath))
                {
                    return true;
                }

                string temporaryPath = patchedServerPath + ".tmp";
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                using var sourceStream = new MemoryStream(sourceBytes, writable: false);
                using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);

                ZipArchiveEntry? dexEntry = sourceArchive.GetEntry("classes.dex");
                if (dexEntry == null)
                {
                    error = "scrcpy-server does not contain classes.dex.";
                    patchedServerPath = string.Empty;
                    return false;
                }

                byte[] originalDex;
                using (Stream dexStream = dexEntry.Open())
                using (var dexBuffer = new MemoryStream())
                {
                    dexStream.CopyTo(dexBuffer);
                    originalDex = dexBuffer.ToArray();
                }

                if (!TryPatchDex(originalDex, out byte[] patchedDex))
                {
                    error = "scrcpy-server mouse injection layout is not recognized. The native mouse patch was not applied.";
                    patchedServerPath = string.Empty;
                    return false;
                }

                using (var outputStream = File.Create(temporaryPath))
                using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (ZipArchiveEntry entry in sourceArchive.Entries)
                    {
                        ZipArchiveEntry outputEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        outputEntry.LastWriteTime = entry.LastWriteTime;
                        outputEntry.ExternalAttributes = entry.ExternalAttributes;

                        using Stream destination = outputEntry.Open();
                        if (entry.FullName == "classes.dex")
                        {
                            destination.Write(patchedDex);
                        }
                        else
                        {
                            using Stream input = entry.Open();
                            input.CopyTo(destination);
                        }
                    }
                }

                File.Move(temporaryPath, patchedServerPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                patchedServerPath = string.Empty;
                return false;
            }
        }

        internal static bool TryPatchDex(byte[] dex, out byte[] patchedDex)
        {
            patchedDex = Array.Empty<byte>();
            if (dex.Length < 32)
            {
                return false;
            }

            int matchOffset = FindUniquePattern(dex, MouseClassificationPattern);
            if (matchOffset < 0)
            {
                return false;
            }

            patchedDex = (byte[])dex.Clone();
            Buffer.BlockCopy(MouseClassificationReplacement, 0, patchedDex, matchOffset, MouseClassificationReplacement.Length);
            UpdateDexIntegrity(patchedDex);
            return true;
        }

        private static int FindUniquePattern(byte[] data, byte[] pattern)
        {
            int found = -1;
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                if (!data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                {
                    continue;
                }

                if (found >= 0)
                {
                    return -1;
                }

                found = i;
            }

            return found;
        }

        private static void UpdateDexIntegrity(byte[] dex)
        {
            byte[] signature = SHA1.HashData(dex.AsSpan(32));
            signature.CopyTo(dex.AsSpan(12, 20));

            BinaryPrimitives.WriteUInt32LittleEndian(dex.AsSpan(8, 4), ComputeAdler32(dex.AsSpan(12)));
        }

        internal static uint ComputeAdler32(ReadOnlySpan<byte> data)
        {
            const uint ModAdler = 65521;
            uint a = 1;
            uint b = 0;

            foreach (byte value in data)
            {
                a = (a + value) % ModAdler;
                b = (b + a) % ModAdler;
            }

            return (b << 16) | a;
        }
    }
}
