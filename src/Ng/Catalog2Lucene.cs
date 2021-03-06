﻿using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using NuGet.Services.Metadata.Catalog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Ng
{
    public static class Catalog2Lucene
    {
        static async Task Loop(string source, string registration, Lucene.Net.Store.Directory directory, bool verbose, int interval)
        {
            Func<HttpMessageHandler> handlerFunc = CommandHelpers.GetHttpMessageHandlerFactory(verbose);

            CommitCollector collector = new SearchIndexFromCatalogCollector(new Uri(source), directory, handlerFunc);

            ReadWriteCursor front = new LuceneCursor(directory, MemoryCursor.Min.Value);
            
            ReadCursor back = (registration == null) ? (ReadCursor)MemoryCursor.Max : new HttpReadCursor(new Uri(registration), handlerFunc);

            while (true)
            {
                bool run = false;
                do
                {
                    run = await collector.Run(front, back);
                }
                while (run);

                Thread.Sleep(interval * 1000);
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: ng catalog2lucene -source <catalog> [-registration <registration-root>] -luceneDirectoryType file|azure [-lucenePath <file-path>] | [-luceneStorageAccountName <azure-acc> -luceneStorageKeyValue <azure-key> -luceneStorageContainer <azure-container>] [-verbose true|false] [-interval <seconds>]");
        }

        public static void Run(string[] args)
        {
            IDictionary<string, string> arguments = CommandHelpers.GetArguments(args, 1);
            if (arguments == null)
            {
                PrintUsage();
                return;
            }

            Lucene.Net.Store.Directory directory = CommandHelpers.GetLuceneDirectory(arguments);
            if (directory == null)
            {
                PrintUsage();
                return;
            }

            string source = CommandHelpers.GetSource(arguments);
            if (source == null)
            {
                PrintUsage();
                return;
            }

            bool verbose = CommandHelpers.GetVerbose(arguments);

            if (verbose)
            {
                Trace.Listeners.Add(new ConsoleTraceListener());
                Trace.AutoFlush = true;
            }

            int interval = CommandHelpers.GetInterval(arguments);

            string registration = CommandHelpers.GetRegistration(arguments);

            if (registration == null)
            {
                Console.WriteLine("Lucene index will be created up to the end of the catalog (alternatively if you provide a registration it will not pass that)");
            }

            Trace.TraceInformation("CONFIG source: \"{0}\" registration: \"{1}\" interval: {2} seconds", source, registration ?? "(null)", interval);

            Loop(source, registration, directory, verbose, interval).Wait();
        }
    }
}
