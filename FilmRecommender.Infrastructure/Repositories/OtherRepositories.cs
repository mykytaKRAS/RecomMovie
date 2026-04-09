using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Dapper;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;
using FilmRecommender.Infrastructure.Database;
using Npgsql;
using System.Text.Json;

namespace FilmRecommender.Infrastructure.Repositories;

// ── Genre ─────────────────────────────────────────────────────

public class GenreRepository : IGenreRepository
{
    private readonly IDbConnectionFactory _db;
    public GenreRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Genre>> GetAllAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Genre>("SELECT id, name FROM genres ORDER BY name");
    }

    public async Task<Genre?> GetByIdAsync(int id)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<Genre>(
            "SELECT id, name FROM genres WHERE id = @id", new { id });
    }
}

// ── UserRating ────────────────────────────────────────────────

public class UserRatingRepository : IUserRatingRepository
{
    private readonly IDbConnectionFactory _db;
    public UserRatingRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<UserRating>> GetByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<UserRating>(
            "SELECT * FROM user_ratings WHERE user_id = @userId ORDER BY rated_at DESC",
            new { userId });
    }

    public async Task<UserRating?> GetByUserAndMovieAsync(Guid userId, Guid movieId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<UserRating>(
            "SELECT * FROM user_ratings WHERE user_id = @userId AND movie_id = @movieId",
            new { userId, movieId });
    }

    public async Task<Guid> CreateAsync(UserRating rating)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO user_ratings (id, user_id, movie_id, rating, review, rated_at)
            VALUES (@Id, @UserId, @MovieId, @Rating, @Review, NOW())
            RETURNING id", rating);
    }

    public async Task UpdateAsync(UserRating rating)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            UPDATE user_ratings
            SET rating = @Rating, review = @Review, rated_at = NOW()
            WHERE id = @Id", rating);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM user_ratings WHERE id = @id", new { id });
    }
}

// ── UserPreference ────────────────────────────────────────────

public class UserPreferenceRepository : IUserPreferenceRepository
{
    private readonly IDbConnectionFactory _db;
    public UserPreferenceRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<UserPreference>> GetByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<UserPreference>(
            "SELECT * FROM user_preferences WHERE user_id = @userId",
            new { userId });
    }

    public async Task UpsertAsync(UserPreference preference)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO user_preferences (id, user_id, genre_id, weight)
            VALUES (@Id, @UserId, @GenreId, @Weight)
            ON CONFLICT (user_id, genre_id)
            DO UPDATE SET weight = EXCLUDED.weight", preference);
    }

    public async Task DeleteByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM user_preferences WHERE user_id = @userId", new { userId });
    }
}

// ── Survey ────────────────────────────────────────────────────

public class SurveyRepository : ISurveyRepository
{
    private readonly IDbConnectionFactory _db;
    public SurveyRepository(IDbConnectionFactory db) => _db = db;

    public async Task<SurveyResponse?> GetByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();

        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM survey_responses WHERE user_id = @userId", new { userId });

        if (row is null) return null;

        return new SurveyResponse
        {
            Id = row.id,
            UserId = row.user_id,
            CompletedAt = row.completed_at,
            PreferenceVector = JsonSerializer.Deserialize<Dictionary<string, decimal>>(
                                    (string)row.preference_vector) ?? []
        };
    }

    public async Task<Guid> CreateAsync(SurveyResponse response)
    {
        using var conn = (NpgsqlConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(@"
            INSERT INTO survey_responses (id, user_id, completed_at, preference_vector)
            VALUES (@id, @userId, NOW(), @vector::jsonb)
            RETURNING id", conn);

        cmd.Parameters.AddWithValue("id", response.Id);
        cmd.Parameters.AddWithValue("userId", response.UserId);
        cmd.Parameters.AddWithValue("vector", JsonSerializer.Serialize(response.PreferenceVector));

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task UpdateAsync(SurveyResponse response)
    {
        using var conn = (NpgsqlConnection)_db.CreateConnection();
        await conn.OpenAsync();

        using var cmd = new NpgsqlCommand(@"
            UPDATE survey_responses
            SET preference_vector = @vector::jsonb, completed_at = NOW()
            WHERE id = @id", conn);

        cmd.Parameters.AddWithValue("id", response.Id);
        cmd.Parameters.AddWithValue("vector", JsonSerializer.Serialize(response.PreferenceVector));

        await cmd.ExecuteNonQueryAsync();
    }
}

// ── Recommendation ────────────────────────────────────────────

public class RecommendationRepository : IRecommendationRepository
{
    private readonly IDbConnectionFactory _db;
    public RecommendationRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Recommendation>> GetByUserIdAsync(
        Guid userId, string? algorithm = null, int limit = 20)
    {
        using var conn = _db.CreateConnection();

        var sql = algorithm is null
            ? @"SELECT * FROM recommendations WHERE user_id = @userId
                ORDER BY score DESC LIMIT @limit"
            : @"SELECT * FROM recommendations WHERE user_id = @userId AND algorithm = @algorithm
                ORDER BY score DESC LIMIT @limit";

        return await conn.QueryAsync<Recommendation>(sql, new { userId, algorithm, limit });
    }

    public async Task SaveManyAsync(IEnumerable<Recommendation> recommendations)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO recommendations (id, user_id, movie_id, score, algorithm, generated_at, was_clicked)
            VALUES (@Id, @UserId, @MovieId, @Score, @Algorithm, NOW(), false)
            ON CONFLICT (user_id, movie_id, algorithm)
            DO UPDATE SET score = EXCLUDED.score, generated_at = NOW()",
            recommendations);
    }

    public async Task MarkClickedAsync(Guid recommendationId)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE recommendations SET was_clicked = true WHERE id = @id",
            new { id = recommendationId });
    }

    public async Task DeleteByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM recommendations WHERE user_id = @userId", new { userId });
    }
}

// ── WatchList ─────────────────────────────────────────────────

public class WatchListRepository : IWatchListRepository
{
    private readonly IDbConnectionFactory _db;
    public WatchListRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<WatchListItem>> GetByUserIdAsync(Guid userId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<WatchListItem>(
            "SELECT * FROM watch_list WHERE user_id = @userId ORDER BY added_at DESC",
            new { userId });
    }

    public async Task<WatchListItem?> GetByUserAndMovieAsync(Guid userId, Guid movieId)
    {
        using var conn = _db.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<WatchListItem>(
            "SELECT * FROM watch_list WHERE user_id = @userId AND movie_id = @movieId",
            new { userId, movieId });
    }

    public async Task<Guid> CreateAsync(WatchListItem item)
    {
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<Guid>(@"
            INSERT INTO watch_list (id, user_id, movie_id, status, added_at)
            VALUES (@Id, @UserId, @MovieId, @Status, NOW())
            RETURNING id", item);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE watch_list SET status = @status WHERE id = @id",
            new { id, status });
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync("DELETE FROM watch_list WHERE id = @id", new { id });
    }
}