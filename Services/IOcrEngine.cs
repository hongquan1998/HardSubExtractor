using System.Drawing;

namespace HardSubExtractor.Services
{
    public interface IOcrEngine : IDisposable
    {
        string Name { get; }
        bool IsAvailable();
        Task<(string text, float confidence)> RecognizeAsync(Bitmap image);
    }
}
