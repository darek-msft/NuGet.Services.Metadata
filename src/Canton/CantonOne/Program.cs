﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Canton;
using NuGet.Services.Metadata.Catalog.Persistence;
using Microsoft.WindowsAzure.Storage;

namespace NuGet.Canton.One
{
    class Program
    {
        /// <summary>
        /// Canton jobs that can only run as single instances.
        /// </summary>
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine(".exe <config path>");
                Environment.Exit(1);
            }

            CantonUtilities.Init();

            Config config = new Config(args[0]);

            CloudStorageAccount account = CloudStorageAccount.Parse(config.GetProperty("StorageConnectionString"));

            Queue<CantonJob> jobs = new Queue<CantonJob>();

            // set up the storage account
            jobs.Enqueue(new InitStorageJob(config));

            // read the gallery to find new packages
            //jobs.Enqueue(new QueueNewPackagesFromGallery(config, new AzureStorage(account, config.GetProperty("GalleryPageContainer"))));

            // tmp
            jobs.Enqueue(new CatalogPageJob(config, new AzureStorage(account, config.GetProperty("tmp")), CantonConstants.GalleryPagesQueue));

            // commit pages to the catalog
            jobs.Enqueue(new CatalogPageCommitJob(config, new AzureStorage(account, config.GetProperty("CatalogContainer"))));

            // create registration blobs
            jobs.Enqueue(new RegistrationJob(config, new AzureStorage(account, config.GetProperty("RegistrationContainer")), new AzureStorageFactory(account, config.GetProperty("RegistrationContainer"))));

            Stopwatch timer = new Stopwatch();

            // avoid flooding the gallery
            TimeSpan minWait = TimeSpan.FromMinutes(2);

            while (true)
            {
                timer.Restart();
                CantonUtilities.RunJobs(jobs);

                TimeSpan waitTime = minWait.Subtract(timer.Elapsed);

                Console.WriteLine("Completed jobs in: " + timer.Elapsed);

                if (waitTime.TotalMilliseconds > 0)
                {
                    Console.WriteLine("Sleeping: " + waitTime.TotalSeconds + "s");
                    Thread.Sleep(waitTime);
                }
            }
        }
    }
}
