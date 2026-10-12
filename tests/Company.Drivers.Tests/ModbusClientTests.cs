using Company.Drivers;
using Xunit;

namespace Company.Drivers.Tests;

/// <summary>示例包行为测试：公共库"每包必有测试"纪律的示范。2.3.1 的修复将以这里的测试驱动。</summary>
public class ModbusClientTests
{
    [Fact]
    public void Connect_合法主机_连接成功()
    {
        using var client = new ModbusClient();
        client.Connect("192.168.1.10", 502);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public void WriteRegister_连接正常_写入并可读回()
    {
        using var client = new ModbusClient();
        client.Connect("192.168.1.10", 502);

        client.WriteRegister(100, 0x1234);

        Assert.Equal(0x1234, client.ReadRegister(100));
    }

    [Fact]
    public void WriteRegister_断连后自动重试_恢复连接并写入()
    {
        // 2.3.1 缺陷修复目标行为：断连后写寄存器应自动重试恢复，不再抛异常。
        using var client = new ModbusClient();
        client.Connect("192.168.1.10", 502);
        client.Disconnect();

        client.WriteRegister(100, 1);

        Assert.True(client.IsConnected);
        Assert.Equal(1, client.ReadRegister(100));
    }
}
