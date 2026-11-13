using System.Buffers.Binary;

namespace VkHotkeys.Core.Protocol;

/// <summary>
/// Кадрирование Native Messaging: uint32 длина (порядок байт little-endian на x86/x64) + полезная нагрузка.
/// </summary>
public static class FrameCodec
{
    /// <summary>Chrome не принимает от хоста сообщения больше 1 МБ.</summary>
    public const int MaxFrameSize = 1024 * 1024;

    /// <summary>Читает один кадр. Возвращает null при корректном конце потока.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameSize)
            throw new InvalidDataException($"Недопустимая длина кадра: {length}");

        var payload = new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, payload, ct).ConfigureAwait(false))
            throw new EndOfStreamException("Поток оборвался посреди кадра");
        return payload;
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        if (payload.Length > MaxFrameSize)
            throw new InvalidDataException($"Кадр слишком большой: {payload.Length}");

        // Заголовок и тело одной записью, чтобы не было частичных кадров при конкурентной отправке.
        var buffer = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, payload.Length);
        payload.CopyTo(buffer.AsMemory(4));
        await stream.WriteAsync(buffer, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return false;
                throw new EndOfStreamException("Поток оборвался посреди кадра");
            }
            read += n;
        }
        return true;
    }
}
