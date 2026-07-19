namespace Ukiyoe;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/Ukiyoe;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
