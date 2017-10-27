using Lykke.Service.Scheduler.Core.Settings.ServiceSettings;
using Lykke.Service.Scheduler.Core.Settings.SlackNotifications;

namespace Lykke.Service.Scheduler.Core.Settings
{
    public class AppSettings
    {
        public SchedulerSettings SchedulerService { get; set; }
        public SlackNotificationsSettings SlackNotifications { get; set; }
    }
}

