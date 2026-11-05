using StaticNorth.Uhp.SchemaGen;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var generator = new SchemaGenerator(root);

if (args.Contains("--refresh"))
{
    await generator.RefreshAsync();
}

await generator.GenerateAsync(args.Contains("--check"));
