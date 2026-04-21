using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FilmRecommender.Domain.Models;

public class Movie
{
    public Guid Id { get; set; }
    public int TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Description { get; set; }
    public short? ReleaseYear { get; set; }
    public short? DurationMin { get; set; }
    public decimal? AvgRating { get; set; }
    public int VoteCount { get; set; }
    public decimal? PopularityScore { get; set; }
    public string? PosterPath { get; set; }
    public string? OriginalLanguage { get; set; }
    public DateTime CreatedAt { get; set; }

    public Dictionary<string, double>? FeatureVector { get; set; }
    public IEnumerable<Genre> Genres { get; set; } = [];
    public IEnumerable<Actor> Actors { get; set; } = [];
    public IEnumerable<Director> Directors { get; set; } = [];
    public IEnumerable<Country> Countries { get; set; } = [];
    public IEnumerable<Tag> Tags { get; set; } = [];
}
