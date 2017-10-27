using System.Threading.Tasks;

namespace Lykke.Service.Scheduler.Core.Services
{
    public interface IShutdownManager
    {
        Task StopAsync();
    }
}