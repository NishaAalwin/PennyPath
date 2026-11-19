using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Auth;
using PennyPath.Infrastructure.Auth;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Tests;

public class AuthServiceTests
{
    private static AuthService Create(out AppDbContext db, out FakeTimeProvider clock)
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        var tokens = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 40) }));
        return new AuthService(db, tokens, clock);
    }

    private static AuthService Create(out AppDbContext db) => Create(out db, out _);

    [Fact]
    public async Task Register_creates_user_with_hashed_password_and_returns_token()
    {
        var svc = Create(out var db);

        var r = await svc.RegisterAsync(new RegisterRequest("  Asha@Example.com ", "correct-horse"));

        Assert.True(r.Succeeded);
        Assert.Equal("asha@example.com", r.Response!.Email);
        Assert.False(string.IsNullOrEmpty(r.Response.Token));
        var user = await db.Users.SingleAsync();
        Assert.NotEqual("correct-horse", user.PasswordHash);
    }

    [Theory]
    [InlineData("", "correct-horse")]
    [InlineData("not-an-email", "correct-horse")]
    [InlineData("a@b.com", "short")]
    [InlineData("a@b.com", "password123")]
    [InlineData("a@b.com", "PASSWORD")]
    [InlineData("jonathan@b.com", "my-jonathan-pass")]
    public async Task Register_rejects_invalid_or_weak_input(string email, string password)
    {
        var r = await Create(out _).RegisterAsync(new RegisterRequest(email, password));
        Assert.Equal(AuthError.InvalidInput, r.Error);
    }

    [Fact]
    public async Task Register_rejects_oversized_passwords()
    {
        var r = await Create(out _).RegisterAsync(new RegisterRequest("a@b.com", new string('x', AuthService.MaxPasswordLength + 1)));
        Assert.Equal(AuthError.InvalidInput, r.Error);
    }

    [Fact]
    public async Task Register_rejects_duplicate_email_case_insensitively()
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));

        var r = await svc.RegisterAsync(new RegisterRequest("A@B.com", "another-pass1"));

        Assert.Equal(AuthError.EmailTaken, r.Error);
    }

    [Fact]
    public async Task Login_succeeds_with_correct_password()
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));

        var r = await svc.LoginAsync(new LoginRequest("A@b.com", "correct-horse"));

        Assert.True(r.Succeeded);
    }

    [Theory]
    [InlineData("a@b.com", "wrong-password")]
    [InlineData("nobody@b.com", "correct-horse")]
    public async Task Login_fails_with_same_error_for_wrong_password_or_unknown_user(string email, string password)
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));

        var r = await svc.LoginAsync(new LoginRequest(email, password));

        Assert.Equal(AuthError.InvalidCredentials, r.Error);
        Assert.Equal("Invalid email or password.", r.Message);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_even_for_the_right_password()
    {
        var svc = Create(out _);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
            await svc.LoginAsync(new LoginRequest("a@b.com", "wrong-password"));

        var r = await svc.LoginAsync(new LoginRequest("a@b.com", "correct-horse"));

        Assert.Equal(AuthError.LockedOut, r.Error);
    }

    [Fact]
    public async Task Lockout_expires_after_the_lockout_duration()
    {
        var svc = Create(out _, out var clock);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
            await svc.LoginAsync(new LoginRequest("a@b.com", "wrong-password"));

        clock.Advance(AuthService.LockoutDuration + TimeSpan.FromSeconds(1));
        var r = await svc.LoginAsync(new LoginRequest("a@b.com", "correct-horse"));

        Assert.True(r.Succeeded);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_counter()
    {
        var svc = Create(out var db);
        await svc.RegisterAsync(new RegisterRequest("a@b.com", "correct-horse"));
        for (var i = 0; i < AuthService.MaxFailedAttempts - 1; i++)
            await svc.LoginAsync(new LoginRequest("a@b.com", "wrong-password"));
        await svc.LoginAsync(new LoginRequest("a@b.com", "correct-horse"));

        await svc.LoginAsync(new LoginRequest("a@b.com", "wrong-password"));

        Assert.Equal(1, (await db.Users.SingleAsync()).FailedLoginCount);
    }
}
