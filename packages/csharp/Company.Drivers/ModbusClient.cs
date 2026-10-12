namespace Company.Drivers;

/// <summary>
/// 示例设备驱动（演示用）：模拟 Modbus 客户端的最小行为。
/// 2.3.1 修复：断连后写寄存器自动重试恢复连接（2.3.0 及之前直接抛异常）。
/// </summary>
public sealed class ModbusClient : IDisposable
{
    private readonly Dictionary<ushort, ushort> _registers = new();
    private bool _connected;

    /// <summary>是否在断连后自动重试。2.3.1 起默认开启（缺陷修复）。</summary>
    public bool RetryOnDisconnect { get; private set; } = true;

    public bool IsConnected => _connected;

    public void Connect(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _connected = true;
    }

    public void Disconnect() => _connected = false;

    /// <summary>模拟写寄存器：断连时自动重试恢复后写入。</summary>
    public void WriteRegister(ushort address, ushort value)
    {
        if (!_connected)
        {
            if (!RetryOnDisconnect)
            {
                throw new InvalidOperationException("连接已断开且重试策略已关闭");
            }

            _connected = true; // 自动重试恢复连接（2.3.1 修复）
        }

        _registers[address] = value;
    }

    public ushort ReadRegister(ushort address) =>
        _connected && _registers.TryGetValue(address, out var value)
            ? value
            : throw new KeyNotFoundException($"寄存器 {address} 未写入或未连接");

    /// <summary>批量读取寄存器：从起始地址连续读取 count 个（2.4.0 新增，对应 Modbus 功能码 0x03 场景）。</summary>
    public ushort[] ReadRegisters(ushort startAddress, ushort count)
    {
        var values = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadRegister((ushort)(startAddress + i));
        }

        return values;
    }

    public void Dispose() => _connected = false;
}
