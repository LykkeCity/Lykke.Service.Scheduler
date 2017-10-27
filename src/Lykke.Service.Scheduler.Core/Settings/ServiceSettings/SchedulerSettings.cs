using System.Collections.Generic;

namespace Lykke.Service.Scheduler.Core.Settings.ServiceSettings
{
    public class SchedulerSettings
    {
        public DbSettings Db { get; set; }
        public ScheduledCall[] ScheduledCalls { get; set; }
    }

    public class ScheduledCall
    {
        public string CronExpression { get; set; }
        public string Url { get; set; }
    }


}

