using System.Threading.Tasks;

namespace Lykke.Service.Scheduler.Core.Services
{
    public interface IStartupManager
    {
        Task StartAsync();
    }
}