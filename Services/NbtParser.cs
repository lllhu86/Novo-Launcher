using System.IO;
using System.IO.Compression;
using System.Text;

namespace MinecraftLauncher.Services;

public static class NbtParser
{
    public static Dictionary<string, object> ParseGzipFile(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            using var gzipStream = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzipStream, Encoding.UTF8, true);

            byte tagType = reader.ReadByte();
            if (tagType != 10)
                return new Dictionary<string, object>();

            ushort nameLength = reader.ReadUInt16();
            reader.ReadBytes(nameLength);

            return ReadCompound(reader);
        }
        catch (Exception ex)
        {
            App.LogError($"NBT 解析失败：{filePath}", ex);
            return new Dictionary<string, object>();
        }
    }

    public static Dictionary<string, object> ParseRawStream(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);

            byte tagType = reader.ReadByte();
            if (tagType != 10)
                return new Dictionary<string, object>();

            ushort nameLength = reader.ReadUInt16();
            reader.ReadBytes(nameLength);

            return ReadCompound(reader);
        }
        catch (Exception ex)
        {
            App.LogError("NBT 流解析失败", ex);
            return new Dictionary<string, object>();
        }
    }

    public static Dictionary<string, object> ReadCompound(BinaryReader reader)
    {
        var result = new Dictionary<string, object>();

        while (true)
        {
            byte tagType = reader.ReadByte();
            if (tagType == 0)
                break;

            ushort nameLength = reader.ReadUInt16();
            byte[] nameBytes = reader.ReadBytes(nameLength);
            string name = Encoding.UTF8.GetString(nameBytes);

            object value = ReadTag(reader, tagType);
            result[name] = value;
        }

        return result;
    }

    public static object ReadTag(BinaryReader reader, byte tagType)
    {
        return tagType switch
        {
            1 => reader.ReadByte(),
            2 => reader.ReadInt16(),
            3 => reader.ReadInt32(),
            4 => reader.ReadInt64(),
            5 => reader.ReadSingle(),
            6 => reader.ReadDouble(),
            7 => ReadByteArray(reader),
            8 => ReadString(reader),
            9 => ReadList(reader),
            10 => ReadCompound(reader),
            11 => ReadIntArray(reader),
            12 => ReadLongArray(reader),
            _ => new object()
        };
    }

    public static byte[] ReadByteArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return reader.ReadBytes(length);
    }

    public static string ReadString(BinaryReader reader)
    {
        ushort length = reader.ReadUInt16();
        byte[] bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    public static List<object> ReadList(BinaryReader reader)
    {
        var list = new List<object>();
        byte listType = reader.ReadByte();
        int length = reader.ReadInt32();

        for (int i = 0; i < length; i++)
        {
            list.Add(ReadTag(reader, listType));
        }

        return list;
    }

    public static int[] ReadIntArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        var result = new int[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = reader.ReadInt32();
        }
        return result;
    }

    public static long[] ReadLongArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        var result = new long[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = reader.ReadInt64();
        }
        return result;
    }

    public static double GetDouble(object value, double defaultValue = 0.0)
    {
        return value switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            _ => defaultValue
        };
    }

    public static int GetInt32(object value, int defaultValue = 0)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            byte b => b,
            _ => defaultValue
        };
    }

    public static long GetInt64(object value, long defaultValue = 0)
    {
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            _ => defaultValue
        };
    }

    public static bool GetBoolean(object value, bool defaultValue = false)
    {
        return value is bool b ? b : defaultValue;
    }

    public static string GetString(object value, string defaultValue = "")
    {
        return value?.ToString() ?? defaultValue;
    }
}
