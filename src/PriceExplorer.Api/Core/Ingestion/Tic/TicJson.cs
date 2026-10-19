using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PriceExplorer.Api.Core.Ingestion.Tic;

internal static class TicJson
{
    /// <summary>Serializer settings for TiC files: snake_case, case-insensitive, tolerant of common type mistakes.</summary>
    public static JsonSerializerOptions CreateOptions(IJsonTypeInfoResolver? resolver = null) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        TypeInfoResolver = resolver ?? new DefaultJsonTypeInfoResolver(),
        Converters =
        {
            new FlexibleStringConverter(),
            new FlexibleDecimalConverter(),
            new FlexibleInt64Converter(),
            new FlexibleInt32Converter(),
            new SingleOrArrayConverter<string?>(),
            new SingleOrArrayConverter<long?>(),
            new SingleOrArrayConverter<int?>(),
        },
    };

    /// <summary>Options whose two big arrays stream element by element into the given callbacks.</summary>
    public static JsonSerializerOptions CreateStreamingInNetworkOptions(Action<ProviderReference> onReference, Action<InNetworkItem> onItem)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(ProviderReferenceSink))
            {
                info.CreateObject = () => new ProviderReferenceSink(onReference);
            }
            else if (info.Type == typeof(InNetworkItemSink))
            {
                info.CreateObject = () => new InNetworkItemSink(onItem);
            }
        });
        return CreateOptions(resolver);
    }

    /// <summary>Opens a cached download for reading, unwrapping gzip (many payers publish .json.gz) and skipping a UTF-8 BOM.</summary>
    public static Stream OpenForReading(Stream file)
    {
        Span<byte> head = stackalloc byte[3];
        var read = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        if (read >= 2 && head[0] == 0x1F && head[1] == 0x8B)
        {
            file.Seek(0, SeekOrigin.Begin);
            return new GZipStream(file, CompressionMode.Decompress);
        }

        var hasBom = read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        file.Seek(hasBom ? 3 : 0, SeekOrigin.Begin);
        return file;
    }
}

/// <summary>Accepts either a JSON array or a single value for list fields (e.g. "service_code": "11").</summary>
internal sealed class SingleOrArrayConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var elementConverter = (JsonConverter<T>)options.GetConverter(typeof(T));

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return [elementConverter.Read(ref reader, typeof(T), options)!];
        }

        var list = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            list.Add(elementConverter.Read(ref reader, typeof(T), options)!);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.ToArray(), options);
}
