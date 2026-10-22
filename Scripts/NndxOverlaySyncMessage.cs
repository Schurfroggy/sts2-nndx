using System.Buffers.Binary;

namespace Nndx.Scripts;

internal readonly struct NndxOverlaySyncMessage
{
    internal const int PayloadSize = 26;
    internal const byte NoFace = byte.MaxValue;
    public ulong PlayerNetId { get; init; }
    public bool IsPlaying { get; init; }
    public byte FaceIndex { get; init; }
    public ulong Sequence { get; init; }
    public uint RunSeed { get; init; }
    public int RoomId { get; init; }

    internal bool IsValid => Sequence > 0 && RoomId >= 0
        && (IsPlaying ? FaceIndex < NndxPortraitSwap.FaceCount : FaceIndex == NoFace);

    public static byte[] Serialize(NndxOverlaySyncMessage message)
    {
        if (!message.IsValid) throw new InvalidDataException("Invalid NNDX overlay message.");
        byte[] buffer = new byte[PayloadSize];
        buffer[0] = message.IsPlaying ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(1), message.PlayerNetId);
        buffer[9] = message.FaceIndex;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(10), message.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(18), message.RunSeed);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(22), message.RoomId);
        return buffer;
    }

    public static NndxOverlaySyncMessage Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length != PayloadSize || data[0] > 1)
            throw new InvalidDataException("Invalid NNDX overlay payload.");
        var message = new NndxOverlaySyncMessage
        {
            IsPlaying = data[0] == 1,
            PlayerNetId = BinaryPrimitives.ReadUInt64LittleEndian(data[1..]),
            FaceIndex = data[9],
            Sequence = BinaryPrimitives.ReadUInt64LittleEndian(data[10..]),
            RunSeed = BinaryPrimitives.ReadUInt32LittleEndian(data[18..]),
            RoomId = BinaryPrimitives.ReadInt32LittleEndian(data[22..]),
        };
        if (!message.IsValid) throw new InvalidDataException("Invalid NNDX overlay message.");
        return message;
    }
}
