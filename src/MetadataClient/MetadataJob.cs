﻿using System;
using System.Text;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Collections;
using Newtonsoft.Json.Serialization;

namespace MetadataClient
{
    // We could totally get rid of this interface and use PackageOwnerAssertionSet
    // It just adds more clarity
    public interface IAssertionSet
    {
        string PackageId { get; }
        bool Exists { get; }
        HashSet<OwnerAssertion> Owners { get; set; }
    }

    // We could totally get rid of this interface and use PackageAssertionSet
    // It just adds more clarity
    public interface IPackageAssertionSet : IAssertionSet
    {
        string Version { get; }
    }
    /// <summary>
    /// NOTE THAT this assertion has the 'packageId' and 'Owners' information only
    /// This is the least common denominator of all assertions. Just the packageId and list of owners
    /// If the owners list is null or empty, only the packageId will be serialized
    /// This assertion will be directly used when there are only 'Remove Owner' assertions on a package
    /// Even there is 1 AddOwner assertion, its immediate derived class will be need to be used
    /// </summary>
    public class PackageOwnerAssertionSet : IAssertionSet
    {
        public PackageOwnerAssertionSet() {}
        internal PackageOwnerAssertionSet(string packageId)
        {
            PackageId = packageId;
        }

        public string PackageId { get; set; }

        public bool Exists
        {
            get
            {
                if (Owners == null || Owners.Count == 0)
                {
                    throw new InvalidOperationException("Owners cannot be null or empty");
                }
                return Owners.Where(o => o.Exists).FirstOrDefault() != null;
            }
        }

        public HashSet<OwnerAssertion> Owners { get; set; }

        public bool ShouldSerializeExists()
        {
            return Exists;
        }
    }

    /// <summary>
    /// NOTE THAT this assertion has the 'packageVersion' information in addition to the 'packageId', 'Owners' and 'Exists' information from its base classes
    /// This assertion will be directly used for a delete package assertion. If there are owner assertions, they will get added here as well. If not, they will be ignored
    /// This assertion does not contain the other information used during 'Add Package' or 'Edit Package' like 'Created Date' and so on
    /// </summary>
    public class PackageMinAssertionSet : IPackageAssertionSet
    {
        /// <summary>
        /// Adding a parameterless default constructor for supporting Dapper and have internal constructor for writing simple unit tests
        /// Could have added a constructor with a signature matching the sql query, but this is less code
        /// </summary>
        public PackageMinAssertionSet() {}
        internal PackageMinAssertionSet(string packageId, string version, bool exists)
        {
            PackageId = packageId;
            Version = version;
            Exists = exists;
        }

        [JsonProperty(Order = -2)]
        public string PackageId { get; set; }

        [JsonProperty(Order = -2)]
        public string Version { get; set; }

        [JsonProperty(Order = -2)]
        public bool Exists { get; set; }

        public HashSet<OwnerAssertion> Owners { get; set; }

        public bool ShouldSerializeOwners()
        {
            return Owners != null && Owners.Count > 0;
        }
    }

    /// <summary>
    /// This assertion is the full assertion containing all the possible fields and is used during 'Add Package' or 'Edit Package'
    /// As with all its base classes, the owners field will be ignored, if the Owners field is null or empty
    /// </summary>
    public class PackageAssertionSet : PackageMinAssertionSet
    {
        /// <summary>
        /// Adding a parameterless default constructor for supporting Dapper and have internal constructor for writing simple unit tests
        /// Could have added a constructor with a signature matching the sql query, but this is less code
        /// </summary>
        public PackageAssertionSet() { }
        internal PackageAssertionSet(string packageId, string version, bool exists) : base(packageId, version, exists) { }

        internal PackageAssertionSet(string packageId, string version, bool exists, string nupkg, bool listed, DateTime? created, DateTime? published)
            : base(packageId, version, exists)
        {
            Nupkg = nupkg;
            Listed = listed;
            Created = created;
            Published = published;
        }

        [JsonIgnore]
        public int Key { get; set; }

        public string Nupkg { get; set; }
        public bool Listed { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Published { get; set; }
    }

    public class OwnerAssertion
    {
        /// <summary>
        /// Adding a parameterless default constructor for supporting Dapper and have internal constructor for writing simple unit tests
        /// Could have added a constructor with a signature matching the sql query, but this is less code
        /// </summary>
        public OwnerAssertion() { }
        internal OwnerAssertion(string username, bool exists)
        {
            Username = username;
            Exists = exists;
        }
        public string Username { get; set; }
        public bool Exists { get; set; }

        public override bool Equals(object obj)
        {
            var other = obj as OwnerAssertion;
            return Exists == other.Exists && String.Equals(Username, other.Username, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            // Simplified: returning hash code of username only. Never will it be that the same user is both added and removed in an assertion set
            return Username.GetHashCode();
        }
    }

    public class PackageOwnerAssertion : OwnerAssertion
    {
        /// <summary>
        /// Adding a parameterless default constructor for supporting Dapper and have internal constructor for writing simple unit tests
        /// Could have added a constructor with a signature matching the sql query, but this is less code
        /// </summary>
        public PackageOwnerAssertion() { }

        internal PackageOwnerAssertion(string packageId, string version, string username, bool exists)
            : base(username, exists)
        {
            PackageId = packageId;
            Version = version;
        }
        [JsonIgnore]
        public int Key { get; set; }
        [JsonIgnore]
        public string PackageId { get; set; }
        [JsonIgnore]
        public string Version { get; set; }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    public static class AssertionQueries
    {
        public const string GetAssertionsQuery = @"DECLARE		@PackageAssertions TABLE
(
			[Key] int
		,	PackageId nvarchar(128)
		,	[Version] nvarchar(64)
)

DECLARE		@PackageOwnerAssertions TABLE
(
			[Key] int
		,	Username nvarchar(64)
		,	PackageId nvarchar(128)
		,	[Version] nvarchar(64)
)

DECLARE		@ProcessingDateTime datetime = GETUTCDATE()

BEGIN TRAN

UPDATE		LogPackages
SET			ProcessAttempts = ProcessAttempts + 1
		,	FirstProcessingDateTime = ISNULL(FirstProcessingDateTime, @ProcessingDateTime)
		,	LastProcessingDateTime = @ProcessingDateTime
OUTPUT		inserted.[Key]
		,	inserted.PackageId
		,	inserted.[Version]
INTO		@PackageAssertions
WHERE		ProcessedDateTime IS NULL

UPDATE		LogPackageOwners
SET			ProcessAttempts = ProcessAttempts + 1
		,	FirstProcessingDateTime = ISNULL(FirstProcessingDateTime, @ProcessingDateTime)
		,	LastProcessingDateTime = @ProcessingDateTime
OUTPUT		inserted.[Key]
		,	inserted.Username
		,	inserted.PackageId
		,	inserted.Version
INTO		@PackageOwnerAssertions
WHERE		ProcessedDateTime IS NULL

UPDATE		LogPackages
SET			ProcessedDateTime = @ProcessingDateTime
WHERE		[Key] NOT IN (SELECT MaxKey = MAX([Key])
			FROM		@PackageAssertions
			GROUP BY	PackageId
					,	[Version])

UPDATE		LogPackageOwners
SET			ProcessedDateTime = @ProcessingDateTime
WHERE		[Key] NOT IN (SELECT MaxKey = MAX([Key])
			FROM		@PackageOwnerAssertions
			GROUP BY	Username
					,	PackageId
					,	[Version])
COMMIT TRAN

SELECT		LogPackages.*
FROM		(
			SELECT		MaxKey = MAX([Key])
					,	PackageId
					,	[Version]
			FROM		@PackageAssertions
			GROUP BY	PackageId
					,	[Version]
			) PackageAssertions
INNER JOIN	LogPackages WITH (NOLOCK)
		ON	LogPackages.[Key] = PackageAssertions.MaxKey

SELECT		LogPackageOwners.*
FROM		(
			SELECT		MaxKey = MAX([Key])
					,	Username
					,	PackageId
					,	[Version]
			FROM		@PackageOwnerAssertions
			GROUP BY	Username
					,	PackageId
					,	[Version]
			) PackageOwnerAssertions
INNER JOIN	LogPackageOwners WITH (NOLOCK)
		ON	LogPackageOwners.[Key] = PackageOwnerAssertions.MaxKey";

        public const string MarkAssertionsQuery = @"DECLARE		@ProcessedDateTime datetime = GETUTCDATE()

UPDATE		LogPackages
SET			ProcessedDateTime = @ProcessedDateTime
WHERE		[Key] IN @packageAssertionKeys

UPDATE		LogPackageOwners
SET			ProcessedDateTime = @ProcessedDateTime
WHERE		[Key] IN @packageOwnerAssertionKeys";
    }

    public static class MetadataJob
    {
        // Formatting constants
        private const string EventsPrefix = "packageassertions/";
        private const string DateTimeFormat = "yyyy/MM/dd/HH-mm-ss-fffZ";
        private const string EventFileNameFormat = "{0}{1}.json";

        // File constants
        private const string IndexJson = "index.json";

        // Property Name constants
        private const string EventTimeStamp = "timestamp";
        private const string EventOlder = "older";
        private const string EventNewer = "newer";
        private const string EventLastUpdated = "lastupdated";
        private const string EventOldest = "oldest";
        private const string EventNewest = "newest";
        private const string EventNull = null;
        private const string EventAssertions = "assertions";
        private const string PackageId = "PackageId";
        private const string PackageVersion = "Version";
        private const string PackageNupkg = "nupkg";
        private const string PackageOwners = "owners";

        private static readonly JsonSerializerSettings DefaultJsonSerializerSettings = new JsonSerializerSettings() { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        private static readonly JObject EmptyIndexJSON = JObject.Parse(@"{
  '" + EventLastUpdated + @"': '',
  '" + EventOldest + @"': null,
  '" + EventNewest + @"': null
}");

        public static async Task Start(CloudStorageAccount blobAccount, CloudBlobContainer container, SqlConnectionStringBuilder sql, bool pushToCloud, bool updateTables)
        {
            Console.WriteLine("Started polling...");
            Console.WriteLine("Looking for changes in {0}/{1} ", sql.DataSource, sql.InitialCatalog);

            if (await container.CreateIfNotExistsAsync())
            {
                Console.WriteLine("Container created");
            }

            Container = container;
            PushToCloud = pushToCloud;
            UpdateTables = updateTables;

            // The blobAccount and container can potentially be used to put the trigger information
            // on package blobs or otherwise. Not Important now

            while (true)
            {
                try
                {
                    await DetectChanges(sql);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                Console.WriteLine('.');
                Thread.Sleep(3000);
            }
        }

        public static CloudBlobContainer Container
        {
            private get;
            set;
        }

        public static bool PushToCloud
        {
            get;
            private set;
        }

        public static bool UpdateTables
        {
            get;
            private set;
        }

        public static async Task<JObject> DetectChanges(SqlConnectionStringBuilder sql)
        {
            JObject json = null;

            try
            {
                using (var connection = await sql.ConnectTo())
                {
                    Console.WriteLine("Connected to database in {0}/{1} obtained: {2}", connection.DataSource, connection.Database, connection.ClientConnectionId);
                    Console.WriteLine("Querying multiple queries...");
                    var results = connection.QueryMultiple(AssertionQueries.GetAssertionsQuery);
                    Console.WriteLine("Completed multiple queries.");

                    Console.WriteLine("Extracting packageassertions and owner assertions...");
                    var packageAssertions = results.Read<PackageAssertionSet>();
                    var packageOwnerAssertions = results.Read<PackageOwnerAssertion>();

                    // Extract the assertions as JArray
                    var jArrayAssertions = GetJArrayAssertions(packageAssertions, packageOwnerAssertions);

                    if (jArrayAssertions.Count > 0)
                    {
                        var timeStamp = DateTime.UtcNow;
                        var indexJSONBlob = Container.GetBlockBlobReference(IndexJson);

                        JObject indexJSON = await GetJSON(indexJSONBlob) ?? (JObject)EmptyIndexJSON.DeepClone();

                        // Get Final JObject with timeStamp, previous, next links etc
                        json = GetJObject(jArrayAssertions, timeStamp, indexJSON);

                        var blobName = GetBlobName(timeStamp);

                        // Write the blob. Update indexJSON blob and previous latest Blob
                        await DumpJSON(json, blobName, timeStamp, indexJSON, indexJSONBlob);

                        if (UpdateTables)
                        {
                            // Mark assertions as processed
                            await MarkAssertionsAsProcessed(connection, packageAssertions, packageOwnerAssertions);
                        }
                        else
                        {
                            Console.WriteLine("Not Updating tables...");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No Assertions to make");
                        if (UpdateTables)
                        {
                            Console.WriteLine("And, not updating tables");
                        }
                        else
                        {
                            Console.WriteLine("Not updating tables anyways...");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            return json;
        }

        /// <summary>
        /// Gets the assertions as JArray from the packageAssertions and packageOwnerAssertions queried from the database
        /// This can be tested separately to verify that the right jArray of assertions are created using mocked assertions
        /// </summary>
        public static JArray GetJArrayAssertions(IEnumerable<PackageAssertionSet> packageAssertions, IEnumerable<PackageOwnerAssertion> packageOwnerAssertions)
        {
            // For every package assertion entry, create an entry in a simple dictionary of (<packageId, packageVersion>, IPackageAssertion)
            var packagesAndOwners = new Dictionary<Tuple<string, string>, IAssertionSet>();
            var ownersOnlyAssertions = new Dictionary<string, IAssertionSet>();
            foreach (var packageAssertion in packageAssertions)
            {
                var key = new Tuple<string, string>(packageAssertion.PackageId, packageAssertion.Version);
                if (packageAssertion.Exists)
                {
                    packagesAndOwners.Add(key, packageAssertion);
                }
                else
                {
                    // If exists is false, it means the package should be deleted
                    // Ignore all the other fields/columns
                    packagesAndOwners.Add(key, new PackageMinAssertionSet(packageAssertion.PackageId, packageAssertion.Version, false));
                }
            }

            // Now, for every packageMinOwnerAssertion created, connect the corresponding package owner assertions
            // If a packageMinOwnerAssertion is not present corresponding to the owner assertion(s),
            // they are owner only assertions. Add them to ownerAssertions list
            foreach (var packageOwnerAssertion in packageOwnerAssertions)
            {
                var key = new Tuple<string, string>(packageOwnerAssertion.PackageId, packageOwnerAssertion.Version);
                IAssertionSet assertionSet = null;
                if (!packagesAndOwners.TryGetValue(key, out assertionSet))
                {
                    var ownerKey = key.Item1;
                    if (!ownersOnlyAssertions.TryGetValue(ownerKey, out assertionSet))
                    {
                        assertionSet = ownersOnlyAssertions[ownerKey] = new PackageOwnerAssertionSet(packageOwnerAssertion.PackageId);
                    }
                }
                if (assertionSet.Owners == null)
                {
                    assertionSet.Owners = new HashSet<OwnerAssertion>();
                }

                if (!assertionSet.Owners.Add(packageOwnerAssertion))
                {
                    Console.WriteLine("PackageOwnerAssertion already exists");
                }
            }

            var assertionSets = packagesAndOwners.Values.Concat(ownersOnlyAssertions.Values);

            var json = JsonConvert.SerializeObject(assertionSets, Formatting.Indented, DefaultJsonSerializerSettings);
            return JArray.Parse(json);
        }

        private static async Task<JObject> GetJSON(CloudBlockBlob blob)
        {
            if (await blob.ExistsAsync())
            {
                try
                {
                    var json = await blob.DownloadTextAsync();
                    return JObject.Parse(json);
                }
                catch (StorageException ex)
                {
                    Console.WriteLine("Azure Storage Exception : " + ex.ToString());
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the final JObject given the assertions as jArray, timeStamp and indexJSON
        /// This can be tested separately to verify that the index is used and updated correctly using a mocked indexJSON JObject
        /// </summary>
        public static JObject GetJObject(JArray jArrayAssertions, DateTime timeStamp, JObject indexJSON)
        {
            var json = new JObject();
            json.Add(EventTimeStamp, timeStamp);
            if (indexJSON == null)
            {
                json.Add(EventOlder, EventNull);
            }
            else
            {
                var eventOlder = indexJSON.SelectToken(EventNewest);
                if (eventOlder == null)
                {
                    throw new ArgumentException("indexJSON does not have a token 'newest'");
                }
                Console.WriteLine("Event newest in empty index json is :" + eventOlder.ToString());
                json.Add(EventOlder, eventOlder.Type == JTokenType.Null ? EventNull : eventOlder.ToString());
            }
            json.Add(EventNewer, EventNull);
            json.Add(EventAssertions, jArrayAssertions);
            return json;
        }

        public static string GetBlobName(DateTime timeStamp)
        {
            return String.Format(EventFileNameFormat, EventsPrefix, timeStamp.ToString(DateTimeFormat));
        }

        /// <summary>
        /// This function simply dumps the json onto console and to the blob if applicable
        /// </summary>
        private static async Task DumpJSON(JObject json, string blobName, DateTime timeStamp, JObject indexJSON, CloudBlockBlob indexJSONBlob)
        {
            if(json == null)
            {
                throw new ArgumentNullException("json");
            }

            if(indexJSON == null)
            {
                throw new ArgumentNullException("indexJSON");
            }

            Console.WriteLine("BlobName: {0}\n", blobName);

            Console.WriteLine("index.json PREVIOUS: \n" + indexJSON.ToString());
            if (PushToCloud)
            {
                Console.WriteLine("Dumping to {0}", blobName);
                var latestBlob = Container.GetBlockBlobReference(blobName);

                // First upload the created block
                using (var stream = new MemoryStream(Encoding.Default.GetBytes(json.ToString()), false))
                {
                    await latestBlob.UploadFromStreamAsync(stream);
                }

                string oldestBlobName = null;
                string previousLatestBlobName = null;
                oldestBlobName = indexJSON.SelectToken(EventOldest).ToString();
                previousLatestBlobName = indexJSON.SelectToken(EventNewest).ToString();

                // Update the previous latest block
                if(String.IsNullOrEmpty(previousLatestBlobName))
                {
                    if(!String.IsNullOrEmpty(oldestBlobName))
                    {
                        Console.WriteLine("WARNING: OldestBlobName is not empty when newestBlobName is. Something went wrong somewhere!!!");
                    }
                    // Both the oldest and newest event blob names are empty
                    // Set the oldest now
                    indexJSON[EventOldest] = blobName;
                }
                else
                {
                    CloudBlockBlob previousLatestBlob = Container.GetBlockBlobReference(previousLatestBlobName);
                    JObject previousLatestJSON = await GetJSON(previousLatestBlob);
                    if (previousLatestJSON == null)
                    {
                        throw new InvalidOperationException("Previous latest blob does not exist");
                    }

                    previousLatestJSON[EventNewer] = blobName;
                    // Finally, upload the index block
                    using (var stream = new MemoryStream(Encoding.Default.GetBytes(previousLatestJSON.ToString()), false))
                    {
                        await previousLatestBlob.UploadFromStreamAsync(stream);
                    }
                    Console.WriteLine("Previous Latest Blob: \n" + previousLatestJSON.ToString());
                }

                // TODO: Should we store the URL instead?
                indexJSON[EventNewest] = blobName;
                indexJSON[EventLastUpdated] = timeStamp;

                // Finally, upload the index block
                using (var stream = new MemoryStream(Encoding.Default.GetBytes(indexJSON.ToString()), false))
                {
                    await indexJSONBlob.UploadFromStreamAsync(stream);
                }
            }
            else
            {
                Console.WriteLine("Not Dumping to cloud...\n");
            }
            Console.WriteLine(json);
            Console.WriteLine("index.json NEW: \n" + indexJSON.ToString());
        }

        private static async Task MarkAssertionsAsProcessed(SqlConnection connection, IEnumerable<PackageAssertionSet> packageAssertions,
            IEnumerable<PackageOwnerAssertion> packageOwnerAssertions)
        {
            var allPackageAssertionKeys = (from packageAssertion in packageAssertions
                                       select packageAssertion.Key).ToList();

            var allPackageOwnerAssertionKeys = (from packageOwnerAssertion in packageOwnerAssertions
                                            select packageOwnerAssertion.Key).ToList();

            // NOTE THAT the number of keys in the 'IN' clause of a SQL query is restricted to 2100
            // Let us keep it simple and restrict the number to 1000. Since there are 2 IN queries,
            // 1 for package assertions and 1 for package owner assertions, set the limit as 500
            var maxINClauseKeysCount = 500;
            var index = 0;

            var packageAssertionsCount = allPackageAssertionKeys.Count;
            var ownerAssertionsCount = allPackageOwnerAssertionKeys.Count;
            while (index < packageAssertionsCount || index < ownerAssertionsCount)
            {
                var packageAssertionKeys = index < packageAssertionsCount ?
                allPackageAssertionKeys.GetRange(index, Math.Min(maxINClauseKeysCount, packageAssertionsCount - index)) : new List<int>();
                var packageOwnerAssertionKeys = index < ownerAssertionsCount ?
                allPackageOwnerAssertionKeys.GetRange(index, Math.Min(maxINClauseKeysCount, ownerAssertionsCount - index)) : new List<int>();

                await connection.QueryAsync<int>(AssertionQueries.MarkAssertionsQuery,
                    new { packageAssertionKeys = packageAssertionKeys, packageOwnerAssertionKeys = packageOwnerAssertionKeys });

                index += maxINClauseKeysCount;
            }
        }
    }
}