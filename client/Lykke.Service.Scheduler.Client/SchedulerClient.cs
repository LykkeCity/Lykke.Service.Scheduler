using System;
using Common.Log;

namespace Lykke.Service.Scheduler.Client
{
    public class SchedulerClient : ISchedulerClient, IDisposable
    {
        private readonly ILog _log;

        public SchedulerClient(string serviceUrl, ILog log)
        {
            _log = log;
        }

        public void Dispose()
        {
            //if (_service == null)
            //    return;
            //_service.Dispose();
            //_service = null;
        }
    }
}
