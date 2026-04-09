using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilmRecommender.Application.DTOs;

// ── Auth ──────────────────────────────────────────

public record RegisterRequest(
    string Email,
    string Username,
    string Password
);

public record LoginRequest(
    string Email,
    string Password
);

public record AuthResponse(
    Guid UserId,
    string Username,
    string Email,
    string Token
);

// ── Movies ────────────────────────────────────────

public record MovieSummaryDto(
    Guid Id,
    string Title,
    short? ReleaseYear,
    decimal? AvgRating,
    string? PosterPath,
    IEnumerable<string> Genres
);

public record MovieDetailDto(
    Guid Id,
    string Title,
    string? OriginalTitle,
    string? Description,
    short? ReleaseYear,
    short? DurationMin,
    decimal? AvgRating,
    int VoteCount,
    string? PosterPath,
    string? OriginalLanguage,
    IEnumerable<string> Genres,
    IEnumerable<ActorDto> Actors,
    IEnumerable<string> Directors,
    IEnumerable<string> Countries,
    IEnumerable<string> Tags,
    UserRatingDto? UserRating
);

public record ActorDto(
    string FullName,
    string? RoleName
);

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}

// ── Ratings ───────────────────────────────────────

public record UserRatingDto(
    Guid Id,
    decimal Rating,
    string? Review,
    DateTime RatedAt
);

public record CreateRatingRequest(
    Guid MovieId,
    decimal Rating,
    string? Review
);

public record UpdateRatingRequest(
    decimal Rating,
    string? Review
);

// ── Survey ────────────────────────────────────────

public record SurveyQuestionDto(
    int GenreId,
    string GenreName
);

public record SubmitSurveyRequest(
    // GenreId → оцінка від 0 до 1
    Dictionary<int, decimal> GenreWeights,
    // Улюблені фільми (до 5)
    List<Guid> FavoriteMovieIds
);

public record SurveyResponseDto(
    Guid Id,
    DateTime CompletedAt,
    bool IsCompleted
);

// ── Recommendations ───────────────────────────────

public record RecommendationDto(
    Guid RecommendationId,
    MovieSummaryDto Movie,
    decimal Score,
    string Algorithm
);

// ── WatchList ─────────────────────────────────────

public record WatchListItemDto(
    Guid Id,
    MovieSummaryDto Movie,
    string Status,
    DateTime AddedAt
);

public record AddToWatchListRequest(
    Guid MovieId
);

public record UpdateWatchStatusRequest(
    string Status
);

// ── Common ────────────────────────────────────────

public record ErrorResponse(
    string Message,
    string? Details = null
);

public record SuccessResponse(
    string Message
);