﻿using NuGet.Services.Metadata.Catalog;
using System;
using System.Diagnostics;
using System.Linq;

namespace Ng
{
    class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Usage: ng [feed2catalog|catalog2registration|catalog2lucene|frameworkcompatibility|copylucene|checklucene|clearlucene]");
        }

        static void Main(string[] args)
        {
            if (args.Length > 0 && String.Equals("dbg", args[0], StringComparison.OrdinalIgnoreCase))
            {
                args = args.Skip(1).ToArray();
                Debugger.Launch();
            }

            try
            {
                if (args.Length == 0)
                {
                    PrintUsage();
                    return;
                }

                switch (args[0])
                {
                    case "feed2catalog" :
                        Feed2Catalog.Run(args);
                        break;
                    case "catalog2registration" :
                        Catalog2Registration.Run(args);
                        break;
                    case "catalog2lucene" :
                        Catalog2Lucene.Run(args);
                        break;
                    case "frameworkcompatibility":
                        FrameworkCompatibility.Run(args);
                        break;
                    case "copylucene":
                        CopyLucene.Run(args);
                        break;
                    case "checklucene":
                        CheckLucene.Run(args);
                        break;
                    case "clearlucene":
                        ResetLucene.Run(args);
                        break;
                    default:
                        PrintUsage();
                        break;
                }
            }
            catch (Exception e)
            {
                Trace.Listeners.Add(new ConsoleTraceListener());
                Trace.AutoFlush = true;

                Utils.TraceException(e);
            }
            Trace.Close();
        }
    }
}
