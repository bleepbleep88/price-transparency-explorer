using PriceExplorer.ReferenceBuilder;

// Builds the small reference files the API ships with (see reference/README.md).
//
//   dotnet run --project tools/PriceExplorer.ReferenceBuilder -- providers --database .data/explorer.duckdb
//   dotnet run --project tools/PriceExplorer.ReferenceBuilder -- codes
//
// "providers": names, specialties and locations for every NPI in the loaded rate files, from CMS's monthly NPPES file
// (streamed straight out of the ~1.1 GB zip), NUCC taxonomy names, and Census ZIP-to-county data.
// "codes": CMS HCPCS Level II names (newest quarterly file) and CMS RBCS categories (newest taxonomy file).

var command = args.FirstOrDefault();
var options = Arguments.Parse(args.Skip(1));

try
{
    switch (command)
    {
        case "providers":
            await ProviderDirectoryBuilder.RunAsync(
                database: options.Required("--database"),
                output: options.Optional("--out") ?? Path.Combine("reference", "provider_directory.csv.gz"),
                cache: options.Optional("--cache") ?? Path.Combine(".data", "reference-cache"),
                CancellationToken.None);
            return 0;
        case "codes":
            await CodeSetsBuilder.RunAsync(
                outputDirectory: options.Optional("--out") ?? "reference",
                cache: options.Optional("--cache") ?? Path.Combine(".data", "reference-cache"),
                CancellationToken.None);
            return 0;
        default:
            Console.Error.WriteLine("Usage: PriceExplorer.ReferenceBuilder providers --database <explorer.duckdb> [--out reference/provider_directory.csv.gz] [--cache .data/reference-cache]");
            Console.Error.WriteLine("       PriceExplorer.ReferenceBuilder codes [--out reference] [--cache .data/reference-cache]");
            return 2;
    }
}
catch (Exception ex) when (ex is ArgumentException or InvalidDataException or HttpRequestException or IOException)
{
    Console.Error.WriteLine($"Failed: {ex.Message}");
    return 1;
}

internal sealed class Arguments(Dictionary<string, string> values)
{
    public static Arguments Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                key = arg;
            }
            else if (key is not null)
            {
                values[key] = arg;
                key = null;
            }
        }

        return new Arguments(values);
    }

    public string Required(string name) =>
        values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing {name}.");

    public string? Optional(string name) => values.GetValueOrDefault(name);
}
