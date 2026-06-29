using Dapper;
using FilmRecommender.Infrastructure.Database;
using FilmRecommender.API.Endpoints;
using FilmRecommender.API.Extensions;

DefaultTypeMap.MatchNamesWithUnderscores = true;
SqlMapper.AddTypeHandler(new JsonTypeHandler<Dictionary<string, double>>());

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSwaggerServices()
    .AddJwtAuthentication(builder.Configuration)
    .AddCorsPolicy(builder.Configuration)
    .AddRepositories()
    .AddApplicationServices();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapMovieEndpoints();
app.MapUserEndpoints();
app.MapGenreEndpoints();
app.MapRatingEndpoints();
app.MapSurveyEndpoints();
app.MapWatchListEndpoints();
app.MapRecommendationEndpoints();
app.MapEvaluationEndpoints();

app.Run();