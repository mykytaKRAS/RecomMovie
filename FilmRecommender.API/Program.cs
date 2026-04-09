using FilmRecommender.API.Extensions;

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
app.MapGenreEndpoints();
app.MapRatingEndpoints();
app.MapSurveyEndpoints();
app.MapWatchListEndpoints();

app.Run();