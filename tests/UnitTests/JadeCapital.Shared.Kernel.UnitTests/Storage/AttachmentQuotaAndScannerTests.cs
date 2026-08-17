namespace JadeCapital.Shared.Kernel.UnitTests.Storage;

using JadeCapital.Shared.Kernel.Storage;

public class AttachmentQuotaTests
{
    [Fact]
    public void Defaults_MatchSpecFiftyMiB_HundredAttachments_NinetyDays()
    {
        var q = AttachmentQuota.Default;
        q.MaxTotalBytes.Should().Be(52_428_800L);
        q.MaxAttachmentCount.Should().Be(100);
        q.ExpirationDays.Should().Be(90);
    }

    [Fact]
    public void Ctor_AllowsCustomOverride()
    {
        var q = new AttachmentQuota(MaxTotalBytes: 1_000_000L, MaxAttachmentCount: 5, ExpirationDays: 30);
        q.MaxTotalBytes.Should().Be(1_000_000L);
        q.MaxAttachmentCount.Should().Be(5);
        q.ExpirationDays.Should().Be(30);
    }

    [Fact]
    public void Record_EqualityRespectsAllFields()
    {
        var a = new AttachmentQuota(MaxTotalBytes: 10, MaxAttachmentCount: 1, ExpirationDays: 1);
        var b = new AttachmentQuota(MaxTotalBytes: 10, MaxAttachmentCount: 1, ExpirationDays: 1);
        var c = new AttachmentQuota(MaxTotalBytes: 11, MaxAttachmentCount: 1, ExpirationDays: 1);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void Default_IsTheParameterlessInstance()
    {
        AttachmentQuota.Default.Should().Be(new AttachmentQuota());
    }
}

public class VirusScanResultTests
{
    [Theory]
    [InlineData(VirusScanResult.NotScanned, (byte)0)]
    [InlineData(VirusScanResult.Clean, (byte)1)]
    [InlineData(VirusScanResult.Infected, (byte)2)]
    [InlineData(VirusScanResult.Error, (byte)3)]
    [InlineData(VirusScanResult.Timeout, (byte)4)]
    public void Enum_UnderlyingByte_MatchesSpec(VirusScanResult value, byte expected)
    {
        ((byte)value).Should().Be(expected);
    }

    [Fact]
    public void ScannerUnavailableException_ExposesMessage()
    {
        var ex = new ScannerUnavailableException("clamd down");
        ex.Message.Should().Be("clamd down");
    }

    [Fact]
    public void ScannerUnavailableException_WrapsInner()
    {
        var inner = new InvalidOperationException("tcp refused");
        var ex = new ScannerUnavailableException("scanner unavailable", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}