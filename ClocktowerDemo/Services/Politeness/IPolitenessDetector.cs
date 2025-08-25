using System.Threading;
using System.Threading.Tasks;
using ClocktowerDemo.Domain;

namespace ClocktowerDemo.Services.Politeness
{
    public interface IPolitenessDetector
    {
        Task<PolitenessResult> ClassifyAsync(string text, CancellationToken ct);
    }
}
