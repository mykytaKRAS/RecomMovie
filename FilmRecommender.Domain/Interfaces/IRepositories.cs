using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Domain.Interfaces;

public interface IMovieRepository
{
    Task<IEnumerable<Movie>> GetAllAsync(MovieFilter filter, int page, int pageSize);
    Task<int> CountAsync(MovieFilter filter);
    Task<Movie?> GetByIdAsync(Guid id);
    Task<IEnumerable<Movie>> SearchAsync(string query, int page, int pageSize);
    Task<IEnumerable<Movie>> GetByGenreAsync(int genreId, int page, int pageSize);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByEmailAsync(string email);
    Task<Guid> CreateAsync(User user);
    Task UpdateAsync(User user);
}

public interface IGenreRepository
{
    Task<IEnumerable<Genre>> GetAllAsync();
    Task<Genre?> GetByIdAsync(int id);
}

public interface IUserRatingRepository
{
    Task<IEnumerable<UserRating>> GetByUserIdAsync(Guid userId);
    Task<UserRating?> GetByUserAndMovieAsync(Guid userId, Guid movieId);
    Task<Guid> CreateAsync(UserRating rating);
    Task UpdateAsync(UserRating rating);
    Task DeleteAsync(Guid id);
}

public interface IUserPreferenceRepository
{
    Task<IEnumerable<UserPreference>> GetByUserIdAsync(Guid userId);
    Task UpsertAsync(UserPreference preference);
    Task DeleteByUserIdAsync(Guid userId);
}

public interface ISurveyRepository
{
    Task<SurveyResponse?> GetByUserIdAsync(Guid userId);
    Task<Guid> CreateAsync(SurveyResponse response);
    Task UpdateAsync(SurveyResponse response);
}

public interface IRecommendationRepository
{
    Task<IEnumerable<Recommendation>> GetByUserIdAsync(Guid userId, string? algorithm = null, int limit = 20);
    Task SaveManyAsync(IEnumerable<Recommendation> recommendations);
    Task MarkClickedAsync(Guid recommendationId);
    Task DeleteByUserIdAsync(Guid userId);
}

public interface IWatchListRepository
{
    Task<IEnumerable<WatchListItem>> GetByUserIdAsync(Guid userId);
    Task<WatchListItem?> GetByUserAndMovieAsync(Guid userId, Guid movieId);
    Task<Guid> CreateAsync(WatchListItem item);
    Task UpdateStatusAsync(Guid id, string status);
    Task DeleteAsync(Guid id);
}

public class MovieFilter
{
    public int? GenreId { get; set; }
    public short? YearFrom { get; set; }
    public short? YearTo { get; set; }
    public decimal? MinRating { get; set; }
    public string? Language { get; set; }
    public string? SortBy { get; set; } = "popularity";
}