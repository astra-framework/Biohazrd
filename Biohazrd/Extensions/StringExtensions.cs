namespace Biohazrd.Extensions;

internal static class StringExtensions
{
    internal static string RemoveFileExtension(this string fileName)
    {
        int lastDotIndex = fileName.LastIndexOf('.');
        return lastDotIndex == -1 ? fileName : fileName[..lastDotIndex];
    }
}
