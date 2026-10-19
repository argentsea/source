// Copyright John Hicks. Licensed under the MIT license.
using System;
using System.Buffers.Binary;
using System.ComponentModel;

namespace ArgentSea
{
    /// <summary>Fixed-width, big-endian numeric storage used by compiled mapping expressions.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class NumericBinaryConverter
    {
        public static int GetWidth(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(ulong)) return 8;
            if (type == typeof(uint)) return 4;
            if (type == typeof(ushort)) return 2;
            if (type == typeof(sbyte)) return 1;
            return 0;
        }

        public static byte[] GetBytes(ulong value)
        {
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            return bytes;
        }
        public static byte[] GetBytes(uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }
        public static byte[] GetBytes(ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            return bytes;
        }
        public static byte[] GetBytes(sbyte value) => new[] { unchecked((byte)value) };

        private static void Validate(byte[] bytes, string name, Type targetType, int width)
        {
            if (bytes == null) throw new UnexpectedNullException(targetType, name);
            if (bytes.Length != width)
                throw new InvalidBinaryValueLengthException(name, targetType, width, bytes.Length);
        }
        public static ulong ReadUInt64(byte[] bytes, string name, Type targetType)
        {
            Validate(bytes, name, targetType, 8);
            return BinaryPrimitives.ReadUInt64BigEndian(bytes);
        }
        public static uint ReadUInt32(byte[] bytes, string name, Type targetType)
        {
            Validate(bytes, name, targetType, 4);
            return BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }
        public static ushort ReadUInt16(byte[] bytes, string name, Type targetType)
        {
            Validate(bytes, name, targetType, 2);
            return BinaryPrimitives.ReadUInt16BigEndian(bytes);
        }
        public static sbyte ReadSByte(byte[] bytes, string name, Type targetType)
        {
            Validate(bytes, name, targetType, 1);
            return unchecked((sbyte)bytes[0]);
        }
    }
}
