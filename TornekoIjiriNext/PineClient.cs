using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace TornekoIjiriNext;

internal sealed record GameInfo(string Title, string Id, string Crc, string GameVersion);

internal sealed class PineClient : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly object _gate = new();
    private bool _disposed;
    private const uint MaxRequestFrame = 650_000;
    private const uint MaxResponseFrame = 450_000;
    public const string AllowedGameId = "SLPM-62199";

    private PineClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _stream.ReadTimeout = 2500;
        _stream.WriteTimeout = 2500;
    }

    public static PineClient Connect(string host, int port)
    {
        var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 2500, SendTimeout = 2500 };
        try
        {
            tcp.Connect(host, port);
            return new PineClient(tcp);
        }
        catch { tcp.Dispose(); throw; }
    }

    private byte[] Request(byte[] commands)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var frameLength = checked((uint)(4 + commands.Length));
            if (frameLength > MaxRequestFrame) throw new ArgumentOutOfRangeException(nameof(commands), "PINE要求が公式上限を超えています。");

            var frame = new byte[frameLength];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, frameLength);
            commands.CopyTo(frame, 4);
            _stream.Write(frame);

            Span<byte> lenBytes = stackalloc byte[4];
            ReadExactly(_stream, lenBytes);
            var responseLength = BinaryPrimitives.ReadUInt32LittleEndian(lenBytes);
            if (responseLength < 5 || responseLength > MaxResponseFrame)
                throw new IOException($"PINE応答サイズが不正です: {responseLength}");

            var response = new byte[responseLength];
            lenBytes.CopyTo(response);
            ReadExactly(_stream, response.AsSpan(4));
            if (response[4] != 0)
                throw new PineCommandException("PCSX2がPINEコマンドを拒否しました。ゲームが起動中か確認してください。");
            return response.AsSpan(5).ToArray();
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            var read = stream.Read(destination);
            if (read == 0) throw new EndOfStreamException("PINE接続が切断されました。");
            destination = destination[read..];
        }
    }

    private static byte[] Opcode(byte opcode) => [opcode];

    public string GetPcsx2Version() => QueryString(8); // MsgVersion

    private string QueryString(byte opcode)
    {
        var body = Request(Opcode(opcode));
        if (body.Length < 4) throw new IOException("PINE文字列応答が短すぎます。");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (size == 0 || size > body.Length - 4 || size > int.MaxValue)
            throw new IOException("PINE文字列応答の長さが不正です。");
        return Encoding.UTF8.GetString(body, 4, (int)size).TrimEnd('\0');
    }

    public uint GetStatus()
    {
        var body = Request(Opcode(0x0F)); // MsgStatus
        if (body.Length != 4) throw new IOException("PINE状態応答の長さが不正です。");
        var status = BinaryPrimitives.ReadUInt32LittleEndian(body);
        if (status > 2) throw new IOException($"PCSX2から未知の状態値を受信しました: {status}");
        return status; // 0=running, 1=paused, 2=shutdown/no VM
    }

    public GameInfo GetGameInfo()
    {
        // Each opcode is an official PINE query. A frame is rejected if a game-dependent
        // query cannot be fulfilled, so incomplete metadata is never considered valid.
        var title = QueryString(0x0B);       // MsgTitle
        var id = QueryString(0x0C);          // MsgID
        var crc = QueryString(0x0D);         // MsgUUID (PCSX2's game CRC string)
        var gameVersion = QueryString(0x0E); // MsgGameVersion
        if (string.IsNullOrWhiteSpace(id)) throw new PineCommandException("ゲームIDを取得できませんでした。");
        return new GameInfo(title, id, crc, gameVersion);
    }

    public byte[] ReadMemory(uint address, int size)
    {
        if (size is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(size), "1〜4096バイトで指定してください。");
        var commands = new byte[size * 5];
        for (var i = 0; i < size; i++)
        {
            commands[i * 5] = 0; // MsgRead8
            BinaryPrimitives.WriteUInt32LittleEndian(commands.AsSpan(i * 5 + 1, 4), checked(address + (uint)i));
        }
        var result = Request(commands);
        if (result.Length != size) throw new IOException($"読み取り長が一致しません (要求 {size}, 応答 {result.Length})。");
        return result;
    }

    /// <summary>
    /// The only write entry point. It performs its own live status and exact game-ID checks,
    /// compares the caller's pre-read bytes, writes, and verifies by reading back.
    /// </summary>
    public byte[] SafeWriteMemory(uint address, ReadOnlySpan<byte> expectedBefore, ReadOnlySpan<byte> newValue)
    {
        if (newValue.Length is < 1 or > 4096 || expectedBefore.Length != newValue.Length)
            throw new ArgumentOutOfRangeException(nameof(newValue), "書き込み前後のデータは同じ1〜4096バイトで指定してください。");

        lock (_gate)
        {
            VerifyWriteAuthorization();
            var current = ReadMemory(address, expectedBefore.Length);
            if (!current.AsSpan().SequenceEqual(expectedBefore))
                throw new PineWriteException("確認後に書き込み前の値が変化したため、中止しました。", current, null);

            // Recheck immediately before issuing the write, not just when the dialog opened.
            VerifyWriteAuthorization();
            WriteBytesRaw(address, newValue);

            var readBack = ReadMemory(address, newValue.Length);
            if (!readBack.AsSpan().SequenceEqual(newValue))
                throw new PineWriteException("書き込み後の読み戻し値が期待値と異なります。成功扱いにはしていません。", current, readBack);
            return readBack;
        }
    }

    private void VerifyWriteAuthorization()
    {
        ThrowIfDisposed();
        var status = GetStatus();
        if (status is not (0 or 1)) throw new PineWriteException("PCSX2でゲームが動作していないため、書き込みを拒否しました。", null, null);
        GameInfo gameInfo;
        try { gameInfo = GetGameInfo(); }
        catch (PineCommandException ex)
        {
            throw new PineWriteException($"ゲーム情報取得に失敗したため、書き込みを拒否しました: {ex.Message}", null, null);
        }
        if (!string.Equals(gameInfo.Id, AllowedGameId, StringComparison.Ordinal))
            throw new PineWriteException($"対象ゲームID {AllowedGameId} と一致しないため、書き込みを拒否しました (取得値: '{gameInfo.Id}').", null, null);
        if (GetStatus() is not (0 or 1)) throw new PineWriteException("ゲーム状態が変化したため、書き込みを拒否しました。", null, null);
    }

    private void WriteBytesRaw(uint address, ReadOnlySpan<byte> data)
    {
        var commands = new byte[data.Length * 6];
        for (var i = 0; i < data.Length; i++)
        {
            commands[i * 6] = 4; // MsgWrite8
            BinaryPrimitives.WriteUInt32LittleEndian(commands.AsSpan(i * 6 + 1, 4), checked(address + (uint)i));
            commands[i * 6 + 5] = data[i];
        }
        var response = Request(commands);
        if (response.Length != 0) throw new IOException($"PINE書き込み応答の長さが不正です: {response.Length}");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PineClient));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
            _client.Dispose();
        }
    }
}

internal sealed class PineCommandException(string message) : IOException(message);

internal sealed class PineWriteException(string message, byte[]? before, byte[]? readBack) : IOException(message)
{
    public byte[]? Before { get; } = before;
    public byte[]? ReadBack { get; } = readBack;
}
