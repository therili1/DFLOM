using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Launcher.Services
{
    /// <summary>
    /// Мінімальний рідер формату NBT (Named Binary Tag), якого достатньо, щоб
    /// прочитати службові дані зі стандартного файлу Minecraft level.dat
    /// (назва світу, сід, режим гри, hardcore, дата останнього заходу тощо).
    /// Не претендує на повну підтримку специфікації NBT — лише на читання.
    /// </summary>
    public static class NbtReader
    {
        private enum TagType : byte
        {
            End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5,
            Double = 6, ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12
        }

        public static Dictionary<string, object?> ReadCompoundFile(string path)
        {
            using var fileStream = File.OpenRead(path);

            // level.dat зазвичай стиснутий gzip-ом (перші два байти 0x1F 0x8B).
            Stream stream = fileStream;
            var header = new byte[2];
            int read = fileStream.Read(header, 0, 2);
            fileStream.Seek(0, SeekOrigin.Begin);

            if (read == 2 && header[0] == 0x1F && header[1] == 0x8B)
            {
                stream = new GZipStream(fileStream, CompressionMode.Decompress);
            }

            using var br = new BinaryReader(stream);

            var rootType = (TagType)br.ReadByte();
            if (rootType != TagType.Compound)
            {
                throw new InvalidDataException("Кореневий тег level.dat не є Compound — файл пошкоджено або формат невідомий.");
            }

            SkipString(br); // ім'я кореневого тега (зазвичай порожнє)
            return ReadCompoundBody(br);
        }

        private static Dictionary<string, object?> ReadCompoundBody(BinaryReader br)
        {
            var result = new Dictionary<string, object?>();

            while (true)
            {
                var type = (TagType)br.ReadByte();
                if (type == TagType.End) break;

                string name = ReadString(br);
                object? value = ReadPayload(br, type);
                result[name] = value;
            }

            return result;
        }

        private static object? ReadPayload(BinaryReader br, TagType type)
        {
            switch (type)
            {
                case TagType.Byte: return br.ReadByte();
                case TagType.Short: return ReadInt16BE(br);
                case TagType.Int: return ReadInt32BE(br);
                case TagType.Long: return ReadInt64BE(br);
                case TagType.Float: return ReadSingleBE(br);
                case TagType.Double: return ReadDoubleBE(br);
                case TagType.ByteArray:
                    {
                        int len = ReadInt32BE(br);
                        return br.ReadBytes(len);
                    }
                case TagType.String:
                    return ReadString(br);
                case TagType.List:
                    {
                        var listType = (TagType)br.ReadByte();
                        int len = ReadInt32BE(br);
                        var list = new List<object?>();
                        for (int i = 0; i < len; i++)
                        {
                            list.Add(listType == TagType.End ? null : ReadPayload(br, listType));
                        }
                        return list;
                    }
                case TagType.Compound:
                    return ReadCompoundBody(br);
                case TagType.IntArray:
                    {
                        int len = ReadInt32BE(br);
                        var arr = new int[len];
                        for (int i = 0; i < len; i++) arr[i] = ReadInt32BE(br);
                        return arr;
                    }
                case TagType.LongArray:
                    {
                        int len = ReadInt32BE(br);
                        var arr = new long[len];
                        for (int i = 0; i < len; i++) arr[i] = ReadInt64BE(br);
                        return arr;
                    }
                default:
                    throw new InvalidDataException($"Невідомий NBT тег: {type}");
            }
        }

        private static void SkipString(BinaryReader br) => ReadString(br);

        private static string ReadString(BinaryReader br)
        {
            int len = ReadUInt16BE(br);
            if (len == 0) return string.Empty;
            var bytes = br.ReadBytes(len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static short ReadInt16BE(BinaryReader br) => BinaryPrimitives.ReadInt16BigEndian(br.ReadBytes(2));
        private static ushort ReadUInt16BE(BinaryReader br) => BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
        private static int ReadInt32BE(BinaryReader br) => BinaryPrimitives.ReadInt32BigEndian(br.ReadBytes(4));
        private static long ReadInt64BE(BinaryReader br) => BinaryPrimitives.ReadInt64BigEndian(br.ReadBytes(8));

        private static float ReadSingleBE(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes);
        }

        private static double ReadDoubleBE(BinaryReader br)
        {
            var bytes = br.ReadBytes(8);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes);
        }

        public static T? GetValue<T>(Dictionary<string, object?> compound, string key, T? fallback = default)
        {
            if (compound.TryGetValue(key, out var val) && val is T typed)
            {
                return typed;
            }
            return fallback;
        }
    }
}
