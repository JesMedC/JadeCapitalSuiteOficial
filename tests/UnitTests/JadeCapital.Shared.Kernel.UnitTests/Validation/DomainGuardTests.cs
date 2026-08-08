using FluentAssertions;
using FluentValidation.Results;
using JadeCapital.Shared.Kernel.Exceptions;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Validation;

namespace JadeCapital.Shared.Kernel.UnitTests.Validation;

public class DomainGuardTests
{
    [Fact]
    public void EnsureSuccess_NonGeneric_Success_DoesNotThrow()
    {
        var sut = Result.Success();

        Action act = () => DomainGuard.EnsureSuccess(sut);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSuccess_Generic_Success_DoesNotThrow()
    {
        var sut = Result.Success(123);

        Action act = () => DomainGuard.EnsureSuccess(sut);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSuccess_NotFound_ThrowsNotFound()
    {
        var r = Result.Failure(Error.NotFound("user_not_found", "missing"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<NotFoundDomainException>()
            .Which.Error.Code.Should().Be("notfound.user_not_found");
    }

    [Fact]
    public void EnsureSuccess_Conflict_ThrowsConflict()
    {
        var r = Result.Failure(Error.Conflict("email_taken", "x"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<ConflictDomainException>()
            .Which.Error.Code.Should().Be("conflict.email_taken");
    }

    [Fact]
    public void EnsureSuccess_Unauthorized_ThrowsUnauthorized()
    {
        var r = Result.Failure(Error.Unauthorized("auth.bad", "x"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<UnauthorizedDomainException>();
    }

    [Fact]
    public void EnsureSuccess_Forbidden_ThrowsForbidden()
    {
        var r = Result.Failure(Error.Forbidden("perm.denied", "x"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<ForbiddenDomainException>();
    }

    [Fact]
    public void EnsureSuccess_ValidationPrefix_ThrowsValidation()
    {
        var r = Result.Failure(Error.Validation("email.invalid", "x"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<ValidationException>()
            .Which.Failures.Should().ContainSingle();
    }

    [Fact]
    public void EnsureSuccess_Generic_NotFound_ThrowsNotFound()
    {
        var r = Result.Failure<int>(Error.NotFound("user.not_found", "missing"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<NotFoundDomainException>();
    }

    [Fact]
    public void EnsureSuccess_UnknownCode_ThrowsGenericDomainException()
    {
        // Sin prefijo de categoria conocido.
        var r = Result.Failure(new Error("weird.code", "x"));

        Action act = () => DomainGuard.EnsureSuccess(r);

        act.Should().Throw<DomainException>()
            .Which.Error.Code.Should().Be("weird.code");
    }
}

public class ValidationExceptionTests
{
    [Fact]
    public void Constructor_StoresAllFailures()
    {
        var failures = new[]
        {
            new ValidationFailure("Email", "is required"),
            new ValidationFailure("Password", "too short"),
            new ValidationFailure("Email", "not valid")
        };

        var ex = new ValidationException(failures);

        ex.Failures.Should().HaveCount(3);
        ex.Failures.Select(f => f.PropertyName).Should().Contain(new[] { "Email", "Password" });
        ex.Message.Should().Be("One or more validation errors occurred.");
    }
}