using FluentValidation;
using PKeetDashboard.API.DTOs;

namespace PKeetDashboard.API.Validation;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(256);
    }
}

public sealed class RegisterInitiateRequestValidator : AbstractValidator<RegisterInitiateRequest>
{
    public RegisterInitiateRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(256);

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.LastName)
            .MaximumLength(64);
    }
}

public sealed class RegisterVerifyRequestValidator : AbstractValidator<RegisterVerifyRequest>
{
    public RegisterVerifyRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);

        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches("^[0-9]{6}$")
            .WithMessage("Code must be a 6-digit number.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(256);

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.LastName)
            .MaximumLength(64);
    }
}

public sealed class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty()
            .MaximumLength(4096);
    }
}

public sealed class DesktopAuthInitiateRequestValidator : AbstractValidator<DesktopAuthInitiateRequest>
{
    public DesktopAuthInitiateRequestValidator()
    {
        RuleFor(x => x.Client)
            .NotEmpty()
            .MaximumLength(32);

        RuleFor(x => x.RedirectUri)
            .NotEmpty()
            .MaximumLength(2048);

        RuleFor(x => x.State)
            .NotEmpty()
            .MaximumLength(256);
    }
}

public sealed class DesktopAuthExchangeRequestValidator : AbstractValidator<DesktopAuthExchangeRequest>
{
    public DesktopAuthExchangeRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(256);
    }
}

