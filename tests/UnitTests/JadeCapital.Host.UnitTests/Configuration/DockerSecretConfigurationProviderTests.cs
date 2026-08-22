// Wave 10 slice 10.2 - Unit tests for DockerSecretConfigurationProvider.
//
// RED scenarios (matches tasks.md Phase 4.1):
//   1. Load_WhenSecretsDirectoryAbsent_LeavesDataEmpty   (silent no-op)
//   2. Load_WithSecretsPresent_PopulatesDataWithTrimmedValues
//   3. Load_WithMultipleSecrets_AggregatesIntoDataDictionary
//   4. Load_WithMalformedSecretContent_TrimsLeadingAndTrailingWhitespace
//   5. Constructor_WithEmptySecretsDirectory_ThrowsArgumentException
//   6. Source_Build_ReturnsConfiguredProvider (IConfigurationSource wiring)
//   7. Load_IsIdempotent_RepeatedCallsProduceSameData

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using JadeCapital.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace JadeCapital.Host.UnitTests.Configuration;

public class DockerSecretConfigurationProviderTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    /// <summary>
    /// Creates a temporary directory and registers it for cleanup.
    /// Each test gets its own scratch directory so tests don't share state.
    /// </summary>
    private string CreateTempDirectory()
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            $"jade-secrets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static void WriteSecret(string directory, string name, string value)
    {
        File.WriteAllText(Path.Combine(directory, name), value);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; failures are not test-affecting.
            }
        }
        _tempDirs.Clear();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_WhenSecretsDirectoryAbsent_LeavesDataEmpty()
    {
        // Arrange - a directory that we know won't exist.
        var ghost = Path.Combine(
            Path.GetTempPath(),
            $"jade-secrets-{Guid.NewGuid():N}-ghost");
        var sut = new DockerSecretConfigurationProvider(ghost);

        // Act
        sut.Load();

        // Assert - silent no-op; no exceptions, empty data dictionary.
        sut.GetLoadedSecrets().Should().BeEmpty();
    }

    [Fact]
    public void Load_WithSecretsPresent_PopulatesDataWithTrimmedValues()
    {
        // Arrange
        var dir = CreateTempDirectory();
        WriteSecret(dir, "postgres_password", "super-secret-value");

        var sut = new DockerSecretConfigurationProvider(dir);

        // Act
        sut.Load();

        // Assert - the key is namespaced under __Secret: and the value matches.
        var data = sut.GetLoadedSecrets();
        data.Should().ContainKey("__Secret:postgres_password");
        data["__Secret:postgres_password"].Should().Be("super-secret-value");
    }

    [Fact]
    public void Load_WithMultipleSecrets_AggregatesIntoDataDictionary()
    {
        // Arrange
        var dir = CreateTempDirectory();
        WriteSecret(dir, "postgres_password", "pg-secret");
        WriteSecret(dir, "jwt_access_token_secret", "jwt-secret");
        WriteSecret(dir, "stripe_api_key", "stripe-secret");

        var sut = new DockerSecretConfigurationProvider(dir);

        // Act
        sut.Load();

        // Assert - all three secrets present, none missing, no extras.
        var data = sut.GetLoadedSecrets();
        data.Should().HaveCount(3);
        data.Should().Contain(new KeyValuePair<string, string>(
            "__Secret:postgres_password", "pg-secret"));
        data.Should().Contain(new KeyValuePair<string, string>(
            "__Secret:jwt_access_token_secret", "jwt-secret"));
        data.Should().Contain(new KeyValuePair<string, string>(
            "__Secret:stripe_api_key", "stripe-secret"));
    }

    [Fact]
    public void Load_WithMalformedSecretContent_TrimsLeadingAndTrailingWhitespace()
    {
        // Arrange - Docker mounts the raw file contents. A trailing newline
        // is the rule, not the exception.
        var dir = CreateTempDirectory();
        WriteSecret(dir, "jwt_access_token_secret", "  jwt-secret\n\n");

        var sut = new DockerSecretConfigurationProvider(dir);

        // Act
        sut.Load();

        // Assert - leading + trailing whitespace stripped, interior preserved.
        sut.GetLoadedSecrets()["__Secret:jwt_access_token_secret"]
            .Should().Be("jwt-secret");
    }

    [Fact]
    public void Constructor_WithEmptySecretsDirectory_ThrowsArgumentException()
    {
        // Arrange + Act
        Action act = () => _ = new DockerSecretConfigurationProvider(string.Empty);

        // Assert - we fail fast at construction so misconfigured callers
        // (e.g. typo'd `ADD` directive in compose) surface as crashes,
        // not silent-no-ops.
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Secrets directory*");
    }

    [Fact]
    public void Source_Build_ReturnsConfiguredProvider()
    {
        // Arrange - double-check the IConfigurationSource integration, which
        // is what Program.cs actually wires (not the provider directly).
        var dir = CreateTempDirectory();
        WriteSecret(dir, "minio_root_password", "minio-secret");

        var source = new DockerSecretConfigurationSource(dir);
        var builder = new ConfigurationBuilder();

        // Act
        var provider = source.Build(builder);
        provider.Should().BeOfType<DockerSecretConfigurationProvider>();
        provider.Load();

        // Assert
        ((DockerSecretConfigurationProvider)provider)
            .GetLoadedSecrets()
            .Should().Contain(new KeyValuePair<string, string>(
                "__Secret:minio_root_password", "minio-secret"));
    }

    [Fact]
    public void Load_IsIdempotent_RepeatedCallsProduceSameData()
    {
        // Arrange - Load() may be invoked multiple times if the host
        // re-binds configuration (e.g. on a Watch-only filesystem in a
        // dev swarm). The data set must not grow or shrink.
        var dir = CreateTempDirectory();
        WriteSecret(dir, "mailgun_api_key", "mg-key");
        var sut = new DockerSecretConfigurationProvider(dir);

        // Act
        sut.Load();
        var firstPassKeys = new SortedSet<string>(sut.GetLoadedSecrets().Keys);
        sut.Load();
        var secondPassKeys = new SortedSet<string>(sut.GetLoadedSecrets().Keys);

        // Assert
        firstPassKeys.Should().BeEquivalentTo(secondPassKeys);
        sut.GetLoadedSecrets().Should().HaveCount(1);
    }

    [Fact]
    public void AddFileBackedSecrets_Materializes_Canonical_Stripe_SecretKey()
    {
        var dir = CreateTempDirectory();
        var secretPath = Path.Combine(dir, "stripe_api_key");
        File.WriteAllText(secretPath, "  sk_test_from_file\n");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey:File"] = secretPath,
                ["Stripe:ApiKey"] = "sk_test_legacy_must_not_win"
            })
            .AddFileBackedSecrets()
            .Build();

        configuration["Stripe:SecretKey"].Should().Be("sk_test_from_file");
    }

    [Fact]
    public void AddFileBackedSecrets_Leaves_Direct_SecretKey_When_No_File_Is_Configured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_direct"
            })
            .AddFileBackedSecrets()
            .Build();

        configuration["Stripe:SecretKey"].Should().Be("sk_test_direct");
    }
}
