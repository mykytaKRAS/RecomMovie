using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilmRecommender.Domain.Models;

public class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Director
{
    public Guid Id { get; set; }
    public int? TmdbId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }
    public string? Country { get; set; }
}

public class Actor
{
    public Guid Id { get; set; }
    public int? TmdbId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }
    public string? Country { get; set; }
    public string? RoleName { get; set; }
    public short? CastOrder { get; set; }
}

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserRating
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public decimal Rating { get; set; }
    public string? Review { get; set; }
    public DateTime RatedAt { get; set; }
}

public class UserPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int GenreId { get; set; }
    public decimal Weight { get; set; }
}

public class SurveyResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime CompletedAt { get; set; }
    public Dictionary<string, decimal> PreferenceVector { get; set; } = [];
}

public class Recommendation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public decimal Score { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool WasClicked { get; set; }
}

public class WatchListItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid MovieId { get; set; }
    public string Status { get; set; } = "want";
    public DateTime AddedAt { get; set; }
}