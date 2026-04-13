using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FilmRecommender.Application.Services;

// ── AuthService ───────────────────────────────────────────────

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly IConfiguration _config;

    public AuthService(IUserRepository users, IConfiguration config)
    {
        _users = users;
        _config = config;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest req)
    {
        var existing = await _users.GetByEmailAsync(req.Email);
        if (existing is not null) return null;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = req.Email.ToLower().Trim(),
            Username = req.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        };

        await _users.CreateAsync(user);

        return new AuthResponse(user.Id, user.Username, user.Email, GenerateToken(user));
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest req)
    {
        var user = await _users.GetByEmailAsync(req.Email.ToLower().Trim());
        if (user is null) return null;
        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash)) return null;

        return new AuthResponse(user.Id, user.Username, user.Email, GenerateToken(user));
    }

    private string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddDays(7);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds);
        Console.WriteLine("GENERATE ISSUER: " + _config["Jwt:Issuer"]);
        Console.WriteLine("GENERATE AUDIENCE: " + _config["Jwt:Audience"]);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ── MovieService ──────────────────────────────────────────────

public class MovieService
{
    private readonly IMovieRepository _movies;

    public MovieService(IMovieRepository movies) => _movies = movies;

    public async Task<PagedResult<MovieSummaryDto>> GetAllAsync(MovieFilter filter, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var items = await _movies.GetAllAsync(filter, page, pageSize);
        var total = await _movies.CountAsync(filter);

        var dtos = items.Select(ToSummary);
        return new PagedResult<MovieSummaryDto>(dtos, total, page, pageSize);
    }

    public static MovieSummaryDto ToSummary(Movie m) =>
        new(m.Id, m.Title, m.ReleaseYear, m.AvgRating, m.PosterPath,
            m.Genres.Select(g => g.Name));
}

// ── RatingService ─────────────────────────────────────────────

public class RatingService
{
    private readonly IUserRatingRepository _ratings;
    private readonly IMovieRepository _movies;

    public RatingService(IUserRatingRepository ratings, IMovieRepository movies)
    {
        _ratings = ratings;
        _movies = movies;
    }

    // Список моїх оцінок з інформацією про фільм
    public async Task<IEnumerable<MyRatingDto>> GetMyRatingsAsync(Guid userId)
    {
        var ratings = await _ratings.GetByUserIdAsync(userId);
        var result = new List<MyRatingDto>();

        foreach (var r in ratings)
        {
            var movie = await _movies.GetByIdAsync(r.MovieId);
            if (movie is null) continue;

            result.Add(new MyRatingDto(
                r.Id,
                MovieService.ToSummary(movie),
                r.Rating,
                r.Review,
                r.RatedAt));
        }

        return result;
    }

    public async Task<Guid?> CreateAsync(Guid userId, CreateRatingRequest req)
    {
        var existing = await _ratings.GetByUserAndMovieAsync(userId, req.MovieId);
        if (existing is not null) return null;

        var rating = new UserRating
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MovieId = req.MovieId,
            Rating = req.Rating,
            Review = req.Review,
        };

        return await _ratings.CreateAsync(rating);
    }

    public async Task<bool> UpdateAsync(Guid userId, Guid ratingId, UpdateRatingRequest req)
    {
        var ratings = await _ratings.GetByUserIdAsync(userId);
        var existing = ratings.FirstOrDefault(r => r.Id == ratingId);
        if (existing is null || existing.UserId != userId) return false;

        existing.Rating = req.Rating;
        existing.Review = req.Review;

        await _ratings.UpdateAsync(existing);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid ratingId)
    {
        var ratings = await _ratings.GetByUserIdAsync(userId);
        var existing = ratings.FirstOrDefault(r => r.Id == ratingId);
        if (existing is null || existing.UserId != userId) return false;

        await _ratings.DeleteAsync(ratingId);
        return true;
    }
}

// ── WatchListService ──────────────────────────────────────────

public class WatchListService
{
    private readonly IWatchListRepository _watchList;
    private readonly IMovieRepository _movies;

    public WatchListService(IWatchListRepository watchList, IMovieRepository movies)
    {
        _watchList = watchList;
        _movies = movies;
    }

    // Список з опціональним фільтром статусу
    public async Task<IEnumerable<WatchListItemDto>> GetByUserAsync(Guid userId, string? status = null)
    {
        var items = await _watchList.GetByUserIdAsync(userId);

        if (status is not null)
            items = items.Where(i => i.Status == status);

        var result = new List<WatchListItemDto>();

        foreach (var item in items)
        {
            var movie = await _movies.GetByIdAsync(item.MovieId);
            if (movie is null) continue;

            result.Add(new WatchListItemDto(
                item.Id,
                MovieService.ToSummary(movie),
                item.Status,
                item.AddedAt));
        }

        return result;
    }

    public async Task<Guid?> AddAsync(Guid userId, AddToWatchListRequest req)
    {
        var existing = await _watchList.GetByUserAndMovieAsync(userId, req.MovieId);
        if (existing is not null) return null;

        var item = new WatchListItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MovieId = req.MovieId,
            Status = "want",
        };

        return await _watchList.CreateAsync(item);
    }

    public async Task<bool> UpdateStatusAsync(Guid userId, Guid itemId, string status)
    {
        var items = await _watchList.GetByUserIdAsync(userId);
        var item = items.FirstOrDefault(i => i.Id == itemId);
        if (item is null || item.UserId != userId) return false;

        await _watchList.UpdateStatusAsync(itemId, status);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid itemId)
    {
        var items = await _watchList.GetByUserIdAsync(userId);
        var item = items.FirstOrDefault(i => i.Id == itemId);
        if (item is null || item.UserId != userId) return false;

        await _watchList.DeleteAsync(itemId);
        return true;
    }
}

// ── SurveyService ─────────────────────────────────────────────

public class SurveyService
{
    private readonly ISurveyRepository _survey;
    private readonly IUserPreferenceRepository _prefs;

    public SurveyService(ISurveyRepository survey, IUserPreferenceRepository prefs)
    {
        _survey = survey;
        _prefs = prefs;
    }

    public async Task SubmitAsync(Guid userId, SubmitSurveyRequest req)
    {
        // Будуємо вектор вподобань з відповідей
        var vector = req.GenreWeights
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

        var existing = await _survey.GetByUserIdAsync(userId);

        if (existing is null)
        {
            await _survey.CreateAsync(new SurveyResponse
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PreferenceVector = vector,
            });
        }
        else
        {
            existing.PreferenceVector = vector;
            await _survey.UpdateAsync(existing);
        }

        // Зберігаємо ваги жанрів окремо для швидкого доступу в фільтрації
        await _prefs.DeleteByUserIdAsync(userId);

        foreach (var (genreId, weight) in req.GenreWeights)
        {
            await _prefs.UpsertAsync(new UserPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GenreId = genreId,
                Weight = weight,
            });
        }
    }
}