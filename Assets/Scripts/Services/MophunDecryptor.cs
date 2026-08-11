/*
 * (C) 2023 Radrat Softworks
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 */

using System;
using System.IO;
using System.Numerics;

namespace Nofun.Services
{
    public enum MophunNormalizationStatus
    {
        Plain,
        Decrypted,
        InvalidMpn,
        UnsupportedEncryption,
        DecryptionFailed
    }

    public sealed class MophunNormalizationResult
    {
        public MophunNormalizationStatus Status { get; private set; }
        public byte[] Bytes { get; private set; }
        public string Message { get; private set; }

        public bool Succeeded => Status == MophunNormalizationStatus.Plain ||
                                  Status == MophunNormalizationStatus.Decrypted;
        public bool WasDecrypted => Status == MophunNormalizationStatus.Decrypted;

        private MophunNormalizationResult(MophunNormalizationStatus status, byte[] bytes, string message)
        {
            Status = status;
            Bytes = bytes;
            Message = message;
        }

        public static MophunNormalizationResult Plain(byte[] bytes) =>
            new MophunNormalizationResult(MophunNormalizationStatus.Plain, bytes, null);

        public static MophunNormalizationResult Decrypted(byte[] bytes) =>
            new MophunNormalizationResult(MophunNormalizationStatus.Decrypted, bytes, null);

        public static MophunNormalizationResult Failure(MophunNormalizationStatus status, string message) =>
            new MophunNormalizationResult(status, null, message);
    }

    /// <summary>
    /// Detects and normalizes Mophun executable code.  Profiles are deliberately
    /// kept behind this class so another Mophun encryption family can be added
    /// without changing the import pipeline.
    /// </summary>
    public static class MophunDecryptor
    {
        private const int HeaderSize = 40;
        private const int MaxOpcode = 116;
        private const byte CompressedFlag = 0x80;

        // The SE profile used by the Honey Cave 2 MPNs.  These are embedded so
        // Android users never need to select or install a key file.  They are
        // not used for files which are already plain or for compressed MPNs.
        private const string HoneyCaveSelectorKeyBase64 = "4wuMnHTAJrTPuoIN0HKzKA==";
        private const string HoneyCaveBigKeysBase64 =
            "WWM9pYVVjCI9sQ9Z73OsHV6ZAAy3MdDXcBM0JbErxhcC46WtxBjBugVg7IsZ8TEB1PwAl4SQ/5k//zrZqnF8NeKOyywUPofuJMTb9OYj6LWfMkDsaAO5jepcuSaqBECqyCHu5wVHblUbvlYI8HRQ5xWk9YQB6V4KKD8+2nL3mvD7lfx8xFQZ/nNuCqkmANJDBb5Y+q/Gvt3mAZ3A8kPev3/FevAFRmbwohYHHgwFat42XUMuzM0ruhmrsRr3kjkIPXKtze8Gc5aONuAMcmJYKixbLmblHwwPuYv8d03R45ByRTovun8avJPDAI01h+WzjXsZd9LMNcXQRdFTpYII3lW23+fJ/r9BzPn3w2V/fl21D6frAw7c7vFymAQOG+vFTeQRevRoUJkQ/fOaqPRMIaDKmCNYC74B5zdLiDnYa3t9SXYq/9HM4pVQ54ebUaP2SxWgDQUrQ0LmFwE6CB9ZMEDn7wVqJ5kDp2g6d6yJ5qh3RGZyB4JLyJc7/gFtHF+gA/fqcRNRaCM67GY0jwzE/Oox2ofkDf8XEySp4yf8gYAfNW/bLJP/Zq2xtILzPqGrpIKdghvinM3AvLFh6yjBWHXygbiBz7alLUiuvtXVizZaYOkowtaf06ZpYLIpbdUR9v2q2vATTbVolfshgpHJi2PtE2sd9IkhJptWh63gJO8=";

        private static readonly byte[] HoneyCaveSelectorKey = Convert.FromBase64String(HoneyCaveSelectorKeyBase64);
        private static readonly byte[] HoneyCaveBigKeys = Convert.FromBase64String(HoneyCaveBigKeysBase64);

        private sealed class EncryptionProfile
        {
            public readonly string Name;
            public readonly byte[] SelectorKey;
            public readonly byte[] BigKeys;

            public EncryptionProfile(string name, byte[] selectorKey, byte[] bigKeys)
            {
                Name = name;
                SelectorKey = selectorKey;
                BigKeys = bigKeys;
            }
        }

        private static readonly EncryptionProfile HoneyCaveProfile =
            new EncryptionProfile("Honey Cave 2 SE", HoneyCaveSelectorKey, HoneyCaveBigKeys);

        private struct Layout
        {
            public int CodeOffset;
            public int CodeSize;
            public int DataOffset;
            public int ResourceOffset;
            public int ResourceSize;
            public bool IsCompressed;
        }

        public static MophunNormalizationResult Normalize(byte[] source)
        {
            Layout layout;
            string error;
            if (!TryReadLayout(source, out layout, out error))
            {
                return MophunNormalizationResult.Failure(MophunNormalizationStatus.InvalidMpn, error);
            }

            if (layout.IsCompressed)
            {
                return MophunNormalizationResult.Failure(
                    MophunNormalizationStatus.UnsupportedEncryption,
                    "This MPN uses compressed sections, which are not supported yet.");
            }

            if (IsPlainCode(source, layout))
            {
                return MophunNormalizationResult.Plain(source);
            }

            byte[] decrypted;
            if (!TryDecryptWithProfile(source, layout, HoneyCaveProfile, out decrypted, out error))
            {
                return MophunNormalizationResult.Failure(
                    error.StartsWith("Encrypted code", StringComparison.Ordinal)
                        ? MophunNormalizationStatus.UnsupportedEncryption
                        : MophunNormalizationStatus.DecryptionFailed,
                    error);
            }

            return MophunNormalizationResult.Decrypted(decrypted);
        }

        public static bool TryValidatePlain(byte[] bytes, out string error)
        {
            Layout layout;
            if (!TryReadLayout(bytes, out layout, out error))
            {
                return false;
            }

            if (layout.IsCompressed)
            {
                error = "The cached MPN is compressed.";
                return false;
            }

            if (!IsPlainCode(bytes, layout))
            {
                error = "The cached MPN still has encrypted or invalid executable code.";
                return false;
            }

            return true;
        }

        private static bool TryReadLayout(byte[] bytes, out Layout layout, out string error)
        {
            layout = new Layout();
            error = null;
            if (bytes == null || bytes.Length < HeaderSize)
            {
                error = "The selected file is too small to be a Mophun MPN.";
                return false;
            }

            if (bytes[0] != (byte)'V' || bytes[1] != (byte)'M' || bytes[2] != (byte)'G' || bytes[3] != (byte)'P')
            {
                error = "The selected file is not a valid VMGP/Mophun MPN.";
                return false;
            }

            uint codeSize = ReadUInt32(bytes, 12);
            uint dataSize = ReadUInt32(bytes, 16);
            uint resourceSize = ReadUInt32(bytes, 24);
            uint poolSize = ReadUInt32(bytes, 32);
            uint stringSize = ReadUInt32(bytes, 36);
            long codeEnd = (long)HeaderSize + codeSize;
            long dataEnd = codeEnd + dataSize;
            long resourceEnd = dataEnd + resourceSize;
            long poolEnd = resourceEnd + (long)poolSize * 8L;
            long fileEnd = poolEnd + stringSize;

            if (codeSize == 0 || codeSize > int.MaxValue || dataSize > int.MaxValue || resourceSize > int.MaxValue ||
                poolSize > int.MaxValue || stringSize > int.MaxValue || fileEnd > bytes.Length)
            {
                error = "The MPN section sizes are outside the file bounds.";
                return false;
            }

            if ((resourceSize < 8) || dataEnd > int.MaxValue || resourceEnd > int.MaxValue)
            {
                error = "The MPN resource section is invalid.";
                return false;
            }

            layout.CodeOffset = HeaderSize;
            layout.CodeSize = (int)codeSize;
            layout.DataOffset = (int)codeEnd;
            layout.ResourceOffset = (int)dataEnd;
            layout.ResourceSize = (int)resourceSize;
            layout.IsCompressed = (bytes[11] & CompressedFlag) != 0;
            return true;
        }

        private static bool IsPlainCode(byte[] bytes, Layout layout)
        {
            if (layout.CodeSize < 4)
            {
                return false;
            }

            return (ReadUInt32(bytes, layout.CodeOffset) & 0xFF) < MaxOpcode;
        }

        private static bool TryDecryptWithProfile(byte[] source, Layout layout, EncryptionProfile profile,
            out byte[] result, out string error)
        {
            result = null;
            error = null;

            byte[] meta;
            if (!TryFindMeta(source, layout, out meta))
            {
                error = "Encrypted code was detected, but the MPN has no readable META resource.";
                return false;
            }

            uint encryptedSelector = ReadUInt32(meta, 0x8C);
            uint selector = XteaDecryptSelector(encryptedSelector, profile.SelectorKey);
            if (selector > 3)
            {
                error = "Encrypted code was detected, but its Honey Cave encryption profile is unknown.";
                return false;
            }

            byte[] modulus = new byte[128];
            Buffer.BlockCopy(profile.BigKeys, (int)selector * 128, modulus, 0, modulus.Length);
            byte[] decryptedMeta;
            if (!TryRsaDeriveKey(meta, modulus, out decryptedMeta))
            {
                error = "Encrypted code was detected, but the embedded Mophun profile could not derive its key.";
                return false;
            }

            uint[] symmetricKey = new uint[4];
            for (int i = 0; i < symmetricKey.Length; i++)
            {
                symmetricKey[i] = ReadUInt32(decryptedMeta, 0x14 + i * 4);
            }

            result = new byte[source.Length];
            Buffer.BlockCopy(source, 0, result, 0, source.Length);
            int wordCount = layout.CodeSize / 4;
            for (int i = 0; i < wordCount; i++)
            {
                int offset = layout.CodeOffset + i * 4;
                uint encryptedWord = ReadUInt32(source, offset);
                WriteUInt32(result, offset, DecryptCodeWord(encryptedWord, symmetricKey));
            }

            if (!IsPlainCode(result, layout))
            {
                result = null;
                error = "Encrypted code was detected, but decryption did not produce valid VMGP code.";
                return false;
            }

            return true;
        }

        private static bool TryFindMeta(byte[] source, Layout layout, out byte[] meta)
        {
            meta = null;
            int start = layout.ResourceOffset;
            int end = start + layout.ResourceSize;
            if (start + 4 > end)
            {
                return false;
            }

            uint resourceHeaderSize = ReadUInt32(source, start);
            if (resourceHeaderSize < 8 || resourceHeaderSize > layout.ResourceSize || (resourceHeaderSize & 3) != 0)
            {
                return false;
            }

            int count = (int)(resourceHeaderSize / 4) - 1;
            if (count <= 0 || start + resourceHeaderSize > end)
            {
                return false;
            }

            int previous = (int)resourceHeaderSize;
            for (int i = 0; i < count; i++)
            {
                int current = i == count - 1
                    ? layout.ResourceSize
                    : (int)ReadUInt32(source, start + 4 + i * 4);
                if (current < previous || current > layout.ResourceSize)
                {
                    return false;
                }

                int resourceStart = start + previous;
                int resourceLength = current - previous;
                if (resourceLength >= 9 + 0x98 && source[resourceStart] == (byte)'M' &&
                    source[resourceStart + 1] == (byte)'E' && source[resourceStart + 2] == (byte)'T' &&
                    source[resourceStart + 3] == (byte)'A')
                {
                    meta = new byte[0x98];
                    Buffer.BlockCopy(source, resourceStart + 9, meta, 0, meta.Length);
                    return true;
                }

                previous = current;
            }

            return false;
        }

        private static bool TryRsaDeriveKey(byte[] meta, byte[] modulusBytes, out byte[] decrypted)
        {
            decrypted = null;
            try
            {
                byte[] modulusPositive = new byte[modulusBytes.Length + 1];
                byte[] metaPositive = new byte[128 + 1];
                Buffer.BlockCopy(modulusBytes, 0, modulusPositive, 0, modulusBytes.Length);
                Buffer.BlockCopy(meta, 0, metaPositive, 0, 128);
                BigInteger modulus = new BigInteger(modulusPositive);
                BigInteger ciphertext = new BigInteger(metaPositive);
                if (modulus <= 1 || ciphertext.Sign < 0)
                {
                    return false;
                }

                byte[] value = BigInteger.ModPow(ciphertext, new BigInteger(3), modulus).ToByteArray();
                decrypted = new byte[128];
                Buffer.BlockCopy(value, 0, decrypted, 0, Math.Min(value.Length, decrypted.Length));
                for (int i = 0x26; i < decrypted.Length; i++)
                {
                    if (decrypted[i] != 0 && decrypted[i] != 0xFF && decrypted[i] != 1)
                    {
                        decrypted = null;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                decrypted = null;
                return false;
            }
        }

        private static uint XteaDecryptSelector(uint input, byte[] keyBytes)
        {
            uint[] key = new uint[8];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (uint)(keyBytes[i * 2] | (keyBytes[i * 2 + 1] << 8));
            }

            uint sum = 0xC6EF3720;
            uint v0 = input & 0xFFFF;
            uint v1 = (input >> 16) & 0xFFFFFF;
            for (int i = 0; i < 32; i++)
            {
                v1 -= (Mix(v0) ^ ((key[2 * ((sum >> 11) & 3)] + sum) & 0xFFFF));
                sum -= 0x9E3779B9;
                v0 -= (Mix(v1) ^ ((key[2 * (sum & 3)] + sum) & 0xFFFF));
            }

            return ((v1 & 0xFFFF) << 16) | (v0 & 0xFFFF);
        }

        private static uint DecryptCodeWord(uint block, uint[] key)
        {
            uint r4 = block;
            uint r10 = key[0];
            uint r8 = key[1];
            uint r5 = block & 0xFFFF;
            uint r7 = key[2];
            uint r9 = key[3];

            r4 = (r4 >> 16) - KeyMix(r5, r7, 0x540F);
            r5 = r5 - KeyMix(r4, r7, 0xDA56);
            r4 = r4 - KeyMix(r5, r9, 0xDA56);
            uint r6 = r5 - KeyMix(r4, r8, 0x609D);
            r5 = r4 - KeyMix(r6, r10, 0x609D);

            r6 = r6 - (Mix(r5) ^ ((r10 + 0xE6E4) & 0xFFFF));
            r4 = r5 - (Mix(r6) ^ ((r10 + 0xE6E4) & 0xFFFF));
            r6 = r6 - KeyMix(r4, r9, 0x6D2B);
            r5 = r4 - KeyMix(r6, r8, 0x6D2B);
            r6 = r6 - KeyMix(r5, r7, 0xF372);
            r4 = r5 - KeyMix(r6, r7, 0xF372);
            r5 = r6 - KeyMix(r4, r8, 0x79B9);
            uint r3 = r4 - KeyMix(r5, r9, 0x79B9);

            uint high = r3 & 0xFFFF;
            uint low = r5 - (Mix(r3) ^ r10);
            return ((high & 0xFFFF) << 16) | (low & 0xFFFF);
        }

        private static uint KeyMix(uint value, uint key, uint constant)
        {
            return Mix(value) ^ ((key + constant) & 0xFFFF);
        }

        private static uint Mix(uint value)
        {
            return (((((value & 0xFFFF) << 4) & 0xFFFF) ^ ((value & 0xFFFF) >> 5)) + value);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) |
                          (bytes[offset + 3] << 24));
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
