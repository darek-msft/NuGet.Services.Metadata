﻿using Newtonsoft.Json.Linq;
using NuGet.Services.Metadata.Catalog.Collecting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NuGet.Services.Metadata.Catalog.WarehouseIntegration
{
    public abstract class StatsCountCollector : BatchCollector
    {
        public StatsCountCollector()
            : base(200)
        {
            Count = 0;
        }

        public int Count { get; private set; }

        protected abstract bool SelectItem(DateTime itemMinDownloadTimestamp, DateTime itemMaxDownloadTimestamp);

        protected abstract bool SelectRow(DateTime rowDownloadTimestamp);

        protected async override Task<bool> ProcessBatch(CollectorHttpClient client, IList<JObject> items, JObject context)
        {
            List<Task<string>> tasks = new List<Task<string>>();

            foreach (JObject item in items)
            {
                DateTime itemMinDownloadTimestamp = item["http://nuget.org/schema#minDownloadTimestamp"]["@value"].ToObject<DateTime>();
                DateTime itemMaxDownloadTimestamp = item["http://nuget.org/schema#maxDownloadTimestamp"]["@value"].ToObject<DateTime>();

                if (SelectItem(itemMinDownloadTimestamp, itemMaxDownloadTimestamp))
                {
                    Uri itemUri = item["url"].ToObject<Uri>();
                    tasks.Add(client.GetStringAsync(itemUri));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks.ToArray());

                foreach (Task<string> task in tasks)
                {
                    JArray statisticsPage = JArray.Parse(task.Result);

                    foreach (JArray row in statisticsPage)
                    {
                        DateTime rowTimeStamp = row[1].ToObject<DateTime>();

                        if (SelectRow(rowTimeStamp))
                        {
                            Count++;
                        }
                    }
                }
            }

            return true;
        }
    }
}
