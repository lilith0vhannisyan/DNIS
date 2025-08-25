using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClocktowerDemo.Services.AI
{
    public interface IAIProvider
    {
        Task<JsonElement> RoleplayAsync(object payload, CancellationToken ct);
    }
}
