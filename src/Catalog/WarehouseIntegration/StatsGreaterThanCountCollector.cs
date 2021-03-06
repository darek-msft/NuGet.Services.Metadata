﻿using System;
using System.Net.Http;

namespace NuGet.Services.Metadata.Catalog.WarehouseIntegration
{
    public class StatsGreaterThanCountCollector : StatsCountCollector
    {
        DateTime _minDownloadTimeStamp;

        public StatsGreaterThanCountCollector(Uri index, DateTime minDownloadTimeStamp, Func<HttpMessageHandler> handlerFunc = null, int batchSize = 200)
            : base(index, handlerFunc, batchSize)
        {
            _minDownloadTimeStamp = minDownloadTimeStamp;
        }

        protected override bool SelectItem(DateTime itemMinDownloadTimestamp, DateTime itemMaxDownloadTimestamp)
        {
            return (_minDownloadTimeStamp < itemMaxDownloadTimestamp);
        }

        protected override bool SelectRow(DateTime rowDownloadTimestamp)
        {
            return (rowDownloadTimestamp > _minDownloadTimeStamp);
        }
    }
}
