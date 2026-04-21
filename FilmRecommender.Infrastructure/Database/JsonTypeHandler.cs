using Dapper;
using System.Data;
using System.Text.Json;

namespace FilmRecommender.Infrastructure.Database;

public class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override T Parse(object value)
    {
        var json = value?.ToString();
        if (string.IsNullOrEmpty(json)) return default!;
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = value is null
            ? DBNull.Value
            : JsonSerializer.Serialize(value);
        parameter.DbType = DbType.String;
    }
}