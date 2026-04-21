using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Infrastructure.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;

namespace FilmRecommender.Infrastructure.Services
{
    public class FeatureVectorizationService
    {
        private readonly IDbConnectionFactory _db;
        private readonly IMovieScoringRepository _scoring;

        public FeatureVectorizationService(
            IDbConnectionFactory db,
            IMovieScoringRepository scoring)
        {
            _db = db;
            _scoring = scoring;
        }

        public async Task VectorizeAllMoviesAsync()
        {
            using var conn = _db.CreateConnection();

            // Отримуємо всі фільми з їх жанрами
            var movies = await conn.QueryAsync<dynamic>(@"
            SELECT
                m.id,
                m.avg_rating,
                m.popularity_score,
                m.release_year,
                m.original_language,
                STRING_AGG(g.name, ',') AS genre_names
            FROM movies m
            LEFT JOIN movie_genres mg ON mg.movie_id = m.id
            LEFT JOIN genres g ON g.id = mg.genre_id
            GROUP BY m.id, m.avg_rating, m.popularity_score,
                     m.release_year, m.original_language");

            // Нормалізуємо рейтинг і популярність для всіх фільмів
            var allRatings = movies.Select(m => (double)(m.avg_rating ?? 0)).ToList();
            var allPopularity = movies.Select(m => (double)(m.popularity_score ?? 0)).ToList();
            var maxRating = allRatings.Any() ? allRatings.Max() : 10.0;
            var maxPopularity = allPopularity.Any() ? allPopularity.Max() : 1.0;

            foreach (var movie in movies)
            {
                var vector = BuildVector(
                    movie,
                    maxRating,
                    maxPopularity);

                await _scoring.UpdateFeatureVectorAsync(
                    (Guid)movie.id, vector);
            }

            Console.WriteLine($"Vectorized {movies.Count()} movies.");
        }

        private static Dictionary<string, double> BuildVector(
            dynamic movie,
            double maxRating,
            double maxPopularity)
        {
            var vector = new Dictionary<string, double>();

            // ── Жанри (бінарні ознаки) ───────────────────────────
            var genreNames = ((string?)movie.genre_names ?? "").Split(',',
                StringSplitOptions.RemoveEmptyEntries);

            var allGenres = new[]
            {
            "Action", "Adventure", "Animation", "Comedy", "Crime",
            "Documentary", "Drama", "Family", "Fantasy", "History",
            "Horror", "Music", "Mystery", "Romance", "Science Fiction",
            "TV Movie", "Thriller", "War", "Western"
        };

            foreach (var genre in allGenres)
            {
                var key = "genre_" + genre.ToLower().Replace(" ", "_");
                vector[key] = genreNames.Contains(genre, StringComparer.OrdinalIgnoreCase)
                    ? 1.0 : 0.0;
            }

            // ── Нормалізований рейтинг [0..1] ────────────────────
            vector["rating_norm"] = maxRating > 0
                ? Math.Min((double)(movie.avg_rating ?? 0) / maxRating, 1.0)
                : 0.0;

            // ── Нормалізована популярність [0..1] ─────────────────
            vector["popularity_norm"] = maxPopularity > 0
                ? Math.Min((double)(movie.popularity_score ?? 0) / maxPopularity, 1.0)
                : 0.0;

            // ── Декада випуску (one-hot) ──────────────────────────
            var year = (int?)movie.release_year ?? 0;
            var decades = new[] { 1970, 1980, 1990, 2000, 2010, 2020 };
            foreach (var decade in decades)
            {
                vector[$"decade_{decade}s"] =
                    (year >= decade && year < decade + 10) ? 1.0 : 0.0;
            }

            // ── Мова (one-hot для топ мов) ────────────────────────
            var lang = (string?)movie.original_language ?? "";
            foreach (var l in new[] { "en", "fr", "de", "ja", "ko", "es", "it" })
                vector[$"lang_{l}"] = lang == l ? 1.0 : 0.0;

            return vector;
        }
    }
}
