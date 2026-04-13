using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dapper;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;
using FilmRecommender.Infrastructure.Database;

namespace FilmRecommender.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _db;

    public UserRepository(IDbConnectionFactory db) => _db = db;

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @id", new { id });
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var conn = _db.CreateConnection();

        return await conn.QuerySingleOrDefaultAsync<User>(@"
        SELECT 
            id AS Id,
            email AS Email,
            password_hash AS PasswordHash,
            username AS Username,
            created_at AS CreatedAt,
            updated_at AS UpdatedAt
        FROM users
        WHERE email = @email",
            new { email });
    }

    public async Task<Guid> CreateAsync(User user)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO users (id, email, password_hash, username, created_at, updated_at)
            VALUES (@Id, @Email, @PasswordHash, @Username, NOW(), NOW())
            RETURNING id", user);
    }

    public async Task UpdateAsync(User user)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE users
            SET email = @Email, username = @Username, updated_at = NOW()
            WHERE id = @Id", user);
    }
}