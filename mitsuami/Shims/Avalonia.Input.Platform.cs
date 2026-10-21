using System.Threading.Tasks;

namespace Avalonia.Input.Platform
{
    /// <summary>What <c>Apply</c> reads a patch from. The view gives it one
    /// over mitsuami's clipboard service.</summary>
    public interface IClipboard
    {
        Task<string> TryGetTextAsync();
    }
}
