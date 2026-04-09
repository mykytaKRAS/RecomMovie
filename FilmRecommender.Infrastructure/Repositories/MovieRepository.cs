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

public class MovieRepository : IMovieRepository
{
    private readonly IDbConnectionFactory _db;

    public MovieRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Movie>> GetAllAsync(MovieFilter filter, int page, int pageSize)
    {
        using var conn = _db.CreateConnection();

        var (where, parameters) = BuildWhereClause(filter);
        var orderBy = filter.SortBy switch
        {
            "rating" => "m.avg_rating DESC NULLS LAST",
            "year" => "m.release_year DESC NULLS LAST",
            "title" => "m.title ASC",
            _ => "m.popularity_score DESC NULLS LAST"
        };

        var sql = $@"
            SELECT DISTINCT
                m.id, m.tmdb_id, m.title, m.release_year,
                m.avg_rating, m.poster_path, m.popularity_score
            FROM movies m
            LEFT JOIN movie_genres mg ON mg.movie_id = m.id
            {where}
            ORDER BY {orderBy}
            LIMIT @PageSize OFFSET @Offset";

        parameters.Add("PageSize", pageSize);
        parameters.Add("Offset", (page - 1) * pageSize);

        var movies = (await conn.QueryAsync<Movie>(sql, parameters)).ToList();

        if (!movies.Any()) return movies;

        var ids = movies.Select(m => m.Id).ToArray();
        var genres = await GetGenresForMoviesAsync(conn, ids);

        foreach (var movie in movies)
            movie.Genres = genres.GetValueOrDefault(movie.Id, []);

        return movies;
    }

    public async Task<int> CountAsync(MovieFilter filter)
    {
        using var conn = _db.CreateConnection();
        var (where, parameters) = BuildWhereClause(filter);

        var sql = $@"
            SELECT COUNT(DISTINCT m.id)
            FROM movies m
            LEFT JOIN movie_genres mg ON mg.movie_id = m.id
            {where}";

        return await conn.ExecuteScalarAsync<int>(sql, parameters);
    }

    public async Task<Movie?> GetByIdAsync(Guid id)
    {
        using var conn = _db.CreateConnection();

        var movie = await conn.QuerySingleOrDefaultAsync<Movie>(
            "SELECT * FROM movies WHERE id = @id", new { id });

        if (movie is null) return null;

        var ids = new[] { movie.Id };

        movie.Genres = (await GetGenresForMoviesAsync(conn, ids)).GetValueOrDefault(movie.Id, []);
        movie.Actors = await GetActorsForMovieAsync(conn, movie.Id);
        movie.Directors = await GetDirectorsForMovieAsync(conn, movie.Id);
        movie.Countries = await GetCountriesForMovieAsync(conn, movie.Id);
        movie.Tags = await GetTagsForMovieAsync(conn, movie.Id);

        return movie;
    }

    public async Task<IEnumerable<Movie>> SearchAsync(string query, int page, int pageSize)
    {
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT m.id, m.title, m.release_year, m.avg_rating, m.poster_path
            FROM movies m
            WHERE m.title ILIKE @query OR m.original_title ILIKE @query
            ORDER BY m.popularity_score DESC NULLS LAST
            LIMIT @PageSize OFFSET @Offset";

        return await conn.QueryAsync<Movie>(sql, new
        {
            query = $"%{query}%",
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        });
    }

    public async Task<IEnumerable<Movie>> GetByGenreAsync(int genreId, int page, int pageSize)
    {
        using var conn = _db.CreateConnection();

        var sql = @"
            SELECT m.id, m.title, m.release_year, m.avg_rating, m.poster_path
            FROM movies m
            JOIN movie_genres mg ON mg.movie_id = m.id
            WHERE mg.genre_id = @genreId
            ORDER BY m.popularity_score DESC NULLS LAST
            LIMIT @PageSize OFFSET @Offset";

        return await conn.QueryAsync<Movie>(sql, new
        {
            genreId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        });
    }

    // ── Приватні хелпери ─────────────────────────────

    private static (string where, DynamicParameters parameters) BuildWhereClause(MovieFilter filter)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (filter.GenreId.HasValue)
        {
            conditions.Add("mg.genre_id = @GenreId");
            p.Add("GenreId", filter.GenreId.Value);
        }
        if (filter.YearFrom.HasValue)
        {
            conditions.Add("m.release_year >= @YearFrom");
            p.Add("YearFrom", filter.YearFrom.Value);
        }
        if (filter.YearTo.HasValue)
        {
            conditions.Add("m.release_year <= @YearTo");
            p.Add("YearTo", filter.YearTo.Value);
        }
        if (filter.MinRating.HasValue)
        {
            conditions.Add("m.avg_rating >= @MinRating");
            p.Add("MinRating", filter.MinRating.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            conditions.Add("m.original_language = @Language");
            p.Add("Language", filter.Language);
        }

        var where = conditions.Any()
            ? "WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        return (where, p);
    }

    private static async Task<Dictionary<Guid, IEnumerable<Genre>>> GetGenresForMoviesAsync(
        System.Data.IDbConnection conn, Guid[] ids)
    {
        var sql = @"
            SELECT mg.movie_id, g.id, g.name
            FROM genres g
            JOIN movie_genres mg ON mg.genre_id = g.id
            WHERE mg.movie_id = ANY(@ids)";

        var rows = await conn.QueryAsync<(Guid MovieId, int Id, string Name)>(sql, new { ids });

        return rows
            .GroupBy(r => r.MovieId)
            .ToDictionary(
                g => g.Key,
                g => (IEnumerable<Genre>)g.Select(r => new Genre { Id = r.Id, Name = r.Name }).ToList());
    }

    private static async Task<IEnumerable<Actor>> GetActorsForMovieAsync(
        System.Data.IDbConnection conn, Guid movieId)
    {
        var sql = @"
            SELECT a.id, a.full_name, ma.role_name, ma.cast_order
            FROM actors a
            JOIN movie_actors ma ON ma.actor_id = a.id
            WHERE ma.movie_id = @movieId
            ORDER BY ma.cast_order";

        return await conn.QueryAsync<Actor>(sql, new { movieId });
    }

    private static async Task<IEnumerable<Director>> GetDirectorsForMovieAsync(
        System.Data.IDbConnection conn, Guid movieId)
    {
        var sql = @"
            SELECT d.id, d.full_name
            FROM directors d
            JOIN movie_directors md ON md.director_id = d.id
            WHERE md.movie_id = @movieId";

        return await conn.QueryAsync<Director>(sql, new { movieId });
    }

    private static async Task<IEnumerable<Country>> GetCountriesForMovieAsync(
        System.Data.IDbConnection conn, Guid movieId)
    {
        var sql = @"
            SELECT c.id, c.name, c.code
            FROM countries c
            JOIN movie_countries mc ON mc.country_id = c.id
            WHERE mc.movie_id = @movieId";

        return await conn.QueryAsync<Country>(sql, new { movieId });
    }

    private static async Task<IEnumerable<Tag>> GetTagsForMovieAsync(
        System.Data.IDbConnection conn, Guid movieId)
    {
        var sql = @"
            SELECT t.id, t.name
            FROM tags t
            JOIN movie_tags mt ON mt.tag_id = t.id
            WHERE mt.movie_id = @movieId";

        return await conn.QueryAsync<Tag>(sql, new { movieId });
    }
}
