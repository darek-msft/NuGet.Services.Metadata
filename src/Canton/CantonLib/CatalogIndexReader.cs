﻿using Newtonsoft.Json.Linq;
using NuGet.Services.Metadata.Catalog.Collecting;
using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Canton
{
    public class CatalogIndexReader
    {
        private readonly Uri _indexUri;
        private readonly CollectorHttpClient _httpClient;

        public CatalogIndexReader(Uri indexUri)
            : this(indexUri, new CollectorHttpClient())
        {

        }

        public CatalogIndexReader(Uri indexUri, CollectorHttpClient httpClient)
        {
            _indexUri = indexUri;
            _httpClient = httpClient;
        }


        public async Task<IEnumerable<CatalogIndexEntry>> GetEntries()
        {
            JObject index = await _httpClient.GetJObjectAsync(_indexUri);

            List<Tuple<DateTime, Uri>> pages = new List<Tuple<DateTime, Uri>>();

            foreach (var item in index["items"])
            {
                pages.Add(new Tuple<DateTime, Uri>(DateTime.Parse(item["commitTimeStamp"].ToString()), new Uri(item["@id"].ToString())));
            }

            return GetEntries(pages.Select(p => p.Item2));
        }


        private ConcurrentBag<CatalogIndexEntry> GetEntries(IEnumerable<Uri> pageUris)
        {
            ConcurrentBag<CatalogIndexEntry> entries = new ConcurrentBag<CatalogIndexEntry>();

            ParallelOptions options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 8;

            Parallel.ForEach(pageUris.ToArray(), options, uri =>
            {
                var task = _httpClient.GetJObjectAsync(uri);
                task.Wait();

                JObject json = task.Result;

                foreach (var item in json["items"])
                {
                    var entry = new CatalogIndexEntry(new Uri(item["@id"].ToString()),
                            item["@type"].ToString(),
                            item["commitId"].ToString(),
                            DateTime.Parse(item["commitTimeStamp"].ToString()),
                            item["nuget:id"].ToString(),
                            NuGetVersion.Parse(item["nuget:version"].ToString()));

                    entries.Add(entry);
                }
            });

            return entries;
        }

    }
}