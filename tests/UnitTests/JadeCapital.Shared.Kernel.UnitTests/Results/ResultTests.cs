using FluentAssertions;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Shared.Kernel.UnitTests.Results;

public class ResultTests
{
    [Fact]
    public void Success_HasIsSuccessTrue_AndNoneError()
    {
        var r = Result.Success();

        r.IsSuccess.Should().BeTrue();
        r.IsFailure.Should().BeFalse();
        r.Error.IsNone.Should().BeTrue();
    }

    [Fact]
    public void Failure_HasIsFailureTrue_AndError()
    {
        var r = Result.Failure(Error.NotFound("user.not_found", "User 123 missing"));

        r.IsSuccess.Should().BeFalse();
        r.IsFailure.Should().BeTrue();
        // El prefijo de categoria se antepone al code.
        r.Error.Code.Should().Be("notfound.user.not_found");
        r.Error.Message.Should().Be("User 123 missing");
    }

    [Fact]
    public void Generic_Success_ExposesValue()
    {
        var r = Result.Success(42);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
    }

    [Fact]
    public void Generic_Failure_AccessingValue_Throws()
    {
        var r = Result.Failure<int>(Error.NotFound("x", "y"));

        Action act = () => { var _ = r.Value; };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Generic_Success_AccessingError_Throws()
    {
        var r = Result.Success(42);

        Action act = () => { var _ = r.Error; };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_FromErrorToResult_CreatesFailure()
    {
        var err = Error.NotFound("user.not_found", "x");
        Result r = err;

        r.IsFailure.Should().BeTrue();
        r.Error.Should().Be(err);
    }

    [Fact]
    public void NonGeneric_Failure_AccessingError_DoesNotThrow()
    {
        var err = Error.Conflict("x", "y");
        var r = Result.Failure(err);

        r.Error.Should().Be(err);
    }
}

public class ErrorTests
{
    [Fact]
    public void None_IsNone()
    {
        Error.None.IsNone.Should().BeTrue();
    }

    [Fact]
    public void Factories_CreateTypedErrors_WithCategoryPrefix()
    {
        // Cada factory debe anteponer su prefijo de categoria.
        Error.Validation("v.c", "msg").Code.Should().Be("validation.v.c");
        Error.NotFound("n.c", "msg").Code.Should().Be("notfound.n.c");
        Error.Conflict("c.c", "msg").Code.Should().Be("conflict.c.c");
        Error.Unauthorized("u.c", "msg").Code.Should().Be("unauthorized.u.c");
        Error.Forbidden("f.c", "msg").Code.Should().Be("forbidden.f.c");
        Error.Failure("x.c", "msg").Code.Should().Be("failure.x.c");
        Error.Infrastructure("i.c", "msg").Code.Should().Be("infrastructure.i.c");
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = Error.NotFound("x", "y");
        var b = Error.NotFound("x", "y");
        var c = Error.NotFound("x", "z");

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void None_IsEmpty()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.Message.Should().BeEmpty();
    }
}