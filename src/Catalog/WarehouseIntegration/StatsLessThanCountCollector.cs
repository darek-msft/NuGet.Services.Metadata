﻿using System;
using System.Net.Http;

namespace NuGet.Services.Metadata.Catalog.WarehouseIntegration
{
    public class StatsLessThanCountCollector : StatsCountCollector
    {
        DateTime _downloadTimeStamp;

        public StatsLessThanCountCollector(Uri index, DateTime downloadTimeStamp, HttpMessageHandler handler = null, int batchSize = 200)
            : base(index, handler, batchSize)
        {
            _downloadTimeStamp = downloadTimeStamp;
        }

        protected override bool SelectItem(DateTime itemMinDownloadTimestamp, DateTime itemMaxDownloadTimestamp)
        {
            return (_downloadTimeStamp > itemMinDownloadTimestamp);
        }

        protected override bool SelectRow(DateTime rowDownloadTimestamp)
        {
            return (rowDownloadTimestamp < _downloadTimeStamp);
        }
    }
}
