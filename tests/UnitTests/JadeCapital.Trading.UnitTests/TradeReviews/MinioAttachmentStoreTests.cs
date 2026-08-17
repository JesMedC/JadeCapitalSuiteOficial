using JadeCapital.Shared.Infrastructure.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.Exceptions;

namespace JadeCapital.Trading.UnitTests.TradeReviews;

/// <summary>
/// Unit tests for <c>MinioAttachmentStore</c> — slice 1d.1 PR-2.
/// Mockeamos <c>IMinioClient</c> via NSubstitute (no necesitamos un
/// container MinIO arriba). Cobertura: delegacion al SDK, manejo de
/// <c>ObjectNotFound</c>, y provisionamiento del bucket al startup.
///
/// Limitation: <c>ObjectStat</c> tiene un ctor non-public y una property
/// <c>Size</c> con setter non-public, por lo que no se puede instanciar
/// directamente desde un test externo. La verificacion de size contra
/// MinIO se cubre via integration tests (slice 1e). Aqui validamos que
/// el SDK se llama con los args correctos y que las excepciones se
/// manejan como best-effort.
/// </summary>
public class MinioAttachmentStoreTests
{
    private readonly IMinioClient _client = Substitute.For<IMinioClient>();
    private readonly Microsoft.Extensions.Logging.ILogger<MinioAttachmentStore> _logger
        = Substitute.For<Microsoft.Extensions.Logging.ILogger<MinioAttachmentStore>>();

    private static MinioOptions SampleOptions() => new()
    {
        Endpoint  = "http://localhost:9000",
        AccessKey = "test",
        SecretKey = "test",
        Bucket    = "jade-test-uploads",
        Ssl       = false,
    };

    private MinioAttachmentStore CreateSut()
        => new(_client, Options.Create(SampleOptions()), _logger);

    [Fact]
    public async Task GetPresignedPutUrlAsync_ReturnsUrlFromSdk()
    {
        var expected = "http://localhost:9000/bucket/obj?signature=abc";
        _client.PresignedPutObjectAsync(Arg.Any<Minio.DataModel.Args.PresignedPutObjectArgs>())
            .Returns(Task.FromResult(expected));

        var sut = CreateSut();
        var url = await sut.GetPresignedPutUrlAsync("foo/bar.png", TimeSpan.FromMinutes(5));

        url.Should().Be(expected);
        await _client.Received(1).PresignedPutObjectAsync(
            Arg.Any<Minio.DataModel.Args.PresignedPutObjectArgs>());
    }

    [Fact]
    public async Task VerifyObjectExistsAsync_StatReturnsNull_FailsSafely()
    {
        // Si MinIO SDK retorna null en StatObjectAsync (caso borde), el
        // store devuelve false en lugar de tirar NullReferenceException.
        _client.StatObjectAsync(Arg.Any<Minio.DataModel.Args.StatObjectArgs>(),
                                  Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromResult<Minio.DataModel.ObjectStat?>(null));

        var sut = CreateSut();
        var ok = await sut.VerifyObjectExistsAsync("foo.png", 1024L);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyObjectExistsAsync_ObjectMissing_ReturnsFalse()
    {
        _client.StatObjectAsync(Arg.Any<Minio.DataModel.Args.StatObjectArgs>(),
                                  Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromException<Minio.DataModel.ObjectStat?>(
                new ObjectNotFoundException("NoSuchKey", "no such key")));

        var sut = CreateSut();
        var ok = await sut.VerifyObjectExistsAsync("foo.png", 1024L);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ObjectMissing_BestEffortNoOp()
    {
        _client.RemoveObjectAsync(Arg.Any<Minio.DataModel.Args.RemoveObjectArgs>(),
                                   Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.FromException(
                new ObjectNotFoundException("NoSuchKey", "no such key")));

        var sut = CreateSut();
        // No debe lanzar excepcion — best-effort.
        var act = () => sut.DeleteAsync("foo.png");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializeAsync_FirstRun_CallsMakeBucket()
    {
        _client.MakeBucketAsync(Arg.Any<Minio.DataModel.Args.MakeBucketArgs>(),
                                 Arg.Any<System.Threading.CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.InitializeAsync();

        await _client.Received(1).MakeBucketAsync(
            Arg.Any<Minio.DataModel.Args.MakeBucketArgs>(),
            Arg.Any<System.Threading.CancellationToken>());
    }
}
