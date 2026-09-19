using ChatApp.Application.Abstractions;
using ChatApp.Application.Auth;
using ChatApp.Application.Common;
using ChatApp.Infrastructure.Persistence;
using ChatApp.Infrastructure.Persistence.Repositories;
using ChatApp.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChatApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));

        var sql = config.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is missing.");

        services.AddDbContext<ChatDbContext>(o => o.UseSqlServer(sql, s =>
        {
            s.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
