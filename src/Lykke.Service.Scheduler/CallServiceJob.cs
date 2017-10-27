using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Quartz;
using Flurl.Http;

namespace Lykke.Service.Scheduler
{
    public class CallServiceJob : Quartz.IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            string url = context.JobDetail.JobDataMap["Url"] as string;
            await url.GetAsync();
        }
    }
}
