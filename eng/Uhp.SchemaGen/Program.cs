using Uhp.SchemaGen;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var generator = new SchemaGenerator(root);

if (args.Contains("--refresh"))
{
    await generator.RefreshAsync();
}

generator.Generate(args.Contains("--check"));
