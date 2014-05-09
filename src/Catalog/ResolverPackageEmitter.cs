﻿using Catalog.Persistence;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using VDS.RDF;
using VDS.RDF.Query;

namespace Catalog
{
    public class ResolverPackageEmitter : CountingPackageEmitter
    {
        int _resolverResourceCount = 0;
        int _mergedResourceCount = 0;

        Storage _storage;
        JObject _resolverFrame;
        int _currentBatchSize = 0;
        int _maxBatchSize;
        TripleStore _currentStore;
        ActionBlock<TripleStore> _actionBlock = null;

        HashSet<string> _beforePackageIds = new HashSet<string>();
        HashSet<string> _afterPackageIds = new HashSet<string>();

        public ResolverPackageEmitter(Storage storage, int maxBatchSize = 1000)
        {
            Options.InternUris = false;

            _resolverFrame = JObject.Parse(Utils.GetResource("context.ResolverFrame.json"));

            _storage = storage;
            _maxBatchSize = maxBatchSize;

            _actionBlock = new ActionBlock<TripleStore>(async (tripleStore) =>
            {
                Console.WriteLine("received {0:N0} triple store", tripleStore.Triples.Count());

                await Process(tripleStore);
            },
            new ExecutionDataflowBlockOptions
            { 
                MaxDegreeOfParallelism = 1,         //  we currently do not lock the storage blobs
                BoundedCapacity = 10
            });
        }

        protected override async Task EmitPackage(JObject package)
        {
            await base.EmitPackage(package);

            lock (this)
            {
                _beforePackageIds.Add(package["id"].ToString().ToLowerInvariant());
            }

            IGraph graph = Utils.CreateGraph(package);

            TripleStore result = null;

            lock (this)
            {
                if (_currentStore == null)
                {
                    _currentStore = new TripleStore();
                }

                _currentStore.Add(graph, true);

                if (_currentBatchSize++ == _maxBatchSize)
                {
                    Console.WriteLine("post {0:N0} triple store", _currentStore.Triples.Count());

                    result = _currentStore;

                    _currentBatchSize = 0;
                    _currentStore = null;
                }
            }

            if (result != null)
            {
                bool success = await _actionBlock.SendAsync(result);
                if (success == false)
                {
                    throw new Exception("_actionBlock.SendAsync false");
                }
            }
        }

        public override async Task Close()
        {
            if (_currentBatchSize > 0)
            {
                bool success = await _actionBlock.SendAsync(_currentStore);
                if (success == false)
                {
                    throw new Exception("_actionBlock.SendAsync false");
                }
            }

            _actionBlock.Complete();
            await _actionBlock.Completion;

            Console.WriteLine("created {0} resolver resources", _resolverResourceCount);
            Console.WriteLine("merged {0} resolver resources", _mergedResourceCount);

            Console.WriteLine("[before] {0} distinct package ids", _beforePackageIds.Count);
            Console.WriteLine("[after] {0} distinct package ids", _afterPackageIds.Count);

            await base.Close();
        }

        async Task Process(TripleStore store)
        {
            try
            {
                //Console.WriteLine("process {0:N0} triples (memory: {1:N0} bytes)", store.Triples.Count(), GC.GetTotalMemory(true));

                SparqlResultSet distinctIds = SparqlHelpers.Select(store, Utils.GetResource("sparql.SelectDistinctPackage.rq"));

                IDictionary<Uri, IGraph> resolverResources = new Dictionary<Uri, IGraph>();

                foreach (SparqlResult row in distinctIds)
                {
                    string id = row["id"].ToString();

                    lock (this)
                    {
                        _afterPackageIds.Add(id);
                    }

                    SparqlParameterizedString sparql = new SparqlParameterizedString();
                    sparql.CommandText = Utils.GetResource("sparql.ConstructResolverGraph.rq");

                    string baseAddress = _storage.BaseAddress + _storage.Container + "/resolver/";

                    sparql.SetLiteral("id", id);
                    sparql.SetLiteral("base", baseAddress);
                    sparql.SetLiteral("extension", ".json");

                    IGraph packageRegistration = SparqlHelpers.Construct(store, sparql.ToString());

                    Uri registrationUri = new Uri(baseAddress + id.ToLowerInvariant() + ".json");

                    resolverResources.Add(registrationUri, packageRegistration);
                }

                if (resolverResources.Count != distinctIds.Count)
                {
                    Console.WriteLine("\t{0} {1}", resolverResources.Count, distinctIds.Count);
                    throw new Exception("resolverResources.Count != distinctIds.Count");
                }

                await MergeAll(resolverResources);

                Interlocked.Add(ref _resolverResourceCount, resolverResources.Count);

                //Console.WriteLine(" [created {0} merged {1}]", _resolverResourceCount, _mergedResourceCount);
            }
            finally
            {
                store.Dispose();
            }
        }

        async Task MergeAll(IDictionary<Uri, IGraph> resolverResources)
        {
            List<Task> tasks = new List<Task>();
            foreach (KeyValuePair<Uri, IGraph> resolverResource in resolverResources)
            {
                tasks.Add(Task.Run(async () => { await Merge(resolverResource); }));
            }
            await Task.WhenAll(tasks.ToArray());
        }

        async Task Merge(KeyValuePair<Uri, IGraph> resource)
        {
            string existingJson = await _storage.Load(resource.Key);
            if (existingJson != null)
            {
                IGraph existingGraph = Utils.CreateGraph(existingJson);
                resource.Value.Merge(existingGraph);

                Interlocked.Increment(ref _mergedResourceCount);
            }

            string content = Utils.CreateJson(resource.Value, _resolverFrame);
            await _storage.Save("application/json", resource.Key, content);
        }
    }
}